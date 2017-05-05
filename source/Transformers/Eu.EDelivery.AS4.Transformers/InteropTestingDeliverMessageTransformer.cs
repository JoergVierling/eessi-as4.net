using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using Eu.EDelivery.AS4.Builders.Core;
using Eu.EDelivery.AS4.Model.Common;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Deliver;
using MessageProperty = Eu.EDelivery.AS4.Model.Core.MessageProperty;
using Party = Eu.EDelivery.AS4.Model.Core.Party;
using PartyId = Eu.EDelivery.AS4.Model.Core.PartyId;

namespace Eu.EDelivery.AS4.Transformers
{
    [ExcludeFromCodeCoverage]
    public class InteropTestingDeliverMessageTransformer : DeliverMessageTransformer
    {
        private const string InteropUriPrefix = "http://www.esens.eu/as4/interoptest";

        protected override DeliverMessageEnvelope CreateDeliverMessageEnvelope(AS4Message as4Message)
        {
            var deliverMessage = CreateMinderDeliverMessage(as4Message);

            // The Minder Deliver Message should be an AS4-Message.
            var builder = new AS4MessageBuilder();

            builder.WithUserMessage(deliverMessage);

            foreach (var att in as4Message.Attachments)
            {
                builder.WithAttachment(att);
            }

            var msg = builder.Build();

            var serializer = Common.Registry.Instance.SerializerProvider.Get(msg.ContentType);

            byte[] content;

            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(msg, memoryStream, CancellationToken.None);
                content = memoryStream.ToArray();
            }

            return new DeliverMessageEnvelope(
                new MessageInfo()
                {
                    MessageId = deliverMessage.MessageId,
                    RefToMessageId = deliverMessage.RefToMessageId
                },
                content,
                msg.ContentType);
        }

        private static UserMessage CreateMinderDeliverMessage(AS4Message as4Message)
        {
            UserMessage userMessage = as4Message.PrimaryUserMessage;

            UserMessage deliverMessage = new UserMessage(userMessage.MessageId)
            {
                RefToMessageId = userMessage.RefToMessageId,
                Timestamp = userMessage.Timestamp,
                CollaborationInfo =
                {
                    Action = "Deliver",
                    Service = {Value = InteropUriPrefix},
                    ConversationId = userMessage.CollaborationInfo.ConversationId
                },
                Sender = new Party($"{InteropUriPrefix}/sut", userMessage.Receiver.PartyIds.FirstOrDefault()),
                Receiver = new Party($"{InteropUriPrefix}/testdriver", new PartyId("minder"))
            };


            // Party Information: sender is the receiver of the AS4Message that has been received.
            //                    receiver is minder.

            // Set the PayloadInfo.
            foreach (var pi in userMessage.PayloadInfo)
            {
                deliverMessage.PayloadInfo.Add(pi);
            }

            deliverMessage.MessageProperties.Add(new MessageProperty("MessageId", userMessage.MessageId));
            AddMessageProperty(deliverMessage, "RefToMessageId", userMessage.RefToMessageId);
            AddMessageProperty(deliverMessage, "ConversationId", userMessage.CollaborationInfo.ConversationId);

            AddMessageProperty(deliverMessage, "Service", userMessage.CollaborationInfo.Service.Value);
            AddMessageProperty(deliverMessage, "Action", userMessage.CollaborationInfo.Action);
            AddMessageProperty(deliverMessage, "ConversationId", userMessage.CollaborationInfo.ConversationId);

            AddMessageProperty(deliverMessage, "FromPartyId", userMessage.Sender.PartyIds.First().Id);
            AddMessageProperty(deliverMessage, "FromPartyRole", userMessage.Sender.Role);

            AddMessageProperty(deliverMessage, "ToPartyId", userMessage.Receiver.PartyIds.First().Id);
            AddMessageProperty(deliverMessage, "ToPartyRole", userMessage.Receiver.Role);

            AddMessageProperty(deliverMessage, userMessage.MessageProperties.FirstOrDefault(p => p.Name.Equals("originalSender")));
            AddMessageProperty(deliverMessage, userMessage.MessageProperties.FirstOrDefault(p => p.Name.Equals("finalRecipient")));

            return deliverMessage;
        }

        private static void AddMessageProperty(UserMessage message, string propertyName, string propertyValue)
        {
            if (propertyValue == null)
            {
                return;
            }

            AddMessageProperty(message, new MessageProperty(propertyName, propertyValue));
        }

        private static void AddMessageProperty(UserMessage message, MessageProperty property)
        {
            if (property == null)
            {
                return;
            }
            message.MessageProperties.Add(property);
        }

    }
}