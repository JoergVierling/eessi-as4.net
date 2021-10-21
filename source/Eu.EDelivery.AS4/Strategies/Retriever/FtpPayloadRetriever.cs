﻿using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Common;
using log4net;

namespace Eu.EDelivery.AS4.Strategies.Retriever
{
    /// <summary>
    /// FTP Retriever Implementation to retrieve the FileStream of a FTP Server
    /// </summary>
    public class FtpPayloadRetriever : IPayloadRetriever
    {
        public const string Key = "ftp://";

        private readonly IConfig _config;
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        /// <summary>
        /// Initializes a new instance of the <see cref="FtpPayloadRetriever" /> class
        /// </summary>
        public FtpPayloadRetriever() : this(Config.Instance) { }

        public FtpPayloadRetriever(IConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            _config = config;
        }

        /// <summary>
        /// Retrieve <see cref="Stream" /> contents from a given <paramref name="location" />.
        /// </summary>
        /// <param name="location">The location.</param>
        /// <returns></returns>
        public Task<Stream> RetrievePayloadAsync(string location)
        {
            if (location == null)
            {
                throw new ArgumentNullException(nameof(location));
            }

            return Task.FromResult(TryGetFtpFile(location));
        }

        private Stream TryGetFtpFile(string location)
        {
            FtpWebRequest ftpRequest = CreateFtpRequest(location);
            return Task.Run(() => GetFtpFile(ftpRequest)).Result;
        }

        private FtpWebRequest CreateFtpRequest(string location)
        {
            var ftpRequest = (FtpWebRequest)WebRequest.Create(new Uri(location));
            ftpRequest.Method = WebRequestMethods.Ftp.DownloadFile;

            ftpRequest.Credentials = new NetworkCredential(_config.GetSetting("ftpusername"), _config.GetSetting("ftppassword"));

            return ftpRequest;
        }

        private static async Task<Stream> GetFtpFile(FtpWebRequest ftpRequest)
        {
            using (WebResponse ftpResponse = await ftpRequest.GetResponseAsync().ConfigureAwait(false))
            {
                return ftpResponse.GetResponseStream();
            }
        }
    }
}