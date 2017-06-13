﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Builders.Core;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Entities;
using Eu.EDelivery.AS4.Exceptions;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Services;
using Eu.EDelivery.AS4.Singletons;
using Eu.EDelivery.AS4.Xml;
using NLog;
using Error = Eu.EDelivery.AS4.Model.Core.Error;
using UserMessage = Eu.EDelivery.AS4.Model.Core.UserMessage;

namespace Eu.EDelivery.AS4.Steps.Receive
{
    /// <summary>
    /// Create an <see cref="Model.Core.Error"/> 
    /// from a given <see cref="AS4Exception"/>
    /// </summary>
    public class CreateAS4ErrorStep : IStep
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private readonly IAS4MessageBodyStore _messageBodyStore;
        private readonly Func<DatastoreContext> _createDatastore;

        /// <summary>
        /// Initializes a new instance of the <see cref="CreateAS4ErrorStep"/> class.
        /// </summary>
        public CreateAS4ErrorStep() : this(Registry.Instance.MessageBodyStore, Registry.Instance.CreateDatastoreContext) {}

        /// <summary>
        /// Initializes a new instance of the <see cref="CreateAS4ErrorStep"/> class
        /// </summary>
        /// <param name="messageBodyStore">The <see cref="IAS4MessageBodyStore"/> that must be used to persist the MessageBody.</param>
        /// <param name="createDatastoreContext">The context in which teh datastore context is set.</param>
        public CreateAS4ErrorStep(IAS4MessageBodyStore messageBodyStore, Func<DatastoreContext> createDatastoreContext)
        {
            _messageBodyStore = messageBodyStore;
            _createDatastore = createDatastoreContext;
        }


        /// <summary>
        /// Start creating <see cref="Model.Core.Error"/>
        /// </summary>
        /// <param name="messagingContext"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="System.Exception">A delegate callback throws an exception.</exception>
        public async Task<StepResult> ExecuteAsync(MessagingContext messagingContext, CancellationToken cancellationToken)
        {
            if (ShouldCreateError(messagingContext) == false)
            {
                return await StepResult.SuccessAsync(messagingContext);
            }

            AS4Message errorMessage = CreateAS4ErrorMessage(messagingContext);
            MessagingContext message = messagingContext.CloneWith(errorMessage);

            // Save the Error Message as well .... 
            using (DatastoreContext db = _createDatastore())
            {
                var service = new OutMessageService(new DatastoreRepository(db), _messageBodyStore);

                // The service will determine the correct operation for each message-part.
                await service.InsertAS4Message(message, Operation.NotApplicable, cancellationToken);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            
            return await StepResult.SuccessAsync(message);
        }

        private static AS4Message CreateAS4ErrorMessage(MessagingContext messagingContext)
        {
            Logger.Info($"{messagingContext.Prefix} Create AS4 Error Message from AS4 Exception");

            AS4Message errorMessage = AS4Message.Create(messagingContext.SendingPMode);

            CreateErrorForEveryUserMessageIn(messagingContext, error => errorMessage.SignalMessages.Add(error));

            errorMessage.SigningId = messagingContext.AS4Message.SigningId;

            return errorMessage;
        }

        private static void CreateErrorForEveryUserMessageIn(MessagingContext messagingContext, Action<Error> callback)
        {
            foreach (UserMessage userMessage in messagingContext.AS4Message.UserMessages)
            {
                Error error = CreateError(messagingContext.Exception, userMessage.MessageId, messagingContext);

                callback(error);
            }
        }

        private static bool ShouldCreateError(MessagingContext messagingContext)
        {
            return messagingContext.Exception != null && (messagingContext.AS4Message?.UserMessages?.Any() ?? false);
        }

        private static Error CreateError(AS4Exception exception, string userMessageId, MessagingContext originalAS4Message)
        {
            Error error = new ErrorBuilder()
                .WithRefToEbmsMessageId(userMessageId)
                .WithAS4Exception(exception)
                .Build();

            if (originalAS4Message.SendingPMode?.MessagePackaging.IsMultiHop == true)
            {
                error.MultiHopRouting = AS4Mapper.Map<RoutingInputUserMessage>(originalAS4Message.AS4Message?.PrimaryUserMessage);
            }

            return error;
        }
    }
}