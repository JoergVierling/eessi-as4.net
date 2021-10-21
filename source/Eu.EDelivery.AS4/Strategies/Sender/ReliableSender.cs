﻿using System;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Model.Deliver;
using Eu.EDelivery.AS4.Model.Notify;
using Eu.EDelivery.AS4.Model.PMode;
using log4net;
using MessageInfo = Eu.EDelivery.AS4.Model.Common.MessageInfo;

namespace Eu.EDelivery.AS4.Strategies.Sender
{
    /// <summary>
    /// Decorator to add the 'reliable' functionality of the sending functionality 
    /// to both the <see cref="IDeliverSender"/> and <see cref="INotifySender"/> implementation.
    /// </summary>
    internal class ReliableSender : IDeliverSender, INotifySender
    {
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        internal IDeliverSender InnerDeliverSender { get; }
        internal INotifySender InnerNotifySender { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReliableSender"/> class.
        /// </summary>
        /// <param name="deliverSender"></param>
        public ReliableSender(IDeliverSender deliverSender)
        {
            if (deliverSender == null)
            {
                throw new ArgumentNullException(nameof(deliverSender));
            }

            InnerDeliverSender = deliverSender;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReliableSender"/> class.
        /// </summary>
        /// <param name="notifySender"></param>
        public ReliableSender(INotifySender notifySender)
        {
            if (notifySender == null)
            {
                throw new ArgumentNullException(nameof(notifySender));
            }

            InnerNotifySender = notifySender;
        }

        /// <summary>
        /// Configure the <see cref="INotifySender"/>
        /// with a given <paramref name="method"/>
        /// </summary>
        /// <param name="method"></param>
        public void Configure(Method method)
        {
            InnerDeliverSender?.Configure(method);
            InnerNotifySender?.Configure(method);
        }

        /// <summary>
        /// Start sending the <see cref="DeliverMessage"/>
        /// </summary>
        /// <param name="envelope"></param>
        public async Task<SendResult> SendAsync(DeliverMessageEnvelope envelope)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return await SendMessageResult(
                    message: envelope,
                    sending: InnerDeliverSender.SendAsync,
                    exMessage: $"(Deliver)[{envelope.Message.MessageInfo?.MessageId}] Unable to send DeliverMessage to the configured endpoint due to an exception")
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Start sending the <see cref="NotifyMessage"/>
        /// </summary>
        /// <param name="notifyMessage"></param>
        public async Task<SendResult> SendAsync(NotifyMessageEnvelope notifyMessage)
        {
            if (notifyMessage == null)
            {
                throw new ArgumentNullException(nameof(notifyMessage));
            }

            return await SendMessageResult(
                message: notifyMessage,
                sending: InnerNotifySender.SendAsync,
                exMessage: $"(Notify)[{notifyMessage?.MessageInfo?.MessageId}] Unable to send NotifyMessage to the configured endpoint due to and exceptoin")
                .ConfigureAwait(false);
        }

        private static async Task<SendResult> SendMessageResult<T>(
            T message,  
            Func<T, Task<SendResult>> sending, 
            string exMessage)
        {
            try
            {
                return await sending(message);
            }
            catch (Exception ex)
            {
                LogExceptionIncludingInner(ex, exMessage);
                return SendResult.FatalFail;
            }
        }

        private static void LogExceptionIncludingInner(Exception ex, string exMessage)
        {

            Logger.Error(exMessage);

            if (ex.InnerException != null)
            {
                Logger.Error(ex.InnerException.Message);
            }
        }
    }
}
