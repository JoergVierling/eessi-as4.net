﻿using System;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Extensions;
using Eu.EDelivery.AS4.Model.Internal;
using log4net;

namespace Eu.EDelivery.AS4.Exceptions.Handlers
{
    /// <summary>
    /// Wrapper for the <see cref="IAgentExceptionHandler"/> implementation to safeguard the exception handling.
    /// </summary>
    /// <seealso cref="IAgentExceptionHandler" />
    internal class SafeExceptionHandler : IAgentExceptionHandler
    {
        private readonly IAgentExceptionHandler _innerHandler;

        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        /// <summary>
        /// Initializes a new instance of the <see cref="SafeExceptionHandler" /> class.
        /// </summary>
        /// <param name="handler">The handler.</param>
        public SafeExceptionHandler(IAgentExceptionHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _innerHandler = handler;
        }

        /// <summary>
        /// Handles the transformation exception.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="messageToTransform">The <see cref="ReceivedMessage"/> that must be transformed by the transformer.</param>
        /// <returns></returns>
        public async Task<MessagingContext> HandleTransformationException(Exception exception, ReceivedMessage messageToTransform)
        {
            return await TryHandling(() => _innerHandler.HandleTransformationException(exception, messageToTransform));
        }

        /// <summary>
        /// Handles the execution exception.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public async Task<MessagingContext> HandleExecutionException(Exception exception, MessagingContext context)
        {
            return await TryHandling(() => _innerHandler.HandleExecutionException(exception, context), faultingContext: context);
        }

        /// <summary>
        /// Handles the error exception.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public async Task<MessagingContext> HandleErrorException(Exception exception, MessagingContext context)
        {
            return await TryHandling(() => _innerHandler.HandleErrorException(exception, context), faultingContext: context);
        }

        private static async Task<MessagingContext> TryHandling(Func<Task<MessagingContext>> actionToTry, MessagingContext faultingContext)
        {
            try
            {
                return await actionToTry();
            }
            catch (Exception ex)
            {
                Logger.Error("An error occured while trying to log an error:");
                Logger.Error(ex);
                Logger.Trace(ex.StackTrace);

                if (faultingContext != null && faultingContext.Exception == null)
                {
                    faultingContext.Exception = ex;
                }

                return faultingContext ?? new MessagingContext(ex);
            }
        }

        private static async Task<MessagingContext> TryHandling(Func<Task<MessagingContext>> actionToTry)
        {
            try
            {
                return await actionToTry();
            }
            catch (Exception exception)
            {
                Logger.Error(exception);
                Logger.Trace(exception.StackTrace);
                return new MessagingContext(exception);
            }
        }
    }
}
