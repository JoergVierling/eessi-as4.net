﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Mappings.Core;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.PMode;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Serialization;
using log4net;

namespace Eu.EDelivery.AS4.Entities
{
    /// <summary>
    /// AS4 Message Entity
    /// </summary>
    public abstract class MessageEntity : Entity
    {
        private static readonly ILog Logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        [MaxLength(256)]
        public string EbmsMessageId { get; private set; }

        [MaxLength(256)]
        public string EbmsRefToMessageId { get; set; }

        [MaxLength(256)]
        public string ContentType { get; set; }

        /// <summary>
        /// Gets a string representation of the PMode that has been used to process this message.
        /// </summary>
        public string PMode { get; private set; }

        /// <summary>
        /// Gets the ID of the PMode that is used to process this message.
        /// </summary>
        [MaxLength(256)]
        public string PModeId { get; private set; }

        /// <summary>
        /// Set the Id & string representation of the PMode that is used to process the message.
        /// </summary>
        /// <param name="pmodeId"></param>
        /// <param name="pmodeContent"></param>
        public void SetPModeInformation(string pmodeId, string pmodeContent)
        {
            PModeId = pmodeId;
            PMode = pmodeContent;
        }

        /// <summary>
        /// Set the PMode that is used to process the message.
        /// </summary>
        /// <param name="pmode"></param>
        public void SetPModeInformation(IPMode pmode)
        {
            if (pmode != null)
            {
                PModeId = pmode.Id;

                // The Xml Serializer is not able to serialize an interface, therefore
                // the argument must first be cast to a correct implementation.

                if (pmode is SendingProcessingMode sp)
                {
                    PMode = AS4XmlSerializer.ToString(sp);
                }
                else if (pmode is ReceivingProcessingMode rp)
                {
                    PMode = AS4XmlSerializer.ToString(rp);
                }
                else
                {
                    throw new NotImplementedException("Unable to serialize the the specified IPMode");
                }
            }
        }

        /// <summary>
        /// Gets the sending processing mode based on a child representation of a message entity.
        /// </summary>
        public abstract SendingProcessingMode GetSendingPMode();

        /// <summary>
        /// Gets the receiving processing mode based on a child representation of a message entity.
        /// </summary>
        public abstract ReceivingProcessingMode GetReceivingPMode();

        [MaxLength(255)]
        public string FromParty { get; set; }

        [MaxLength(255)]
        public string ToParty { get; set; }

        [Column("MPC")]
        [MaxLength(255)]
        public string Mpc { get; set; }

        [MaxLength(50)]
        public string ConversationId { get; set; }

        [MaxLength(255)]
        public string Service { get; set; }

        [MaxLength(255)]
        public string Action { get; set; }

        public bool IsDuplicate { get; set; }

        public bool IsTest { get; set; }

        /// <summary>
        /// Flag that indicates whether or not we have treated this message 
        /// as an Intermediary MSH
        /// </summary>        
        public bool Intermediary { get; set; }

        /// <summary>
        /// Gets to the location where the AS4Message body can be found.
        /// </summary>
        [MaxLength(512)]
        public string MessageLocation { get; set; }

        [Column("Operation")]
        [MaxLength(50)]
        public Operation Operation { get; set; }

        [Column("MEP")]
        [MaxLength(25)]
        public MessageExchangePattern MEP { get; set; }

        [MaxLength(50)]
        public MessageType EbmsMessageType { get; set; }

        [Column("Status")]
        [MaxLength(50)]
        public string Status { get; protected set; }

        public string SoapEnvelope { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageEntity"/> class.
        /// </summary>
        protected MessageEntity()
        {
            Operation = default(Operation);
            EbmsMessageType = default(MessageType);
            MEP = default(MessageExchangePattern);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageEntity"/> class.
        /// </summary>
        protected MessageEntity(string ebmsMessageId) : this()
        {
            EbmsMessageId = ebmsMessageId;
        }

        /// <summary>
        /// Assigns the parent properties.
        /// </summary>
        /// <param name="messageUnit">The MessageUnit from which the properties must be retrieved..</param>
        public void AssignAS4Properties(MessageUnit messageUnit)
        {
            if (messageUnit is UserMessage userMessage)
            {
                FromParty = userMessage.Sender.PartyIds.First().Id;
                ToParty = userMessage.Receiver.PartyIds.First().Id;
                Action = userMessage.CollaborationInfo.Action;
                Service = userMessage.CollaborationInfo.Service.Value;
                ConversationId = userMessage.CollaborationInfo.ConversationId;
                Mpc = userMessage.Mpc;
                IsTest = userMessage.IsTest;
                IsDuplicate = userMessage.IsDuplicate;
                SoapEnvelope = AS4XmlSerializer.ToString(UserMessageMap.Convert(userMessage));
            }
            else
            {
                if (messageUnit is SignalMessage signalMessage)
                {
                    IsDuplicate = signalMessage.IsDuplicate;
                    Mpc = signalMessage.MultiHopRouting.Select(r => r.mpc).GetOrElse(Constants.Namespaces.EbmsDefaultMpc);
                }
            }
        }

        /// <summary>
        /// Retrieves the Message body as a stream.
        /// </summary>
        /// <param name="store">
        /// The <see cref="MessageBodyStore" /> which is responsible for providing the correct
        /// <see cref="IAS4MessageBodyStore" /> that loads the <see cref="AS4Message" /> body.
        /// </param>
        /// <returns>A Stream which contains the MessageBody</returns>
        public async Task<Stream> RetrieveMessageBody(IAS4MessageBodyStore store)
        {
            if (string.IsNullOrWhiteSpace(MessageLocation))
            {
                Logger.Warn("Unable to retrieve the AS4 Message Body: MessageLocation is not set.");
                return null;
            }

            return await TryLoadMessageBody(store);
        }

        private async Task<Stream> TryLoadMessageBody(IAS4MessageBodyStore store)
        {
            try
            {
                return await store.LoadMessageBodyAsync(MessageLocation);
            }
            catch (Exception exception)
            {
                Logger.Error(exception.Message);

                return null;
            }
        }
    }
}