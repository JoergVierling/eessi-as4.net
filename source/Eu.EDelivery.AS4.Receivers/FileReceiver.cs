﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Extensions;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Utilities;
using log4net;
using Function =
    System.Func<Eu.EDelivery.AS4.Model.Internal.ReceivedMessage, System.Threading.CancellationToken,
        System.Threading.Tasks.Task<Eu.EDelivery.AS4.Model.Internal.MessagingContext>>;

namespace Eu.EDelivery.AS4.Receivers
{
    /// <summary>
    /// <see cref="IReceiver" /> Implementation to receive Files
    /// </summary>
    [Info("FILE receiver")]
    public class FileReceiver : PollingTemplate<FileInfo, ReceivedMessage>, IReceiver
    {
        private const string FileLockName = "file.lock";

        private readonly SynchronizedCollection<(FileInfo file, string contentType)> _pendingFiles = 
            new SynchronizedCollection<(FileInfo, string)>();

        private bool _isReceiving = false;
        private FileReceiverSettings _settings;
        /// <summary>
        /// Initializes a new instance of the <see cref="FileReceiver" /> class
        /// </summary>
        public FileReceiver()
        {
            Logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        }

        [Info("File path", required: true)]
        [Description("Path to the folder to poll for new files")]
        private string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _settings.FilePath);

        [Info("File mask", required: true, defaultValue: "*.*")]
        [Description("Mask used to match files.")]
        private string FileMask => _settings.FileMask;

        [Info("Batch size", required: true, defaultValue: SettingKeys.BatchSizeDefault)]
        [Description("Indicates how many files should be processed per batch.")]
        private int BatchSize => _settings.BatchSize;

        [Info("Polling interval", defaultValue: SettingKeys.PollingIntervalDefault)]
        protected override TimeSpan PollingInterval => _settings.PollingInterval;

        protected override ILog Logger { get; }

        private static readonly string[] ExcludedExtensions = { ".pending", ".processing", ".accepted", ".exception", ".details", ".lock" };

        #region Configuration

        private static class SettingKeys
        {
            public const string FilePath = "FilePath";
            public const string FileMask = "FileMask";
            public const string BatchSize = "BatchSize";
            public const string BatchSizeDefault = "20";
            public const string PollingInterval = "PollingInterval";
            public const string PollingIntervalDefault = "00:00:03";
        }

        /// <summary>
        /// Configure the receiver with a given settings dictionary.
        /// </summary>
        /// <param name="settings"></param>
        public void Configure(FileReceiverSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;
        }

        /// <summary>
        /// Configure the receiver with a given settings dictionary.
        /// </summary>
        /// <param name="settings"></param>
        void IReceiver.Configure(IEnumerable<Setting> settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var properties = settings.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);

            var configuredBatchSize = properties.ReadOptionalProperty(SettingKeys.BatchSize, SettingKeys.BatchSizeDefault);

            if (Int32.TryParse(configuredBatchSize, out var batchSize) == false)
            {
                batchSize = 20;
            }

            _settings = new FileReceiverSettings(properties.ReadMandatoryProperty(SettingKeys.FilePath),
                                                 properties.ReadOptionalProperty(SettingKeys.FileMask, "*.*"),
                                                 batchSize,
                                                 ReadPollingIntervalFromProperties(properties));

