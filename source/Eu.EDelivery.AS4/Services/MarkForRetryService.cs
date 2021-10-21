﻿using System;
using System.Collections.Generic;
using System.Linq;
using Eu.EDelivery.AS4.Extensions;
using Eu.EDelivery.AS4.Entities;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Strategies.Sender;
using log4net;
using Eu.EDelivery.AS4.Common;

namespace Eu.EDelivery.AS4.Services
{
    /// <summary>
    /// Service abstraction to set the referenced deliver message to the right Status/Operation accordingly to the <see cref="SendResult"/>.
    /// </summary>
    internal class MarkForRetryService
    {
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        private readonly IDatastoreRepository _repository;

        /// <summary>
        /// Initializes a new instance of the <see cref="MarkForRetryService"/> class.
        /// </summary>
        /// <param name="repository">The repository.</param>
        public MarkForRetryService(IDatastoreRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            _repository = repository;
        }

        /// <summary>
        /// Updates the AS4Message's Status/Operation accordingly to the status of the 
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="status"></param>
        public void UpdateAS4MessageForSendResult(long messageId, SendResult status)
        {
            _repository.UpdateOutMessage(
                outMessageId: messageId,
                updateAction: entity => UpdateMessageEntity(
                    resultOfOperation: status,
                    entityToBeRetried: entity,
                    getRetryEntries: () => _repository.GetRetryReliability(r => r.RefToOutMessageId == entity.Id, r => r),
                    onCompleted: e =>
                    {
                        Logger.Trace("Update OutMessage with Status and Operation set to Sent");

                        e.SetStatus(OutStatus.Sent);
                        e.Operation = Operation.Sent;
                    },
                    onDeadLettered: e =>
                    {
                        Logger.Debug($"AS4Message failed during the sending, exhausted retries");
                        e.SetStatus(OutStatus.Exception);
                    }));
        }

        /// <summary>
        /// Updates the DeliverMessage's Status/Operation accordingly to <see cref="SendResult"/>.
        /// </summary>
        /// <param name="messageId">The message identifier.</param>
        /// <param name="status">The upload status during the delivery of the payloads.</param>
        public void UpdateDeliverMessageForUploadResult(string messageId, SendResult status)
        {
            if (messageId == null)
            {
                throw new ArgumentNullException(nameof(messageId));
            }

            _repository.UpdateInMessage(
                messageId: messageId,
                updateAction: entity => UpdateMessageEntity(
                    resultOfOperation: status,
                    entityToBeRetried: entity,
                    getRetryEntries: () => _repository.GetRetryReliability(r => r.RefToInMessageId == entity.Id, r => r),
                    onCompleted: _ => Logger.Trace("Attachments are uploaded successfully, no retry is needed"),
                    onDeadLettered: e =>
                    {
                        Logger.Debug("DeliverMessage failed during the delivery, exhausted retries");
                        e.SetStatus(InStatus.Exception);
                    }));
        }

        /// <summary>
        /// Updates the DeliverMessage's Status/Operation accordingly to <see cref="Eu.EDelivery.AS4.Strategies.Sender.SendResult"/>.
        /// </summary>
        /// <param name="messageId">The message identifier.</param>
        /// <param name="status">The deliver status during the delivery of the deliver message.</param>
        /// <returns></returns>
        public void UpdateDeliverMessageForDeliverResult(string messageId, SendResult status)
        {
            if (messageId == null)
            {
                throw new ArgumentNullException(nameof(messageId));
            }

            _repository.UpdateInMessage(
                messageId: messageId,
                updateAction: entity => UpdateMessageEntity(
                    resultOfOperation: status,
                    entityToBeRetried: entity,
                    getRetryEntries: () => _repository.GetRetryReliability(r => r.RefToInMessageId == entity.Id, r => r),
                    onCompleted: e =>
                    {
                        Logger.Debug("Update InMessage with Status and Operation set to Delivered");

                        e.SetStatus(InStatus.Delivered);
                        e.Operation = Operation.Delivered;
                    },
                    onDeadLettered: e =>
                    {
                        Logger.Debug("DeliverMessage failed during the delivery, exhausted retries");
                        e.SetStatus(InStatus.Exception);
                    }));
        }

