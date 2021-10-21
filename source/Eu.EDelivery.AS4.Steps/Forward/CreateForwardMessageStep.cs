﻿using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Builders.Entities;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Entities;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Serialization;
using log4net;

namespace Eu.EDelivery.AS4.Steps.Forward
{
    [Info("Creates a copy of the received message so that it can be forwarded.")]
    [Description("Creates a copy of the received message so that it can be forwarded.")]
    public class CreateForwardMessageStep : IStep
    {
        private readonly IConfig _configuration;
        private readonly IAS4MessageBodyStore _messageStore;
        private readonly Func<DatastoreContext> _createDataStoreContext;

        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        /// <summary>
        /// Initializes a new instance of the <see cref="CreateForwardMessageStep"/> class.
        /// </summary>
        public CreateForwardMessageStep()
            : this(Config.Instance, Registry.Instance.MessageBodyStore, Registry.Instance.CreateDatastoreContext) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="CreateForwardMessageStep" /> class.
        /// </summary>
        /// <param name="configuration">The local configuration.</param>
        /// <param name="messageStore">The store where the datastore persist its messages.</param>
        /// <param name="createDatastoreContext">Create a new datastore context.</param>
        public CreateForwardMessageStep(
            IConfig configuration,
            IAS4MessageBodyStore messageStore,
            Func<DatastoreContext> createDatastoreContext)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (messageStore == null)
            {
                throw new ArgumentNullException(nameof(messageStore));
            }

            if (createDatastoreContext == null)
            {
                throw new ArgumentNullException(nameof(createDatastoreContext));
            }

            _configuration = configuration;
            _messageStore = messageStore;
            _createDataStoreContext = createDatastoreContext;
        }

        /// <summary>
        /// Execute the step for a given <paramref name="messagingContext"/>.
        /// </summary>
        /// <param name="messagingContext">Message used during the step execution.</param>
        /// <returns></returns>
        public async Task<StepResult> ExecuteAsync(MessagingContext messagingContext)
        {
            var entityMessage = messagingContext?.ReceivedMessage as ReceivedEntityMessage;
            if (!(entityMessage?.Entity is InMessage receivedInMessage))
            {
                throw new InvalidOperationException(
                    "The MessagingContext must contain a ReceivedMessage that represents an InMessage." + Environment.NewLine +
                    "Other types of ReceivedMessage models are not supported in this Step.");
            }

            // Forward message by creating an OutMessage and set operation to 'ToBeProcessed'.
            Logger.Info($"{Config.Encode(messagingContext.LogTag)} Create a message that will be forwarded to the next MSH");
            using (Stream originalInMessage =
                await _messageStore.LoadMessageBodyAsync(receivedInMessage.MessageLocation))
            {
                string outLocation = await _messageStore.SaveAS4MessageStreamAsync(
                    _configuration.OutMessageStoreLocation,
                    originalInMessage);

                originalInMessage.Position = 0;

                AS4Message msg =
                    await SerializerProvider.Default
                        .Get(receivedInMessage.ContentType)
                        .DeserializeAsync(originalInMessage, receivedInMessage.ContentType);

                using (DatastoreContext dbContext = _createDataStoreContext())
                {
                    var repository = new DatastoreRepository(dbContext);

                    // Only create an OutMessage for the primary message-unit.
                    OutMessage outMessage = OutMessageBuilder
                        .ForMessageUnit(
                            msg.PrimaryMessageUnit,
                            receivedInMessage.ContentType,
                            messagingContext.SendingPMode)
                        .BuildForForwarding(outLocation, receivedInMessage);

                    Logger.Debug("Insert OutMessage {{Intermediary=true, Operation=ToBeProcesed}}");
                    repository.InsertOutMessage(outMessage);

                    // Set the InMessage to Forwarded.
                    // We do this for all InMessages that are present in this AS4 Message
                    repository.UpdateInMessages(
                        m => msg.MessageIds.Contains(m.EbmsMessageId),
                        r => r.Operation = Operation.Forwarded);

                    await dbContext.SaveChangesAsync();
                }
            }

            return StepResult.Success(messagingContext);
        }
    }
}
