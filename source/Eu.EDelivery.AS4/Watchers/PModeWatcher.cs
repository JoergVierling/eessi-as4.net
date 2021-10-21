﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using System.Xml.Serialization;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Extensions;
using Eu.EDelivery.AS4.Model.PMode;
using Eu.EDelivery.AS4.Validators;
using FluentValidation;
using FluentValidation.Results;
using log4net;

namespace Eu.EDelivery.AS4.Watchers
{
    /// <summary>
    /// Watcher to check if there's a new <see cref="SendingProcessingMode"/>/<see cref="ReceivingProcessingMode"/> available
    /// </summary>
    /// <typeparam name="T">PMode type that's either a <see cref="SendingProcessingMode"/> or a <see cref="ReceivingProcessingMode"/></typeparam>
    /// TODO: moves the initial pmode loading to a factory method instead of overloading the ctor of this type.
    internal class PModeWatcher<T> : IDisposable where T : class, IPMode
    {
        private readonly ConcurrentDictionary<string, ConfiguredPMode> _pmodes = new ConcurrentDictionary<string, ConfiguredPMode>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _filePModeIdMap = new ConcurrentDictionary<string, string>();

        private readonly AbstractValidator<T> _pmodeValidator;
        private readonly FileSystemWatcher _watcher;

        // ReSharper disable once StaticMemberInGenericType - same instance will be used for the same generic type but it's not a problem.
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );
        private static readonly XmlSerializer XmlSerializer = new XmlSerializer(typeof(T));
        private static readonly string PModeName = 
            typeof(T) == typeof(SendingProcessingMode) 
                ? "SendingPMode" 
                : typeof(T) == typeof(ReceivingProcessingMode)
                    ? "ReceivingPMode"
                    : "PMode";

        /// <summary>
        /// Initializes a new instance of the <see cref="PModeWatcher{T}" /> class
        /// </summary>
        /// <param name="path">The path on which this watcher should look for <see cref="IPMode"/> implementations.</param>
        /// <param name="validator">The validator to use when retrieving <see cref="IPMode"/> implementations.</param>
        internal PModeWatcher(
            string path, 
            AbstractValidator<T> validator)
        {
            _pmodeValidator = validator;

            _watcher = new FileSystemWatcher(path, "*.xml") { IncludeSubdirectories = true };
            _watcher.Changed += OnChanged;
            _watcher.Created += OnCreated;
            _watcher.Deleted += OnDeleted;
            _watcher.NotifyFilter =
                NotifyFilters.LastAccess
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName;

            RetrievePModes(_watcher.Path);
        }