        /// <summary>
        /// Updates the NotifyMessage stored as <see cref="InMessage"/> in the datastore, accordingly to the given notification result.
        /// </summary>
        /// <param name="messageId">Identifier to update the <see cref="InMessage"/></param>
        /// <param name="result">Notification result used to determine the right update values for the to be updated entity</param>
        public void UpdateNotifyMessageForIncomingMessage(long messageId, SendResult result)
        {
            _repository.UpdateInMessage(
                id: messageId,
                update: entity => UpdateMessageEntity(
                    resultOfOperation: result,
                    entityToBeRetried: entity,
                    getRetryEntries: () => _repository.GetRetryReliability(r => r.RefToInMessageId == entity.Id, r => r),
                    onCompleted: e =>
                    {
                        Logger.Debug("Update InMessage with Status and Operation set to Notified");

                        e.SetStatus(InStatus.Notified);
                        e.Operation = Operation.Notified;
                    },
                    onDeadLettered: m =>
                    {
                        Logger.Debug("NotifyMessage failed during the notification, exhausted retries");
                        m.SetStatus(InStatus.Exception);
                    }));
        }

        /// <summary>
        /// Updates the NotifyMessage stored as <see cref="OutMessage"/> in the datastore, accordingly to the given notification result.
        /// </summary>
        /// <param name="messageId">Identifier to update the <see cref="OutMessage"/></param>
        /// <param name="result">Notification result used to determine the right update values for the to be updated entity</param>
        public void UpdateNotifyMessageForOutgoingMessage(long messageId, SendResult result)
        {
            _repository.UpdateOutMessage(
                messageId,
                entity => UpdateMessageEntity(
                    resultOfOperation: result,
                    entityToBeRetried: entity,
                    getRetryEntries: () => _repository.GetRetryReliability(r => r.RefToOutMessageId == entity.Id, r => r),
                    onCompleted: m =>
                    {
                        Logger.Debug("Update InMessage with Status and Operation set to Notified");

                        m.SetStatus(OutStatus.Notified);
                        m.Operation = Operation.Notified;
                    },
                    onDeadLettered: m =>
                    {
                        Logger.Debug("NotifyMessage failed during the notification, exhausted retries");
                        m.SetStatus(OutStatus.Exception);
                    }));
        }

        private static void UpdateMessageEntity<T>(
            SendResult resultOfOperation,
            T entityToBeRetried,
            Func<IEnumerable<RetryReliability>> getRetryEntries,
            Action<T> onCompleted,
            Action<T> onDeadLettered) where T : MessageEntity
        {
            // Only for records that are not yet been completly Notified/DeadLettered should we botter to retry
            if (entityToBeRetried.Operation == Operation.Notified
                || entityToBeRetried.Operation == Operation.DeadLettered)
            {
                return;
            }

            RetryReliability rr = getRetryEntries().FirstOrDefault();
            if (resultOfOperation == SendResult.Success)
            {
                onCompleted(entityToBeRetried);

                Logger.Trace("Successful result, so update RetryReliability.Status=Completed");

                if (rr != null)
                {
                    rr.Status = RetryStatus.Completed;
                }
            }
            else
            {
                if (rr == null)
                {
                    Logger.Debug("Message can't be retried because no RetryReliability is configured");
                    Logger.Debug($"Update {Config.Encode(typeof(T).Name)} with {{Status=Exception, Operation=DeadLettered}}");

                    onDeadLettered(entityToBeRetried);
                    entityToBeRetried.Operation = Operation.DeadLettered;
                }
                else if (resultOfOperation == SendResult.RetryableFail)
                {
                    Logger.Debug($"Message failed this time, set for retry by updating {Config.Encode(typeof(T).Name)}.Operation=ToBeRetried");

                    entityToBeRetried.Operation = Operation.ToBeRetried;
                    rr.Status = RetryStatus.Pending;
                }
                else
                {
                    Logger.Debug("Message failed this time due to a fatal result during sending");
                    Logger.Debug($"Update {Config.Encode(typeof(T).Name)} with Status=Exception, Operation=DeadLettered");

                    onDeadLettered(entityToBeRetried);
                    entityToBeRetried.Operation = Operation.DeadLettered;
                    rr.Status = RetryStatus.Completed;
                }
            }
        }

