﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Builders.Entities;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Entities;
using Eu.EDelivery.AS4.Exceptions;
using Eu.EDelivery.AS4.Extensions;
using Eu.EDelivery.AS4.Factories;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Model.PMode;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Serialization;
using Eu.EDelivery.AS4.Streaming;
using log4net;
using MessageExchangePattern = Eu.EDelivery.AS4.Entities.MessageExchangePattern;
using RetryReliability = Eu.EDelivery.AS4.Model.PMode.RetryReliability;

namespace Eu.EDelivery.AS4.Services
{
    /// <summary>
    /// Repository to expose Data store related operations
    /// for the Update Data store Steps
    /// </summary>
    internal class InMessageService
    {
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        private readonly IDatastoreRepository _repository;
        private readonly IConfig _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="InMessageService"/> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        /// <param name="repository">The repository.</param>
        public InMessageService(IConfig config, IDatastoreRepository repository)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            _configuration = config;
            _repository = repository;
        }

        /// <summary>
        /// Insert a DeadLettered AS4 Error refering a specified <paramref name="ebmsMessageId"/> 
        /// for a specified <paramref name="mep"/> notifying only if the specified <paramref name="sendingPMode"/> is configured this way.
        /// </summary>
        /// <param name="ebmsMessageId"></param>
        /// <param name="mep"></param>
        /// <param name="sendingPMode"></param>
        /// <exception cref="ArgumentNullException"></exception>
        internal void InsertDeadLetteredErrorForAsync(
            string ebmsMessageId,
            MessageExchangePattern mep,
            SendingProcessingMode sendingPMode)
        {
            if (ebmsMessageId == null)
            {
                throw new ArgumentNullException(nameof(ebmsMessageId));
            }

            Error errorMessage =
                Error.FromErrorResult(
                    IdentifierFactory.Instance.Create(),
                    ebmsMessageId,
                    new ErrorResult("Missing Receipt", ErrorAlias.MissingReceipt));

            AS4Message as4Message = AS4Message.Create(errorMessage, sendingPMode);

            // We do not use the InMessageService to persist the incoming message here, since this is not really
            // an incoming message.  We create this InMessage in order to be able to notify the Message Producer
            // if he should be notified when a message cannot be sent.
            // (Maybe we should only create the InMessage when notification is enabled ?)
            string location =
                Registry.Instance
                        .MessageBodyStore
                        .SaveAS4Message(
                            location: Config.Instance.InMessageStoreLocation,
                            message: as4Message);

            InMessage inMessage = InMessageBuilder
                .ForSignalMessage(errorMessage, as4Message, mep)
                .WithPMode(sendingPMode)
                .OnLocation(location)
                .BuildAsDeadLetteredError();

            Logger.Debug($"Create Error for missed Receipt with {{Operation={Config.Encode(inMessage.Operation)}}}");
            _repository.InsertInMessage(inMessage);
        }

        /// <summary>
        /// Inserts a received Message in the DataStore.
        /// For each message-unit that exists in the AS4Message,an InMessage record is created.
        /// The AS4 Message Body is persisted as it has been received.
        /// </summary>
        /// <remarks>The received Message is parsed to an AS4 Message instance.</remarks>
        /// <param name="sendingPMode"></param>
        /// <param name="mep"></param>
        /// <param name="messageBodyStore"></param>
        /// <param name="as4Message"></param>
        /// <param name="originalMessage"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <returns>A MessagingContext instance that contains the parsed AS4 Message.</returns>
        public async Task<AS4Message> InsertAS4MessageAsync(
            AS4Message as4Message,
            ReceivedMessage originalMessage,
            SendingProcessingMode sendingPMode,
            MessageExchangePattern mep,
            IAS4MessageBodyStore messageBodyStore)
        {
            if (as4Message == null)
            {
                throw new ArgumentNullException(nameof(as4Message));
            }

            if (originalMessage == null)
            {
                throw new InvalidOperationException("The MessagingContext must contain a ReceivedMessage");
            }

            if (messageBodyStore == null)
            {
                throw new ArgumentNullException(nameof(messageBodyStore));
            }

            // TODO: should we start the transaction here.
            string location =
                await messageBodyStore.SaveAS4MessageStreamAsync(
                    location: _configuration.InMessageStoreLocation,
                    as4MessageStream: originalMessage.UnderlyingStream).ConfigureAwait(false);

            StreamUtilities.MovePositionToStreamStart(originalMessage.UnderlyingStream);

            try
            {
                InsertUserMessages(as4Message, mep, location, sendingPMode);
                InsertSignalMessages(as4Message, mep, location, sendingPMode);

                return as4Message;
            }
            catch (Exception ex)
            {
                Logger.Error(Config.Encode(ex.Message));

                var service = new ExceptionService(_configuration, _repository, messageBodyStore);
                await service.InsertIncomingExceptionAsync(ex, new MemoryStream(Encoding.UTF8.GetBytes(location)));

                throw;
            }
        }

