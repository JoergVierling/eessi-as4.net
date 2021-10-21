﻿using System;
using System.IO;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Streaming;
using log4net;
using Eu.EDelivery.AS4.Extensions;

namespace Eu.EDelivery.AS4.Strategies.Retriever
{
    /// <summary>
    /// Temporary <see cref="IPayloadRetriever"/> implementation that removes the file after retrieving.
    /// </summary>
    /// <seealso cref="IPayloadRetriever" />
    public class TempFilePayloadRetriever : IPayloadRetriever
    {
        public const string Key = "temp:///";

        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        /// <summary>
        /// Retrieve <see cref="Stream"/> contents from a given <paramref name="location"/>.
        /// </summary>
        /// <param name="location">The location.</param>
        /// <returns></returns>
        public async Task<Stream> RetrievePayloadAsync(string location)
        {
            if (location == null)
            {
                throw new ArgumentNullException(nameof(location));
            }

            string absolutePath = location.Replace(Key, string.Empty);

            Stream targetStr = await RetrieveTempFileContents(absolutePath);
            DeleteTempFile(absolutePath);

            return targetStr;
        }

        private static async Task<Stream> RetrieveTempFileContents(string absolutePath)
        {
            var virtualStr = VirtualStream.Create();

            using (var fileStr = new FileStream(
                absolutePath, 
                FileMode.Open, 
                FileAccess.Read, 
                FileShare.Read))
            {
                await fileStr.CopyToFastAsync(virtualStr);
            }

            virtualStr.Position = 0;
            return virtualStr;
        }

        private static void DeleteTempFile(string absolutePath)
        {
            try
            {
                Logger.Trace($"Removing temporary file at location: {absolutePath}");
                File.Delete(absolutePath);
                Logger.Trace($"Temporary file {absolutePath} removed.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }
    }
}