        /// <summary>
        /// Updates the NotifyMessage stored as <see cref="InException"/> in the datastore, accordingly to the given notification result.
        /// </summary>
        /// <param name="messageId">Identifier to update the <see cref="InException"/></param>
        /// <param name="result">Notification result used to determine the right update values for the to be updated entity</param>
        public void UpdateNotifyExceptionForIncomingMessage(long messageId, SendResult result)
        {
            _repository.UpdateInException(
                id: messageId,
                update: exEntity => UpdateExceptionRetry(
                    resultOfOperation: result,
                    entityToBeRetried: exEntity,
                    getRetryEntries: () => _repository.GetRetryReliability(r => r.RefToInExceptionId == exEntity.Id, r => r)));
        }

        /// <summary>
        /// Updates the NotifyMessage stored as <see cref="OutException"/> in the datastore, accordingly to the given notification result.
        /// </summary>
        /// <param name="messageId">Identifier to update the <see cref="OutException"/></param>
        /// <param name="result">Notification result used to determine the right update values for the to be updated entity</param>
        public void UpdateNotifyExceptionForOutgoingMessage(long messageId, SendResult result)
        {
            _repository.UpdateOutException(
                id: messageId,
                update: exEntity => UpdateExceptionRetry(
                    resultOfOperation: result,
                    entityToBeRetried: exEntity,
                    getRetryEntries: () => _repository.GetRetryReliability(r => r.RefToOutExceptionId == exEntity.Id, r => r)));
        }

        private static void UpdateExceptionRetry<T>(
            SendResult resultOfOperation,
            T entityToBeRetried,
            Func<IEnumerable<RetryReliability>> getRetryEntries) where T : ExceptionEntity
        {
            // There could be more In/Out Exceptions for a single message, 
            // therefore we should only look for exceptions that are not yet been Notified/DeadLettered.
            if (entityToBeRetried.Operation == Operation.Notified 
                || entityToBeRetried.Operation == Operation.DeadLettered)
            {
                return;
            }

            RetryReliability rr = getRetryEntries().FirstOrDefault();
            string reftoMessageId =
                entityToBeRetried.EbmsRefToMessageId == null
                ? String.Empty
                : $"[{entityToBeRetried.EbmsRefToMessageId}]";

            if (resultOfOperation == SendResult.Success)
            {
                Logger.Debug($"Update {Config.Encode(typeof(T).Name)} {Config.Encode(reftoMessageId)} with Operation=Notified");
                entityToBeRetried.Operation = Operation.Notified;

                if (rr != null)
                {
                    rr.Status = RetryStatus.Completed;
                }
            }
            else
            {
                if (rr == null)
                {
                    Logger.Debug($"{Config.Encode(typeof(T).Name)} NotifyMessage failed during the notification, exhausted retries");
                    Logger.Debug($"Update {Config.Encode(typeof(T).Name)} with {{Status=Exception, Operation=DeadLettered}}");

                    entityToBeRetried.Operation = Operation.DeadLettered;
                }
                else if (resultOfOperation == SendResult.RetryableFail)
                {
                    Logger.Debug($"{Config.Encode(typeof(T).Name)} NotifyMessage failed this time, will be retried by updating Operation=ToBeRetried");

                    entityToBeRetried.Operation = Operation.ToBeRetried;
                    rr.Status = RetryStatus.Pending;
                }
                else
                {
                    Logger.Debug($"{Config.Encode(typeof(T).Name)} NotifyMessage failed during the notification, exhausted retries");
                    Logger.Debug($"Update {Config.Encode(typeof(T).Name)} with {{Status=Exception, Operation=DeadLettered}}");

                    entityToBeRetried.Operation = Operation.DeadLettered;
                    rr.Status = RetryStatus.Completed;
                }
            }
        }
    }
}
