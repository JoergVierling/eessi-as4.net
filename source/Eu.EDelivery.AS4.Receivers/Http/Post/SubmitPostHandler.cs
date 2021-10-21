﻿using System;
using System.Net;
using Eu.EDelivery.AS4.Model.Internal;
using log4net;

namespace Eu.EDelivery.AS4.Receivers.Http.Post
{
    /// <summary>
    /// HTTP POST handler to respond to a <see cref="Model.Submit.SubmitMessage"/>.
    /// </summary>
    internal class SubmitPostHandler : IHttpPostHandler
    {
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        /// <summary>
        /// Determines if the resulted context can be handled by this instance.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public bool CanHandle(MessagingContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // Ugly hack until the Transformer is refactored.
            // When we're in SubmitMode and have an Empty AS4Message, then we should return an Accepted.
            return context.Mode == MessagingContextMode.Submit && context.AS4Message?.IsEmpty == false;
        }

        /// <summary>
        /// Handles the resulted context.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public HttpResult Handle(MessagingContext context)
        {
            Logger.Debug("Respond with 202 Accepted");
            return HttpResult.Empty(HttpStatusCode.Accepted);
        }
    }
}