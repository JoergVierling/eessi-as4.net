﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Extensions;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Model.PMode;
using Eu.EDelivery.AS4.Receivers.PullRequest;
using Eu.EDelivery.AS4.Serialization;
using log4net;

namespace Eu.EDelivery.AS4.Receivers
{
    /// <summary>
    /// <see cref="IReceiver" /> implementation to pull exponentially for Pull Requests.
    /// </summary>
    public class PullRequestReceiver : ExponentialIntervalReceiver<PModePullRequest>
    {
        private readonly IConfig _configuration;

        private Func<PModePullRequest, Task<MessagingContext>> _messageCallback;

        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        /// <summary>
        /// Initializes a new instance of the <see cref="PullRequestReceiver" /> class.
        /// </summary>
        public PullRequestReceiver() : this(Config.Instance) {}

        /// <summary>
        /// Initializes a new instance of the <see cref="PullRequestReceiver" /> class.
        /// </summary>
        /// <param name="configuration"><see cref="IConfig" /> implementation to collection PModes.</param>
        public PullRequestReceiver(IConfig configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            _configuration = configuration;
        }

        /// <summary>
        /// Configure the receiver with a given settings dictionary.
        /// </summary>
        /// <param name="settings"></param>
        public override void Configure(IEnumerable<Setting> settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
        }

        /// <summary>
        /// Start receiving on a configured Target
        /// Received messages will be send to the given Callback
        /// </summary>
        /// <param name="messageCallback"></param>
        /// <param name="cancellationToken"></param>
        /// <exception cref="Exception">A delegate callback throws an exception.</exception>
        public override void StartReceiving(
            Func<ReceivedMessage, CancellationToken, Task<MessagingContext>> messageCallback,
            CancellationToken cancellationToken)
        {
            if (messageCallback == null)
            {
                throw new ArgumentNullException(nameof(messageCallback));
            }

            _messageCallback = async message =>
            {
                var receivedMessage = new ReceivedMessage(
                    underlyingStream: await AS4XmlSerializer.ToStreamAsync(message.PMode), 
                    contentType: Constants.ContentTypes.Soap,
                    origin: message.PMode?.PushConfiguration?.Protocol?.Url ?? "unknown");

                return await messageCallback(receivedMessage, cancellationToken);
            };

            // Wait some time till the Kernel is fully started
            Thread.Sleep(TimeSpan.FromSeconds(5));

            StartInterval();
        }

        /// <summary>
        /// <paramref name="intervalPullRequest" /> is received.
        /// </summary>
        /// <param name="intervalPullRequest"></param>
        /// <returns></returns>
        /// <exception cref="Exception">A delegate callback throws an exception.</exception>
        protected override async Task<Interval> OnRequestReceived(PModePullRequest intervalPullRequest)
        {
            MessagingContext resultedMessage = await _messageCallback(intervalPullRequest).ConfigureAwait(false);

            try
            {
                bool isUserMessage = resultedMessage.AS4Message?.IsUserMessage == true;
                Interval intervalResult = isUserMessage ? Interval.Reset : Interval.Increase;
                Logger.Info($"PullRequest result in {Config.Encode((isUserMessage ? "UserMessage" : "Error"))} next interval will be \"{Config.Encode(intervalResult)}\"");

                return intervalResult;
            }
            finally
            {
                resultedMessage?.Dispose();
            }
        }
    }
}