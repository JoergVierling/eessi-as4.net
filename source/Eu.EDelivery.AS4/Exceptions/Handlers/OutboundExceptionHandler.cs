﻿using System;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Extensions;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Entities;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Serialization;
using Eu.EDelivery.AS4.Services;
using log4net;

namespace Eu.EDelivery.AS4.Exceptions.Handlers
{
    internal class OutboundExceptionHandler : IAgentExceptionHandler
    {
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );
        private readonly Func<DatastoreContext> _createContext;
        private readonly IConfig _configuration;
        private readonly IAS4MessageBodyStore _bodyStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="OutboundExceptionHandler"/> class.
        /// </summary>
        public OutboundExceptionHandler() 
            : this(
                Registry.Instance.CreateDatastoreContext,
                Config.Instance,
                Registry.Instance.MessageBodyStore) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="OutboundExceptionHandler" /> class.
        /// </summary>
        /// <param name="createContext">The create context.</param>
        /// <param name="configuration"></param>
        /// <param name="bodyStore"></param>
        public OutboundExceptionHandler(
            Func<DatastoreContext> createContext,
            IConfig configuration,
            IAS4MessageBodyStore bodyStore)
        {
            if (createContext == null)
            {
                throw new ArgumentNullException(nameof(createContext));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (bodyStore == null)
            {
                throw new ArgumentNullException(nameof(bodyStore));
            }

            _createContext = createContext;
            _configuration = configuration;
            _bodyStore = bodyStore;
        }

        /// <summary>
        /// Handles the transformation exception.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="messageToTransform">The <see cref="ReceivedMessage"/> that must be transformed by the transformer.</param>
        /// <returns></returns>
        public async Task<MessagingContext> HandleTransformationException(Exception exception, ReceivedMessage messageToTransform)
        {
            Logger.Error($"Exception occured during transformation: {exception.Message}");

            using (DatastoreContext db = _createContext())
            {
                var repository = new DatastoreRepository(db);
                var service = new ExceptionService(_configuration, repository, _bodyStore);

                await service.InsertOutgoingExceptionAsync(exception, messageToTransform.UnderlyingStream);
                await db.SaveChangesAsync();
            }

            return new MessagingContext(exception);
        }

        /// <summary>
        /// Handles the error exception.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public async Task<MessagingContext> HandleErrorException(Exception exception, MessagingContext context)
        {
            Logger.Error($"Exception occured while executing Error Pipeline: {exception.Message}");
            return await HandleExecutionException(exception, context);
        }

        /// <summary>
        /// Handles the execution exception.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="context">The message context.</param>
        /// <returns></returns>
        public async Task<MessagingContext> HandleExecutionException(Exception exception, MessagingContext context)
        {
            Logger.Error($"Exception occured while executing Steps: {exception.Message}");
            Logger.Trace(exception.StackTrace);

            if (exception.InnerException != null)
            {
                Logger.Error(exception.InnerException.Message);
                Logger.Trace(exception.InnerException.StackTrace);
            }

            string ebmsMessageId = await GetEbmsMessageId(context);
            using (DatastoreContext db = _createContext())
            {
                await db.TransactionalAsync(async ctx =>
                {
                    var repository = new DatastoreRepository(ctx);
                    var service = new ExceptionService(_configuration, repository, _bodyStore);

                    OutException entity =
                        context.SubmitMessage != null
                            ? await service.InsertOutgoingSubmitExceptionAsync(exception, context.SubmitMessage, context.SendingPMode)
                            : await service.InsertOutgoingAS4MessageExceptionAsync(exception, ebmsMessageId, context.MessageEntityId, context.SendingPMode);

                    await ctx.SaveChangesAsync();

                    service.InsertRelatedRetryReliability(entity, context.SendingPMode?.ExceptionHandling?.Reliability);
                    await ctx.SaveChangesAsync();
                });
            }

            return new MessagingContext(exception);
        }

        private static async Task<string> GetEbmsMessageId(MessagingContext context)
        {
            string ebmsMessageId = context.EbmsMessageId;

            if (String.IsNullOrWhiteSpace(ebmsMessageId) && context.ReceivedMessage != null)
            {
                AS4Message as4Message = await TryDeserialize(context.ReceivedMessage);
                ebmsMessageId = as4Message?.GetPrimaryMessageId();
            }

            return ebmsMessageId;
        }

        private static async Task<AS4Message> TryDeserialize(ReceivedMessage message)
        {
            ISerializer serializer = SerializerProvider.Default.Get(message.ContentType);
            try
            {
                message.UnderlyingStream.Position = 0;

                return await serializer.DeserializeAsync(
                    message.UnderlyingStream, 
                    message.ContentType);
            }
            catch
            {
                return null;
            }
        }
    }
}