        private void InsertUserMessages(
            AS4Message as4Message,
            MessageExchangePattern mep,
            string location,
            SendingProcessingMode pmode)
        {
            if (!as4Message.HasUserMessage)
            {
                Logger.Trace("No UserMessages present to be inserted");
                return;
            }

            IDictionary<string, bool> duplicateUserMessages =
                DetermineDuplicateUserMessageIds(as4Message.UserMessages.Select(m => m.MessageId));

            foreach (UserMessage userMessage in as4Message.UserMessages)
            {
                if (userMessage.IsTest)
                {
                    Logger.Trace($"Incoming UserMessage {Config.Encode(userMessage.MessageId)} is a 'Test Message'");
                }

                userMessage.IsDuplicate = IsUserMessageDuplicate(userMessage, duplicateUserMessages);

                try
                {
                    InMessage inMessage = InMessageBuilder
                        .ForUserMessage(userMessage, as4Message, mep)
                        .WithPMode(pmode)
                        .OnLocation(location)
                        .BuildAsToBeProcessed();

                    Logger.Debug(
                        $"Insert InMessage UserMessage {Config.Encode(userMessage.MessageId)} with {{"
                        + $"Operation={Config.Encode(inMessage.Operation)}, "
                        + $"Status={Config.Encode(inMessage.Status)}, "
                        + $"PModeId={pmode?.Id ?? "null"}, "
                        + $"IsTest={Config.Encode(userMessage.IsTest)}, "
                        + $"IsDuplicate={Config.Encode(userMessage.IsDuplicate)}}}");

                    _repository.InsertInMessage(inMessage);
                }
                catch (Exception ex)
                {
                    string description = $"Unable to insert UserMessage {Config.Encode(userMessage.MessageId)}";
                    Logger.Error(Config.Encode(description));

                    throw new DataException(description, ex);
                }
            }
        }

        private IDictionary<string, bool> DetermineDuplicateUserMessageIds(IEnumerable<string> searchedMessageIds)
        {
            IEnumerable<string> duplicateMessageIds = _repository.SelectExistingInMessageIds(searchedMessageIds);

            return MergeTwoListsIntoADuplicateMessageMapping(searchedMessageIds, duplicateMessageIds);
        }

        private void InsertSignalMessages(
            AS4Message as4Message,
            MessageExchangePattern mep,
            string location,
            SendingProcessingMode pmode)
        {
            if (!as4Message.HasSignalMessage)
            {
                Logger.Trace("No SignalMessages present to be inserted");
                return;
            }

            IEnumerable<string> relatedUserMessageIds = as4Message.SignalMessages
                .Select(m => m.RefToMessageId)
                .Where(refToMessageId => !String.IsNullOrWhiteSpace(refToMessageId));

            IDictionary<string, bool> duplicateSignalMessages =
                DetermineDuplicateSignalMessageIds(relatedUserMessageIds);

            foreach (SignalMessage signalMessage in as4Message.SignalMessages.Where(s => !(s is PullRequest)))
            {
                signalMessage.IsDuplicate = IsSignalMessageDuplicate(signalMessage, duplicateSignalMessages);

                try
                {
                    InMessage inMessage = InMessageBuilder
                        .ForSignalMessage(signalMessage, as4Message, mep)
                        .WithPMode(pmode)
                        .OnLocation(location)
                        .BuildAsToBeProcessed();

                    Logger.Debug(
                        $"Insert InMessage {Config.Encode(signalMessage.GetType().Name)} {Config.Encode(signalMessage.MessageId)} with {{"
                        + $"Operation={Config.Encode(inMessage.Operation)}, "
                        + $"Status={Config.Encode(inMessage.Status)}, "
                        + $"PModeId={Config.Encode(pmode?.Id)}}}");

                    _repository.InsertInMessage(inMessage);
                }
                catch (Exception exception)
                {
                    string description = $"Unable to insert SignalMessage {Config.Encode(signalMessage.MessageId)}";
                    Logger.Error(Config.Encode(description));

                    throw new DataException(description, exception);
                }
            }
        }

