﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Exceptions;
using Eu.EDelivery.AS4.Extensions;
using Eu.EDelivery.AS4.Factories;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Model.PMode;
using Eu.EDelivery.AS4.Serialization;
using Eu.EDelivery.AS4.Streaming;
using Eu.EDelivery.AS4.Utilities;
using log4net;

namespace Eu.EDelivery.AS4.Transformers
{
    public class ReceiveMessageTransformer : ITransformer
    {
        private readonly IConfig _config;

        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        private IDictionary<string, string> _properties;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReceiveMessageTransformer"/> class.
        /// </summary>
        public ReceiveMessageTransformer() : this(Config.Instance) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReceiveMessageTransformer"/> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        public ReceiveMessageTransformer(IConfig configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            _config = configuration;
        }

        public const string ReceivingPModeKey = "ReceivingPMode";

        [Info("Receiving Processing Mode", required: false, type: "receivingpmode")]
        [Description("ReceivingPMode identifier that defines the PMode that must be used while processing a received AS4 Message")]
        private string ReceivingPMode => _properties?.ReadOptionalProperty(ReceivingPModeKey);

        /// <summary>
        /// Configures the <see cref="ITransformer"/> implementation with specific user-defined properties.
        /// </summary>
        /// <param name="properties">The properties.</param>
        public void Configure(IDictionary<string, string> properties)
        {
            _properties = properties;
        }

        /// <summary>
        /// Transform a given <see cref="ReceivedMessage"/> to a Canonical <see cref="MessagingContext"/> instance.
        /// </summary>
        /// <param name="message">Given message to transform.</param>
        /// <returns></returns>
        public async Task<MessagingContext> TransformAsync(ReceivedMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (message.UnderlyingStream == null)
            {
                throw new InvalidMessageException(
                    "The incoming stream is not an ebMS Message. " +
                    "Only ebMS messages conform with the AS4 Profile are supported.");
            }

            if (!ContentTypeSupporter.IsContentTypeSupported(message.ContentType))
            {
                throw new InvalidMessageException(
                    $"ContentType is not supported {message.ContentType}{Environment.NewLine}" +
                    $"Supported ContentTypes are {Constants.ContentTypes.Soap} and {Constants.ContentTypes.Mime}");
            }

            ReceivedMessage rm = await EnsureIncomingStreamIsSeekable(message);
            AS4Message as4Message = await DeserializeToAS4Message(rm);

            //Debug.Assert(m.UnderlyingStream.Position == 0, "The Deserializer failed to reposition the stream to its start-position");

            if (as4Message.IsSignalMessage && ReceivingPMode != null)
            {
                Logger.Error(
                    "Static Receive configuration doesn't allow receiving signal messages. " +
                    $"Please remove the static configured Receiving PMode: {Config.Encode(ReceivingPMode)} to also receive signal messages");

                throw new InvalidMessageException(
                    "Static Receive configuration doesn't allow receiving signal messages. ");
            }

            if (as4Message.PrimaryMessageUnit != null)
            {
                Logger.Info($"(Receive) Receiving AS4Message -> {Config.Encode(as4Message.PrimaryMessageUnit.GetType().Name)} {Config.Encode(as4Message.PrimaryMessageUnit.MessageId)}"); 
            }

            var context = new MessagingContext(as4Message, rm, MessagingContextMode.Receive);

            if (ReceivingPMode != null)
            {
                ReceivingProcessingMode pmode =
                    _config.GetReceivingPModes()
                           ?.FirstOrDefault(p => p.Id == ReceivingPMode);

                if (pmode != null)
                {
                    context.ReceivingPMode = pmode;
                }
                else
                {
                    Logger.Error(
                        $"ReceivingPMode with Id: {Config.Encode(ReceivingPMode)} was configured as default PMode, but this PMode cannot be found in the configured receiving PModes."
                        + $"{Config.Encode(Environment.NewLine)} Configured Receiving PModes are placed on the folder: '.\\config\\receive-pmodes\\'.");

                    var errorResult = new ErrorResult(
                        "Static configured ReceivingPMode cannot be found", 
                        ErrorAlias.ProcessingModeMismatch);

                    var as4Error = new Error(
                        IdentifierFactory.Instance.Create(),
                        as4Message.GetPrimaryMessageId() ?? IdentifierFactory.Instance.Create(),
                        ErrorLine.FromErrorResult(errorResult));

                    return new MessagingContext(
                        AS4Message.Create(as4Error), 
                        MessagingContextMode.Receive)
                    {
                        ErrorResult = errorResult
                    };
                }
            }

            return context;
        }

        private static async Task<ReceivedMessage> EnsureIncomingStreamIsSeekable(ReceivedMessage m)
        {
            if (m.UnderlyingStream.CanSeek)
            {
                return m;
            }

            VirtualStream str =
                VirtualStream.Create(
                    expectedSize: m.UnderlyingStream.CanSeek
                        ? m.UnderlyingStream.Length
                        : VirtualStream.ThresholdMax,
                    forAsync: true);

            await m.UnderlyingStream.CopyToFastAsync(str);
            str.Position = 0;

            return new ReceivedMessage(
                str, 
                m.ContentType,
                m.Origin,
                m.Length);
        }

        private static async Task<AS4Message> DeserializeToAS4Message(ReceivedMessage message)
        {
            try
            {
                return await SerializerProvider
                    .Default
                    .Get(message.ContentType)
                    .DeserializeAsync(message.UnderlyingStream, message.ContentType);

            }
            catch (Exception ex)
            {
                Logger.Error(Config.Encode(ex));

                throw new InvalidMessageException(
                    "The incoming stream is not an ebMS Message, " +
                    $"although the Content-Type is: {message.ContentType}. " +
                    "Only ebMS messages conform with the AS4 Profile are supported.");
            }
        }
    }
}