            if (!Directory.Exists(FilePath))
            {
                Logger.Warn($"Directory: '{Config.Encode(FilePath)}' does not exists");
            }
        }

        private static TimeSpan ReadPollingIntervalFromProperties(Dictionary<string, string> properties)
        {
            if (properties.ContainsKey(SettingKeys.PollingInterval) == false)
            {
                return TimeSpan.Parse(SettingKeys.PollingIntervalDefault);
            }

            string pollingInterval = properties[SettingKeys.PollingInterval];
            return pollingInterval.AsTimeSpan(TimeSpan.Parse(SettingKeys.PollingIntervalDefault));
        }

        #endregion

        /// <summary>
        /// Start Receiving on the given File LocationParameter
        /// </summary>
        /// <param name="messageCallback"></param>
        /// <param name="cancellationToken"></param>
        public void StartReceiving(Function messageCallback, CancellationToken cancellationToken)
        {
            if (messageCallback == null)
            {
                throw new ArgumentNullException(nameof(messageCallback));
            }

            _isReceiving = true;
            Logger.Debug($"Start receiving on \"{Config.Encode(Path.GetFullPath(FilePath))}\" ...");
            StartPolling(messageCallback, cancellationToken);
        }

        /// <summary>
        /// Stop the <see cref="IReceiver"/> instance from receiving.
        /// </summary>
        public void StopReceiving()
        {
            _isReceiving = false;
            Logger.Debug($"Stop receiving on \"{Config.Encode(Path.GetFullPath(FilePath))}\"");
        }

        /// <summary>
        /// Declaration to where the Message are and can be polled
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override IEnumerable<FileInfo> GetMessagesToPoll(CancellationToken cancellationToken)
        {
            if (AddLockFile() == FileLock.Failure)
            {
                return Enumerable.Empty<FileInfo>();
            }

            var directoryInfo = new DirectoryInfo(FilePath);
            var resultedFiles = new List<FileInfo>();

            if (cancellationToken.IsCancellationRequested || _isReceiving == false)
            {
                return new FileInfo[] { };
            }

            FileInfo[] directoryFiles =
                directoryInfo.GetFiles(FileMask)
                             .Where(fi => ExcludedExtensions.Contains(fi.Extension) == false)
                             .Take(BatchSize).ToArray();

            try
            {
                foreach (FileInfo file in directoryFiles)
                {
                    try
                    {
                        string contentType = MimeTypeRepository.Instance.GetMimeTypeFromExtension(file.Extension);
                        var result = MoveFile(file, "pending");

                        if (result.success)
                        {
                            var pendingFile = new FileInfo(result.filename);

                            Logger.Trace(
                                $"Locked file {Config.Encode(file.Name)} to be processed and renamed it to {Config.Encode(pendingFile.Name)}");

                            _pendingFiles.Add((pendingFile, contentType));

                            resultedFiles.Add(pendingFile);
                        }
                    }
                    catch (IOException ex)
                    {
                        Logger.Info($"FileReceiver on \"{Config.Encode(file.FullName)}\" skipped since it is in use.");
                        Logger.Trace(Config.Encode(ex.Message));
                    }
                }
            }
            finally
            {
                RemoveFileLock();
            }

            return resultedFiles;
        }

        private enum FileLock { Created, Failure }

        private FileLock AddLockFile()
        {
            try
            {
                using (var fs = new FileStream(
                    Path.Combine(FilePath, FileLockName),
                    FileMode.CreateNew,
                    FileAccess.Write))
                {
                    fs.Close();
                }

                return FileLock.Created;
            }
            catch (IOException ex)
            {
                Logger.Trace(ex, "The lock file cannot be added, reason: " + ex.Message);
                return FileLock.Failure;
            }
        }

        private void RemoveFileLock()
        {
            try
            {
                File.Delete(Path.Combine(FilePath, FileLockName));
            }
            catch (IOException ex)
            {
                Logger.Trace(ex, "The lock file cannot be removed, reason: " + ex.Message);
            }
        }

        /// <summary>
        /// Declaration to the action that has to executed when a Message is received
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="messageCallback">Message Callback after the Message is received</param>
        /// <param name="token"></param>
        protected override async void MessageReceived(FileInfo entity, Function messageCallback, CancellationToken token)
        {
            Logger.Info($"Received message from Filesystem: \"{Config.Encode(entity.Name)}\"");
            if (!entity.Exists)
            {
                return;
            }


            var item = _pendingFiles.FirstOrDefault(f => f.file == entity);
            await OpenStreamFromMessage(item, messageCallback, token);
            _pendingFiles.Remove(item);
        }

        private async Task OpenStreamFromMessage(
            (FileInfo fileInfo, string contentType) _, 
            Function messageCallback, 
            CancellationToken token)
        {
            try
            {
                var result = MoveFile(_.fileInfo, "processing");

                if (result.success)
                {
                    MessagingContext messagingContext = null;

                    try
                    {
                        using (Stream fileStream = new FileStream(result.filename, FileMode.Open, FileAccess.Read))
                        {
                            fileStream.Seek(0, SeekOrigin.Begin);

                            var receivedMessage = new ReceivedMessage(
                                underlyingStream: fileStream, 
                                contentType: _.contentType,
                                origin: result.filename,
                                length: _.fileInfo.Length);
                            messagingContext = await messageCallback(receivedMessage, token).ConfigureAwait(false);
                        }

                        await NotifyReceivedFile(_.fileInfo, messagingContext).ConfigureAwait(false);
                    }
                    finally
                    {
                        messagingContext?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occured while processing \"{Config.Encode(_.fileInfo.Name)}\"");
                Logger.Trace(Config.Encode(ex.Message));
            }
        }

        private async Task NotifyReceivedFile(FileInfo fileInfo, MessagingContext messagingContext)
        {
            if (messagingContext.Exception != null)
            {
                await HandleException(fileInfo, messagingContext.Exception);
            }
            else
            {
                MoveFile(fileInfo, "accepted");
            }
        }

        private async Task HandleException(FileInfo fileInfo, Exception exception)
        {
            MoveFile(fileInfo, "exception");
            await CreateExceptionFile(fileInfo, exception);
        }

        private async Task CreateExceptionFile(FileSystemInfo fileInfo, Exception exception)
        {
            string fileName = fileInfo.FullName + ".details";
            Logger.Info($"Exception Details are stored at: \"{Config.Encode(fileName)}\"");

            using (var streamWriter = new StreamWriter(fileName))
            {
                await streamWriter.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            }
        }
       
        protected override void ReleasePendingItems()
        {
            // Rename the 'pending' files to their original filename.
            string extension = Path.GetExtension(FileMask);

            lock (_pendingFiles.SyncRoot)
            {
                for (int i = _pendingFiles.Count - 1; i >= 0; i--)
                {
                    var item = _pendingFiles[i];

                    if (File.Exists(item.file.FullName))
                    {
                        MoveFile(item.file, extension);
                    }

                    _pendingFiles.Remove(item);
                }
            }
        }

        /// <summary>
        /// Describe what to do in case of an Exception
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="exception"></param>
        protected override void HandleMessageException(FileInfo fileInfo, Exception exception)
        {
            Logger.Error(Config.Encode(exception.Message));
            MoveFile(fileInfo, "exception");
        }

        /// <summary>
        /// Move file to another place on the File System
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="extension"></param>
        private (bool success, string filename) MoveFile(FileInfo fileInfo, string extension)
        {
            extension = extension.TrimStart('.');
            Logger.Trace($"Renaming file '{Config.Encode(fileInfo.Name)}'...");
            string destFileName =
                $"{fileInfo.Directory?.FullName}\\{Path.GetFileNameWithoutExtension(fileInfo.FullName)}.{extension}";

            try
            {
                destFileName = FilenameUtils.EnsureFilenameIsUnique(destFileName);

                int attempts = 0;

                do
                {
                    try
                    {
                        fileInfo.MoveTo(destFileName);
                        attempts = 5;
                    }
                    catch (IOException)
                    {
                        // When the file is in use, an IO exception will be thrown.
                        // If that is the case, wait a little and retry.                       
                        if (attempts == 5)
                        {
                            throw;
                        }
                        attempts++;
                        Thread.Sleep(500);
                    }
                } while (attempts < 5);

                Logger.Trace($"File renamed to: '{Config.Encode(fileInfo.Name)}'");

                return (success: true, filename: destFileName);
            }
            catch (Exception ex)
            {
                Logger.Error($"Unable to MoveFile \"{Config.Encode(fileInfo.FullName)}\" to \"{Config.Encode(destFileName)}\"");
                Logger.Error(Config.Encode(ex.Message));
                Logger.Trace(Config.Encode(ex.StackTrace));
                return (success: false, filename: string.Empty);
            }
        }

    }
}