        private IDictionary<string, bool> DetermineDuplicateSignalMessageIds(IEnumerable<string> searchedMessageIds)
        {
            IEnumerable<string> duplicateMessageIds = _repository.SelectExistingRefInMessageIds(searchedMessageIds);

            return MergeTwoListsIntoADuplicateMessageMapping(searchedMessageIds, duplicateMessageIds);
        }

        private static IDictionary<string, bool> MergeTwoListsIntoADuplicateMessageMapping(
            IEnumerable<string> searchedMessageIds,
            IEnumerable<string> duplicateMessageIds)
        {
            return searchedMessageIds
                .Select(i => new KeyValuePair<string, bool>(i, duplicateMessageIds.Contains(i)))
                .ToDictionary(k => k.Key, v => v.Value);
        }

        /// <summary>
        /// Updates an <see cref="AS4Message"/> for delivery and notification.
        /// </summary>
        /// <param name="as4Message">The message.</param>
        /// <param name="receivingPMode"></param>
        /// <param name="messageBodyStore">The as4 message body persister.</param>
        /// <param name="sendingPMode"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <returns></returns>
        public void UpdateAS4MessageForMessageHandling(
            AS4Message as4Message,
            SendingProcessingMode sendingPMode,
            ReceivingProcessingMode receivingPMode,
            IAS4MessageBodyStore messageBodyStore)
        {
            if (as4Message == null)
            {
                throw new ArgumentNullException(nameof(as4Message));
            }

            if (messageBodyStore == null)
            {
                throw new ArgumentNullException(nameof(messageBodyStore));
            }

            if (as4Message.HasUserMessage)
            {
                string savedLocation =
                    messageBodyStore.SaveAS4Message(_configuration.InMessageStoreLocation, as4Message);

                IEnumerable<string> userMessageIds = as4Message.UserMessages.Select(u => u.MessageId);

                _repository.UpdateInMessages(
                    m => userMessageIds.Any(id => id == m.EbmsMessageId), 
                    m => m.MessageLocation = savedLocation);
            }

            if (receivingPMode?.MessageHandling?.MessageHandlingType == MessageHandlingChoiceType.Forward)
            {
                Logger.Debug($"Received AS4Message must be forwarded since the ReceivingPMode {Config.Encode(receivingPMode?.Id)} MessageHandling has a <Forward/> element");

                string pmodeString = AS4XmlSerializer.ToString(receivingPMode);
                string pmodeId = receivingPMode.Id;

                // Only set the Operation of the InMessage that represents the 
                // Primary Message-Unit to 'ToBeForwarded' since we want to prevent
                // that the same message is forwarded more than once (x number of messaging units 
                // present in the AS4 Message).

                _repository.UpdateInMessages(
                    m => as4Message.MessageIds.Contains(m.EbmsMessageId),
                    m =>
                    {
                        m.Intermediary = true;
                        m.SetPModeInformation(pmodeId, pmodeString);
                        Logger.Debug($"Update InMessage {Config.Encode(m.EbmsMessageType)} with {{Intermediary={Config.Encode(m.Intermediary)}, PMode={Config.Encode(pmodeId)}}}");
                    });

                _repository.UpdateInMessage(
                    as4Message.GetPrimaryMessageId(),
                    m =>
                    {
                        m.Operation = Operation.ToBeForwarded;
                        Logger.Debug($"Update InMessage {Config.Encode(m.EbmsMessageType)} with Operation={Config.Encode(m.Operation)}");
                    });
            }
            else if (receivingPMode?.MessageHandling?.MessageHandlingType == MessageHandlingChoiceType.Deliver)
            {
                UpdateUserMessagesForDelivery(as4Message.UserMessages, receivingPMode);
                UpdateSignalMessagesForNotification(as4Message.SignalMessages, sendingPMode);
            }
            else
            {
                UpdateSignalMessagesForNotification(as4Message.SignalMessages, sendingPMode);
            }
        }

