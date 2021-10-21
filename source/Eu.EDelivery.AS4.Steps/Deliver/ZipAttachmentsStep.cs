﻿using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Streaming;
using log4net;

namespace Eu.EDelivery.AS4.Steps.Deliver
{
    /// <summary>
    /// <see cref="IStep"/> implementation to .zip the attachments to one file
    /// </summary>
    [Info("Zip payloads in one archive")]
    [Description("If the received AS4 Message contains multiple attachments, then this step zips them into one payload.")]
    public class ZipAttachmentsStep : IStep
    {
        private static readonly ILog Logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Start zipping <see cref="Attachment"/> Models
        /// </summary>
        /// <param name="messagingContext"></param>
        /// <returns></returns>
        public async Task<StepResult> ExecuteAsync(MessagingContext messagingContext)
        {
            if (messagingContext?.AS4Message == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(ZipAttachmentsStep)} requires an AS4Message to zip the attachments but no AS4Message is present in the MessagingContext");
            }

            if (messagingContext.AS4Message.Attachments.Count() > 1)
            {
                Stream zippedStream = await ZipAttachmentsInAS4MessageAsync(messagingContext.AS4Message).ConfigureAwait(false);
                Attachment zipAttachment = CreateZippedAttachment(zippedStream);

                OverwriteAttachmentEntries(messagingContext.AS4Message, zipAttachment);
            }

            Logger.Info($"{messagingContext.LogTag} Zip the Attachments to a single file");
            return StepResult.Success(messagingContext);
        }

        private static async Task<Stream> ZipAttachmentsInAS4MessageAsync(AS4Message message)
        {
            var stream = new VirtualStream(forAsync: true);

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (Attachment attachment in message.Attachments)
                {
                    ZipArchiveEntry archiveEntry = CreateAttachmentEntry(archive, attachment);
                    await AddAttachmentStreamToEntryAsync(attachment.Content, archiveEntry).ConfigureAwait(false);
                }
            }

            stream.Position = 0;
            return stream;
        }

        private static ZipArchiveEntry CreateAttachmentEntry(ZipArchive archive, Attachment attachment)
        {
            string entryName = attachment.Id + MimeTypeRepository.Instance.GetExtensionFromMimeType(attachment.ContentType);
            return archive.CreateEntry(entryName, CompressionLevel.Optimal);
        }

        private static async Task AddAttachmentStreamToEntryAsync(Stream attachmentStream, ZipArchiveEntry entry)
        {
            using (Stream entryStream = entry.Open())
            {
                await attachmentStream.CopyToAsync(entryStream).ConfigureAwait(false);
            }
        }

        private static Attachment CreateZippedAttachment(Stream stream)
        {
            return new Attachment(
                id: Guid.NewGuid().ToString(),
                content: stream,
                contentType: "application/zip");
        }

        private static void OverwriteAttachmentEntries(AS4Message message, Attachment zipAttachment)
        {
            message.RemoveAllAttachments();
            message.AddAttachment(zipAttachment);
        }
    }
}
