﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Extensions;
using Eu.EDelivery.AS4.Model.Common;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Deliver;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Model.PMode;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Services;
using Eu.EDelivery.AS4.Strategies.Sender;
using Eu.EDelivery.AS4.Strategies.Uploader;
using log4net;

namespace Eu.EDelivery.AS4.Steps.Deliver
{
    /// <summary>
    /// Describes how the message payloads are uploaded to their respective media
    /// </summary>
    [Info("Upload attachments to deliver location")]
    [Description("This step uploads the deliver message payloads to the destination that was configured in the receiving pmode.")]
    public class UploadAttachmentsStep : IStep
    {
        private readonly Func<DatastoreContext> _createDbContext;
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        private readonly IAttachmentUploaderProvider _provider;

        /// <summary>
        /// Initializes a new instance of the <see cref="UploadAttachmentsStep" /> class.
        /// </summary>
        public UploadAttachmentsStep() 
            : this(AttachmentUploaderProvider.Instance, Registry.Instance.CreateDatastoreContext) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="UploadAttachmentsStep" /> class.
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <param name="createDbContext">Creates a database context.</param>
        public UploadAttachmentsStep(IAttachmentUploaderProvider provider, Func<DatastoreContext> createDbContext)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (createDbContext == null)
            {
                throw new ArgumentNullException(nameof(createDbContext));
            }

            _provider = provider;
            _createDbContext = createDbContext;
        }

        /// <summary>
        /// Start uploading the AS4 Message Payloads
        /// </summary>
        /// <param name="messagingContext"></param>
        /// <returns></returns>
        public async Task<StepResult> ExecuteAsync(MessagingContext messagingContext)
        {
            if (messagingContext == null)
            {
                throw new ArgumentNullException(nameof(messagingContext));
            }

            if (messagingContext.DeliverMessage == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(UploadAttachmentsStep)} requires a DeliverMessage to upload the attachments from but no DeliverMessage is present in the MessagingContext");
            }

            DeliverMessageEnvelope deliverEnvelope = messagingContext.DeliverMessage;
            if (!deliverEnvelope.Attachments.Any())
            {
                Logger.Debug("(Deliver) No attachments to upload for DeliverMessage");
                return StepResult.Success(messagingContext);
            }

            if (messagingContext.ReceivingPMode == null)
            {
                throw new InvalidOperationException(
                    "Unable to send DeliverMessage: no ReceivingPMode is set");
            }

            if (messagingContext.ReceivingPMode.MessageHandling?.DeliverInformation?.PayloadReferenceMethod == null)
            {
                throw new InvalidOperationException(
                    $"Unable to send the DeliverMessage: the ReceivingPMode {messagingContext.ReceivingPMode.Id} "
                    + "does not contain any <PayloadReferenceMethod/> element in the MessageHandling.Deliver element. "
                    + "Please provide a correct <PayloadReferenceMethod/> tag to indicate where the attachments of the DeliverMessage should be sent to.");
            }

            if (messagingContext.ReceivingPMode.MessageHandling.DeliverInformation.PayloadReferenceMethod.Type == null)
            {
                throw new InvalidOperationException(
                    $"Unable to send the DeliverMessage: the ReceivingPMode {messagingContext.ReceivingPMode.Id} "
                    + "does not contain any <Type/> element in the MessageHandling.Deliver.PayloadReferenceMethod element "
                    + "that indicates which uploading strategy that must be used."
                    + "Default uploading strategies are: 'FILE' and 'HTTP'. See 'Deliver Uploading' for more information");
            }

            IAttachmentUploader uploader = GetAttachmentUploader(messagingContext.ReceivingPMode);
            var results = new Collection<UploadResult>();

            foreach (Attachment att in deliverEnvelope.Attachments)
            {
                UploadResult result = await TryUploadAttachmentAsync(att, deliverEnvelope, uploader).ConfigureAwait(false);
                results.Add(result);
            }

            SendResult accResult = results
                .Select(r => r.Status)  
                .Aggregate(SendResultUtils.Reduce);

            if (accResult == SendResult.Success)
            {
                return StepResult.Success(messagingContext);
            }
            
            return StepResult.Failed(messagingContext);
        }

        private IAttachmentUploader GetAttachmentUploader(ReceivingProcessingMode pmode)
        {
            Method payloadReferenceMethod = pmode.MessageHandling.DeliverInformation.PayloadReferenceMethod;
            IAttachmentUploader uploader = _provider.Get(payloadReferenceMethod.Type);
            if (uploader == null)
            {
                throw new ArgumentNullException(
                    nameof(uploader),
                    $@"No {nameof(IAttachmentUploader)} can be found for PayloadReferenceMethod.Type = {payloadReferenceMethod.Type}");
            }

            uploader.Configure(payloadReferenceMethod);
            return uploader;
        }

        private static async Task<UploadResult> TryUploadAttachmentAsync(
            Attachment attachment, 
            DeliverMessageEnvelope deliverMessage,
            IAttachmentUploader uploader)
        {
            try
            {
                Logger.Trace($"Start Uploading Attachment {Config.Encode(attachment.Id)}...");
                Logger.Info($"UploadAttachmentsStep -> Start Uploading Attachment {Config.Encode(attachment.Id)}...");
                Task<UploadResult> uploadAsync = uploader.UploadAsync(attachment, deliverMessage.Message.MessageInfo);
                if (uploadAsync == null)
                {
                    throw new ArgumentNullException(
                        nameof(uploadAsync),
                        $@"{uploader.GetType().Name} returns 'null' for Attachment {attachment.Id}");
                }

                UploadResult attachmentResult = await uploadAsync.ConfigureAwait(false);

                attachment.ResetContentPosition();

                Payload referencedPayload = 
                    deliverMessage.Message.Payloads.FirstOrDefault(attachment.Matches);

                if (referencedPayload == null)
                {
                    throw new InvalidOperationException(
                        $"No referenced <Payload/> element found in DeliverMessage to assign the upload location to with attachment Id = {attachment.Id}");
                }

                referencedPayload.Location = attachmentResult.DownloadUrl;

                Logger.Trace($"Attachment {Config.Encode(attachment.Id)} uploaded succesfully");
                Logger.Info($"UploadAttachmentsStep -> Attachment {Config.Encode(attachment.Id)} uploaded succesfully");
                return attachmentResult;
            }
            catch (Exception exception)
            {
                Logger.Error($"Attachment {Config.Encode(attachment.Id)} cannot be uploaded because of an exception: {Config.Encode(Environment.NewLine)}" + Config.Encode(exception));
                return UploadResult.FatalFail;
            }
        }

        private async Task UpdateDeliverMessageAccordinglyToUploadResult(string messageId, SendResult status)
        {
            using (DatastoreContext context = _createDbContext())
            {
                var repository = new DatastoreRepository(context);
                var service = new MarkForRetryService(repository);

                service.UpdateDeliverMessageForUploadResult(messageId, status);
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }
}