        private void UpdateUserMessagesForDelivery(IEnumerable<UserMessage> userMessages, ReceivingProcessingMode receivingPMode)
        {
            if (userMessages.Any() == false)
            {
                Logger.Trace("No UserMessages present to be delivered");
                return;
            }

            string receivingPModeId = receivingPMode?.Id;
            string receivingPModeString = AS4XmlSerializer.ToString(receivingPMode);

            var xs = _repository
                .GetInMessagesData(userMessages.Select(um => um.MessageId), im => im.Id)
                .Zip(userMessages, Tuple.Create);

            foreach ((long id, UserMessage userMessage) in xs)
            {
                _repository.UpdateInMessage(
                    userMessage.MessageId,
                    message =>
                    {
                        message.SetPModeInformation(receivingPModeId, receivingPModeString);

                        if (UserMessageNeedsToBeDelivered(receivingPMode, userMessage)
                            && message.Intermediary == false)
                        {
                            message.Operation = Operation.ToBeDelivered;

                            RetryReliability reliability =
                                receivingPMode?.MessageHandling?.DeliverInformation?.Reliability;

                            if (reliability?.IsEnabled ?? false)
                            {
                                var r = Entities.RetryReliability.CreateForInMessage(
                                    refToInMessageId: id,
                                    maxRetryCount: reliability.RetryCount,
                                    retryInterval: reliability.RetryInterval.AsTimeSpan(),
                                    type: RetryType.Delivery);

                                Logger.Debug(
                                    $"Insert RetryReliability for UserMessage InMessage {Config.Encode(r.RefToInMessageId)} with {{"
                                    + $"MaxRetryCount={Config.Encode(r.MaxRetryCount)}, RetryInterval={Config.Encode(r.RetryInterval)}}}");

                                _repository.InsertRetryReliability(r);
                            }
                            else
                            {
                                Logger.Trace(
                                    "Will not insert RetryReliability for UserMessage(s) so it can be retried during delivery "
                                    + $"since the ReceivingPMode {Config.Encode(receivingPMode?.Id)} MessageHandling.Deliver.Reliability.IsEnabled = false");
                            }

                            Logger.Debug($"Update InMessage UserMessage {Config.Encode(userMessage.MessageId)} with Operation={Config.Encode(message.Operation)}");
                        }
                    });
            }
        }

        private void UpdateSignalMessagesForNotification(IEnumerable<SignalMessage> signalMessages, SendingProcessingMode sendingPMode)
        {
            if (!signalMessages.Any())
            {
                Logger.Trace("No SignalMessages present to be notified");
                return;
            }

            // Improvement: I think it will be safer if we retrieve the sending-pmodes of the related usermessages ourselves here
            // instead of relying on the SendingPMode that is available in the AS4Message object (which is set by another Step in the queue).
            IEnumerable<Receipt> receipts = signalMessages.OfType<Receipt>();
            bool notifyReceipts = sendingPMode?.ReceiptHandling?.NotifyMessageProducer ?? false;
            if (!notifyReceipts)
            {
                Logger.Debug($"No Receipts will be notified since the SendingPMode {Config.Encode(sendingPMode?.Id)} ReceiptHandling.NotifyMessageProducer = false");
            }

            RetryReliability retryReceipts = sendingPMode?.ReceiptHandling?.Reliability;
            if (retryReceipts?.IsEnabled == false)
            {
                Logger.Trace(
                    "Will not insert RetryReliability for Receipt(s) so it can be retried during delivery "
                    + $"since the ReceivingPMode {Config.Encode(sendingPMode?.Id)} ReceiptHandling.Reliability.IsEnabled = false");
            }

            if (notifyReceipts)
            {
                UpdateSignalMessages(sendingPMode, receipts, retryReceipts);
            }

            UpdateReferencedUserMessagesStatus(receipts, OutStatus.Ack);

            IEnumerable<Error> errors = signalMessages.OfType<Error>();
            bool notifyErrors = sendingPMode?.ErrorHandling?.NotifyMessageProducer ?? false;
            if (!notifyErrors)
            {
                Logger.Debug($"No Errors will be notified since the SendingPMode {Config.Encode(sendingPMode?.Id)} Errorhandling.NotifyMessageProducer = false");
            }

            RetryReliability retryErrors = sendingPMode?.ErrorHandling?.Reliability;
            if (retryErrors?.IsEnabled == false)
            {
                Logger.Trace(
                    "Will not insert RetryReliability for Error(s) so it can be retried during notification "
                    + $"since the SendingPMode {Config.Encode(sendingPMode?.Id)} ErrorHandling.Reliability.IsEnabled = false");
            }

            if (notifyErrors)
            {
                UpdateSignalMessages(sendingPMode, errors, retryErrors);
            }

            UpdateReferencedUserMessagesStatus(errors, OutStatus.Nack);
        }

