using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Serialization;
using Eu.EDelivery.AS4.Streaming;
using Eu.EDelivery.AS4.Common;
using log4net;

namespace Eu.EDelivery.AS4.Steps.Send.Response
{
    /// <summary>
    /// 
    /// </summary>
    internal class AS4Response : IAS4Response
    {
        private readonly HttpWebResponse _httpWebResponse;
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        /// <summary>
        /// Initializes a new instance of the <see cref="AS4Response" /> class.
        /// </summary>
        /// <param name="requestMessage">The resulted Message.</param>
        /// <param name="webResponse">The web Response.</param>
        private AS4Response(MessagingContext requestMessage, HttpWebResponse webResponse)
        {
            _httpWebResponse = webResponse;
            OriginalRequest = requestMessage;
        }

        /// <summary>
        /// Gets the HTTP Status Code of the HTTP response.
        /// </summary>
        public HttpStatusCode StatusCode => _httpWebResponse?.StatusCode ?? HttpStatusCode.InternalServerError;

        /// <summary>
        /// Gets the Message from the AS4 response.
        /// </summary>
        public AS4Message ReceivedAS4Message { get; private set; }

        public ReceivedMessage ReceivedStream { get; private set; }

        /// <summary>
        /// Gets the Original Request from this response.
        /// </summary>
        public MessagingContext OriginalRequest { get; }

        /// <summary>
        /// Create a new <see cref="AS4Response"/> instance.
        /// </summary>
        /// <param name="requestMessage"></param>
        /// <param name="webResponse"></param>
        /// <returns></returns>
        public static async Task<AS4Response> Create(MessagingContext requestMessage, HttpWebResponse webResponse)
        {
            var response = new AS4Response(requestMessage, webResponse);
            var responseStream = webResponse.GetResponseStream() ?? Stream.Null;
            var contentStream = VirtualStream.Create(webResponse.ContentLength, forAsync: true);

            await responseStream.CopyToFastAsync(contentStream);
            contentStream.Position = 0;

            response.ReceivedStream = 
                new ReceivedMessage(
                    contentStream, 
                    webResponse.ContentType, 
                    webResponse.ResponseUri?.AbsolutePath ?? "unknown",
                    webResponse.ContentLength);

            response.ReceivedAS4Message = await TryDeserializeReceivedStream(response.ReceivedStream, CancellationToken.None);

            if (Logger.IsInfoEnabled)
            {
                if (response.ReceivedAS4Message.IsEmpty == false)
                {
                    LogReceivedAS4Response(
                        requestMessage.AS4Message,
                        response.ReceivedAS4Message);
                }
            }

            return response;
        }

        private static void LogReceivedAS4Response(AS4Message request, AS4Message response)
        {
            if (request?.PrimaryMessageUnit != null && response.PrimaryMessageUnit != null)
            {
                Logger.Info($"Sending AS4Message {Config.Encode(request.GetPrimaryMessageId())} results in: {Config.Encode(request.PrimaryMessageUnit.GetType().Name)} -> {Config.Encode(response.PrimaryMessageUnit.GetType().Name)} ");
            }

            foreach (MessageUnit mu in response.MessageUnits)
            {
                switch (mu)
                {
                    case Error err:
                        Logger.Error($"Error message {Config.Encode(err.FormatErrorLines())} response received for message with with ebMS Id {Config.Encode(mu.RefToMessageId)}");
                        break;
                    case Receipt r:
                        Logger.Debug($"{Config.Encode((r.NonRepudiationInformation != null ? "Non-Repudiation " : String.Empty))}Receipt message response received for message with ebMS Id {Config.Encode(mu.RefToMessageId)}");
                        break;
                }
            }
        }

        private static async Task<AS4Message> TryDeserializeReceivedStream(ReceivedMessage receivedStream, CancellationToken cancellation)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(receivedStream.ContentType))
                {
                    if (Logger.IsDebugEnabled)
                    {
                        Logger.Debug("No ContentType set - returning an empty AS4 response.");

                        // Not in 'using' because it closes the underlying stream
                        var streamReader = new StreamReader(receivedStream.UnderlyingStream);
                        string responseContent = await streamReader.ReadToEndAsync();
                        if (!string.IsNullOrEmpty(responseContent))
                        {
                            Logger.Debug(Config.Encode(responseContent));
                        }
                    }

                    return AS4Message.Empty;
                }

                var serializer = SerializerProvider.Default.Get(receivedStream.ContentType);

                return await serializer.DeserializeAsync(receivedStream.UnderlyingStream, receivedStream.ContentType, cancellation);
            }
            catch (Exception exception)
            {
                Logger.Error(Config.Encode(exception.Message));
                return AS4Message.Empty;
            }
            finally
            {
                receivedStream.UnderlyingStream.Position = 0;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            _httpWebResponse?.Dispose();
        }
    }

    /// <summary>
    /// Contract to define the HTTP/AS4 response being handled.
    /// </summary>
    public interface IAS4Response : IDisposable
    {
        /// <summary>
        /// Gets the HTTP Status Code of the HTTP response.
        /// </summary>
        HttpStatusCode StatusCode { get; }

        /// <summary>
        /// Gets the an AS4Message representation of the response.
        /// </summary>
        AS4Message ReceivedAS4Message { get; }

        /// <summary>
        /// Gets a Stream that contains the response like it has been received.
        /// </summary>
        ReceivedMessage ReceivedStream { get; }

        /// <summary>
        /// Gets the Original Request from this response.
        /// </summary>
        MessagingContext OriginalRequest { get; }
    }
}