        /// <summary>
        /// Start watching for pmodes.
        /// </summary>
        internal void Start()
        {
            _watcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// Stop watching for pmodes
        /// </summary>
        internal void Stop()
        {
            _watcher.EnableRaisingEvents = false;
        }

        /// <summary>
        /// Verify if the Watcher contains a <see cref="IPMode"/> for a given <paramref name="id"/>.
        /// </summary>
        /// <param name="id">Id for which the verification is done.</param>
        /// <returns>A value indicating whether or not there exists a <see cref="T"/> for this <paramref name="id"/>.</returns>
        internal bool ContainsPMode(string id)
        {
            return _pmodes.ContainsKey(id);
        }

        /// <summary>
        /// Gets the <see cref="ConfiguredPMode"/> entry for a given <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <exception cref="ArgumentException">The specified PMode key is invalid. - key</exception>
        internal ConfiguredPMode GetPModeEntry(string key)
        {
            if (String.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException($@"The specified {PModeName} key is invalid.", nameof(key));
            }

            _pmodes.TryGetValue(key, out ConfiguredPMode configuredPMode);
            return configuredPMode;
        }

        /// <summary>
        /// Gets the p modes cached inside the watcher.
        /// </summary>
        /// <returns></returns>
        internal IEnumerable<IPMode> GetPModes()
        {
            return _pmodes.Values.Select(p => p.PMode);
        }

        private void RetrievePModes(string pmodeFolder)
        {
            var startDir = new DirectoryInfo(pmodeFolder);
            IEnumerable<FileInfo> files = TryGetFiles(startDir);

            foreach (FileInfo file in files)
            {
                AddOrUpdateConfiguredPMode(file.FullName);
            }
        }

        private static IEnumerable<FileInfo> TryGetFiles(DirectoryInfo startDir)
        {
            try
            {
                return startDir.GetFiles("*.xml", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occured while trying to get {Config.Encode(PModeName)} files: {Config.Encode(ex.Message)}");
                return new List<FileInfo>();
            }
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            AddOrUpdateConfiguredPMode(Path.GetFullPath(e.FullPath));
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            AddOrUpdateConfiguredPMode(Path.GetFullPath(e.FullPath));
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            string key = _pmodes.FirstOrDefault(p => p.Value.Filename.Equals(e.FullPath)).Key;

            if (key != null)
            {
                Logger.Trace($"Remove {Config.Encode(PModeName)} with Id: " + key);
                _pmodes.TryRemove(key, out _);
            }
        }

        private readonly object __cacheLock = new object();

        private void AddOrUpdateConfiguredPMode(string fullPath)
        {
            lock (__cacheLock)
            {
                if (_fileEventCache.Contains(fullPath))
                {
                    Logger.Trace($"{Config.Encode(PModeName)} {Config.Encode(fullPath)} has already been handled.");
                    return;
                }

                _fileEventCache.Add(fullPath, fullPath, _policy);
            }

            T pmode = TryDeserialize(fullPath);
            if (pmode == null)
            {
                Logger.Warn($"File at: \'{Config.Encode(fullPath)}\' cannot be converted to a {Config.Encode(PModeName)} because the XML in the file isn\'t valid.");

                // Since the PMode that we expect in this file is invalid, it
                // must be removed from our cache.
                RemoveLocalPModeFromCache(fullPath);
                return;
            }

            ValidationResult pmodeValidation = _pmodeValidator.Validate(pmode);
            if (!pmodeValidation.IsValid)
            {
                Logger.Warn($"Invalid {Config.Encode(PModeName)} at: \'{Config.Encode(fullPath)}\'");
                pmodeValidation.LogErrors(Logger);

                // Since the PMode that we expect isn't valid according to the validator, it
                // must be removed from our cache.
                RemoveLocalPModeFromCache(fullPath);
                return;
            }

            var configuredPMode = new ConfiguredPMode(fullPath, pmode);

            if (_pmodes.ContainsKey(pmode.Id))
            {
                Logger.Warn($"Existing PMode {Config.Encode(pmode.Id)} will be overwritten with PMode from {Config.Encode(fullPath)}");
            }
            else
            {
                Logger.Trace($"Add new {Config.Encode(PModeName)} with Id: " + pmode.Id);
            }

            _pmodes.AddOrUpdate(pmode.Id, configuredPMode, (key, value) => configuredPMode);
            _filePModeIdMap.AddOrUpdate(fullPath, pmode.Id, (key, value) => pmode.Id);
        }

        //// cache which keeps track of the date and time a PMode file was last handled by the FileSystemWatcher.
        //// Due to an issue with FileSystemWatcher, events can be triggered multiple times for the same operation on the 
        //// same file.

        private readonly MemoryCache _fileEventCache = MemoryCache.Default;

        private readonly CacheItemPolicy _policy = new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMilliseconds(500) };

        private static T TryDeserialize(string path)
        {
            try
            {
                var retryCount = 0;
                while (IsFileLocked(path) && retryCount < 10)
                {
                    // Wait till the file lock is released ...
                    System.Threading.Thread.Sleep(50);
                    retryCount++;
                }

                void OnUnknownXmlElement(object sender, XmlElementEventArgs e)
                {
                    if (e.Element.LocalName == "SendingPMode"
                        && e.ObjectBeingDeserialized is ReplyHandling)
                    {
                        Logger.Warn(
                            $"ReceivingPMode at {Config.Encode(path)} still has a ReplyHandling.SendingPMode element."
                            + $"{Config.Encode(Environment.NewLine)} SendingPModes are not used anymore for responding to AS4 messages. "
                            + "Please upgrade your PMode by executing the script ./scripts/copy-responsepmode-to-receivingpmode.ps1."
                            + $"{Config.Encode(Environment.NewLine)} For more information see the wiki section: \"Remove Sending PMode as responding PMode\"");
                    }
                    else
                    {
                        Logger.Warn(
                            $"Unknown XML element found while deserializing the {Config.Encode(PModeName)} -> {Config.Encode(e.Element.LocalName)} "
                            + $"at {Config.Encode(path)} ({Config.Encode(e.LineNumber)},{Config.Encode(e.LinePosition)}). {Config.Encode(Environment.NewLine)} "
                            + $"Expected elements: {Config.Encode(Environment.NewLine)} - {e.ExpectedElements.Replace(", ", Environment.NewLine + " - ")}");
                    }

                }

                using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    XmlSerializer.UnknownElement += OnUnknownXmlElement;
                    var result = XmlSerializer.Deserialize(fileStream) as T;
                    XmlSerializer.UnknownElement -= OnUnknownXmlElement;

                    return result;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occured while deserializing {Config.Encode(PModeName)} at {Config.Encode(path)}");
                Logger.Error(Config.Encode(ex.Message));
                if (ex.InnerException != null)
                {
                    Logger.Error(Config.Encode(ex.InnerException.Message));
                }

                return null;
            }
        }

        private void RemoveLocalPModeFromCache(string fullPath)
        {
            if (_filePModeIdMap.TryGetValue(fullPath, out string pmodeId))
            {
                _pmodes.TryRemove(pmodeId, out _);
                _filePModeIdMap.TryRemove(fullPath, out _);
            }
        }

        private static bool IsFileLocked(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pmodes.Clear();
                _filePModeIdMap.Clear();
                _watcher?.Dispose();
            }
        }

    }
}