        private void UpdateSignalMessages<TSignal>(
            SendingProcessingMode sendingPMode,
            IEnumerable<TSignal> signalMessages,
            RetryReliability reliability) where TSignal : SignalMessage
        {
            string[] signalsToNotify =
                signalMessages.Where(r => r.IsDuplicate == false)
                              .Select(s => s.MessageId)
                              .ToArray();

            if (!signalsToNotify.Any())
            {
                return;
            }

            string ebmsMessageType = typeof(TSignal).Name;

            _repository.UpdateInMessages(
                m => signalsToNotify.Contains(m.EbmsMessageId) && m.Intermediary == false,
                m =>
                {
                    m.Operation = Operation.ToBeNotified;
                    m.SetPModeInformation(sendingPMode);
                    Logger.Debug($"Update InMessage {Config.Encode(ebmsMessageType)} {Config.Encode(m.EbmsMessageId)} with Operation={Config.Encode(m.Operation)} according to SendingPMode {Config.Encode(sendingPMode.Id)}");
                });

            bool isRetryEnabled = reliability?.IsEnabled ?? false;
            if (isRetryEnabled)
            {
                IEnumerable<long> ids = _repository.GetInMessagesData(signalsToNotify, m => m.Id);
                foreach (long id in ids)
                {
                    var r = Entities.RetryReliability.CreateForInMessage(
                        refToInMessageId: id,
                        maxRetryCount: reliability.RetryCount,
                        retryInterval: reliability.RetryInterval.AsTimeSpan(),
                        type: RetryType.Notification);

                    Logger.Debug(
                        $"Insert RetryReliability for SignalMessage InMessage {Config.Encode(id)} with {{"
                        + $"MaxRetryCount={Config.Encode(r.MaxRetryCount)}, "
                        + $"RetryInterval={Config.Encode(r.RetryInterval)}}}");

                    _repository.InsertRetryReliability(r);
                }
            }
        }

        private void UpdateReferencedUserMessagesStatus(IEnumerable<SignalMessage> signalMessages, OutStatus outStatus)
        {
            string[] refToMessageIds = signalMessages.Select(r => r.RefToMessageId).Where(id => !String.IsNullOrEmpty(id)).ToArray();
            if (refToMessageIds.Any())
            {
                _repository.UpdateOutMessages(
                    m => refToMessageIds.Contains(m.EbmsMessageId) && m.Intermediary == false,
                    m =>
                    {
                        m.SetStatus(outStatus);
                        Logger.Debug($"Update OutMessage UserMessage {Config.Encode(m.EbmsMessageId)} with Status={Config.Encode(outStatus)}");
                    });
            }
        }

        #region UserMessage related

        private static bool IsUserMessageDuplicate(
            MessageUnit userMessage,
            IDictionary<string, bool> duplicateUserMessages)
        {
            duplicateUserMessages.TryGetValue(userMessage.MessageId, out bool isDuplicate);

            if (isDuplicate)
            {
                Logger.Debug($"[{Config.Encode(userMessage.MessageId)}] Incoming User Message is a duplicated one");
            }

            return isDuplicate;
        }

        #endregion

        #region SignalMessage related

        private static bool IsSignalMessageDuplicate(
            MessageUnit signalMessage,
            IDictionary<string, bool> duplicateSignalMessages)
        {
            if (String.IsNullOrWhiteSpace(signalMessage.RefToMessageId))
            {
                return false;
            }

            duplicateSignalMessages.TryGetValue(signalMessage.RefToMessageId, out bool isDuplicate);

            if (isDuplicate)
            {
                Logger.Debug($"[{Config.Encode(signalMessage.RefToMessageId)}] Incoming Signal Message is a duplicated one");
            }

            return isDuplicate;
        }

        #endregion SignalMessage related

        private static bool UserMessageNeedsToBeDelivered(ReceivingProcessingMode pmode, UserMessage userMessage)
        {
            if (pmode?.MessageHandling?.DeliverInformation == null)
            {
                Logger.Debug(
                    $"UserMessage will not be delivered since the ReceivingPMode {Config.Encode(pmode?.Id)} has not a MessageHandling.Deliver element");

                return false;
            }

            bool needsToBeDelivered = 
                pmode.MessageHandling.DeliverInformation.IsEnabled
                && !userMessage.IsDuplicate 
                && !userMessage.IsTest;

            Logger.Debug(
                $"UserMessage {(needsToBeDelivered ? "will" : "will not")} be delivered because the " +
                $"ReceivingPMode {Config.Encode(pmode.Id)} MessageHandling.Deliver.IsEnabled={Config.Encode(pmode.MessageHandling.DeliverInformation.IsEnabled)} and " +
                $"the UserMessage {(userMessage.IsTest ? "is" : "isn't")} a test message and {(userMessage.IsDuplicate ? "is" : "isn't")} a duplicate one");

            return needsToBeDelivered;
        }
    }
}