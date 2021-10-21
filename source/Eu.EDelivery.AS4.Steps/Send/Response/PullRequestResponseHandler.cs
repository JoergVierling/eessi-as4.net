﻿using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Services;
using Eu.EDelivery.AS4.Strategies.Sender;
using log4net;

namespace Eu.EDelivery.AS4.Steps.Send.Response
{
    /// <summary>
    /// <see cref="IAS4ResponseHandler"/> implementation to handle the response for a Pull Request.
    /// </summary>
    internal sealed class PullRequestResponseHandler : IAS4ResponseHandler
    {
        private readonly Func<DatastoreContext> _createContext;
        private readonly IAS4ResponseHandler _nextHandler;

        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        internal PullRequestResponseHandler(IAS4ResponseHandler nextHandler) 
            : this(Registry.Instance.CreateDatastoreContext, nextHandler) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="PullRequestResponseHandler"/> class.
        /// </summary>
        public PullRequestResponseHandler(
            Func<DatastoreContext> createContext,
            IAS4ResponseHandler nextHandler)
        {
            if (createContext == null)
            {
                throw new ArgumentNullException(nameof(createContext));
            }

            if (nextHandler == null)
            {
                throw new ArgumentNullException(nameof(nextHandler));
            }

            _createContext = createContext;
            _nextHandler = nextHandler;
        }

        /// <summary>
        /// Handle the given <paramref name="response" />, but delegate to the next handler if you can't.
        /// </summary>
        /// <param name="response"></param>
        /// <returns></returns>
        public async Task<StepResult> HandleResponse(IAS4Response response)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            MessagingContext request = response.OriginalRequest;
            if (request?.AS4Message?.IsPullRequest == true)
            {
                bool pullRequestWasPiggyBacked = 
                    request.AS4Message.SignalMessages.Any(s => !(s is PullRequest));

                if (pullRequestWasPiggyBacked)
                {
                    using (DatastoreContext ctx = _createContext())
                    {
                        SendResult result =
                            response.StatusCode == HttpStatusCode.Accepted
                            || response.StatusCode == HttpStatusCode.OK
                                ? SendResult.Success
                                : SendResult.RetryableFail;

                        var service = new PiggyBackingService(ctx);
                        service.ResetSignalMessagesToBePiggyBacked(request.AS4Message.SignalMessages, result);

                        await ctx.SaveChangesAsync().ConfigureAwait(false);
                    }
                }

                bool isEmptyChannelWarning = 
                    (response.ReceivedAS4Message?.FirstSignalMessage as Error)?.IsPullRequestWarning == true;

                if (isEmptyChannelWarning)
                {
                    request.ModifyContext(response.ReceivedAS4Message, MessagingContextMode.Send);
                    return StepResult.Success(response.OriginalRequest).AndStopExecution();
                }
            }

            return await _nextHandler.HandleResponse(response);
        }
    }
}