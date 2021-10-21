﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Services;
using Microsoft.EntityFrameworkCore;
using log4net;
using Eu.EDelivery.AS4.Extensions;

namespace Eu.EDelivery.AS4.Steps.Send
{
    [Info("Confirm that the message can be sent.")]
    [Description("Confirms that the message is ready to be sent.")]
    public class SetMessageToBeSentStep : IStep
    {
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        private readonly Func<DatastoreContext> _createContext;
        private readonly IAS4MessageBodyStore _messageStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="SetMessageToBeSentStep"/> class.
        /// </summary>
        public SetMessageToBeSentStep()
            : this(Registry.Instance.CreateDatastoreContext, Registry.Instance.MessageBodyStore) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="SetMessageToBeSentStep"/> class.
        /// </summary>
        /// <param name="createContext">The get data store context.</param>
        /// <param name="messageStore">The message store.</param>
        public SetMessageToBeSentStep(Func<DatastoreContext> createContext, IAS4MessageBodyStore messageStore)
        {
            if (createContext == null)
            {
                throw new ArgumentNullException(nameof(createContext));
            }

            if (messageStore == null)
            {
                throw new ArgumentNullException(nameof(messageStore));
            }

            _createContext = createContext;
            _messageStore = messageStore;
        }

        /// <summary>
        /// Execute the step for a given <paramref name="messagingContext"/>.
        /// </summary>
        /// <param name="messagingContext">Message used during the step execution.</param>
        /// <returns></returns>
        public async Task<StepResult> ExecuteAsync(MessagingContext messagingContext)
        {
            if (messagingContext?.AS4Message == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(SetMessageToBeSentStep)} requires an AS4Message to mark for sending but no AS4Message is present in the MessagingContext");
            }

            Logger.Trace($"{Config.Encode(messagingContext.LogTag)} Set the message's Operation=ToBeSent");
            if (messagingContext.MessageEntityId == null)
            {
                throw new InvalidOperationException(
                    $"{messagingContext.LogTag} MessagingContext does not contain the ID of the OutMessage that must be set to ToBeSent");
            }

            using (DatastoreContext context = _createContext())
            {
                var repository = new DatastoreRepository(context);
                var service = new OutMessageService(repository, _messageStore);

                service.UpdateAS4MessageToBeSent(
                    messagingContext.MessageEntityId.Value, 
                    messagingContext.AS4Message,
                    messagingContext.SendingPMode?.Reliability?.ReceptionAwareness);

                await context.SaveChangesAsync().ConfigureAwait(false);
                messagingContext.OutMessage = context.OutMessages.AsNoTracking().FirstOrDefault(p => p.Id == messagingContext.MessageEntityId.Value);
            }

            return StepResult.Success(messagingContext);
        }
    }
}

