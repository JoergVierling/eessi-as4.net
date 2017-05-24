﻿using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Exceptions;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Model.PMode;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Security.Strategies;
using Eu.EDelivery.AS4.Serialization;
using Eu.EDelivery.AS4.Steps;
using Eu.EDelivery.AS4.Steps.Receive;
using Moq;
using Xunit;

namespace Eu.EDelivery.AS4.UnitTests.Steps.Receive
{
    /// <summary>
    /// Testing <see cref="DecryptAS4MessageStep" />
    /// </summary>
    public class GivenDecryptAS4MessageStepFacts
    {
        private readonly Mock<IEncryptionStrategy> _mockedEncryptedStrategy;
        private readonly IStep _step;

        public GivenDecryptAS4MessageStepFacts()
        {
            _mockedEncryptedStrategy = new Mock<IEncryptionStrategy>();

            var mockedRespository = new Mock<ICertificateRepository>();
            mockedRespository.Setup(r => r.GetCertificate(It.IsAny<X509FindType>(), It.IsAny<string>()))
                             .Returns(new X509Certificate2(Properties.Resources.holodeck_partyc_certificate, "ExampleC", X509KeyStorageFlags.Exportable));

            _step = new DecryptAS4MessageStep(mockedRespository.Object);
        }

        public class GivenValidArguments : GivenDecryptAS4MessageStepFacts
        {
            [Fact]
            public async Task ThenExecuteStepSucceedsAsync()
            {
                // Arrange
                AS4Message as4Message = await GetEncryptedAS4MessageAsync();
                var internalMessage = new InternalMessage(as4Message) {ReceivingPMode = new ReceivingProcessingMode()};
                internalMessage.ReceivingPMode.Security.Decryption.Encryption = Limit.Allowed;

                // Act
                StepResult stepResult = await _step.ExecuteAsync(internalMessage, CancellationToken.None);

                // Assert
                Assert.True(stepResult.InternalMessage.AS4Message.IsEncrypted);
            }
        }

        public class GivenInvalidArguments : GivenDecryptAS4MessageStepFacts
        {
            [Fact]
            public async Task ThenExecuteStepFailsWithNotAllowedEncryptionAsync()
            {
                // Arrange
                var as4Message = new AS4Message
                {
                    SecurityHeader = new SecurityHeader(null, _mockedEncryptedStrategy.Object)
                };
                var internalMessage = new InternalMessage(as4Message) {ReceivingPMode = new ReceivingProcessingMode()};
                internalMessage.ReceivingPMode.Security.Decryption.Encryption = Limit.NotAllowed;

                // Act / Assert
                AS4Exception as4Exception =
                    await Assert.ThrowsAsync<AS4Exception>(
                        () => _step.ExecuteAsync(internalMessage, CancellationToken.None));

                Assert.Equal(ErrorCode.Ebms0103, as4Exception.ErrorCode);
            }

            [Fact]
            public async Task ThenExecuteStepFailsWithRequiredEncryptionAsync()
            {
                // Arrange
                var as4Message = new AS4Message();
                var internalMessage = new InternalMessage(as4Message) {ReceivingPMode = new ReceivingProcessingMode()};
                internalMessage.ReceivingPMode.Security.Decryption.Encryption = Limit.Required;

                // Act
                AS4Exception as4Exception =
                    await Assert.ThrowsAsync<AS4Exception>(
                        () => _step.ExecuteAsync(internalMessage, CancellationToken.None));

                Assert.Equal(ErrorCode.Ebms0103, as4Exception.ErrorCode);
            }
        }

        protected Task<AS4Message> GetEncryptedAS4MessageAsync()
        {
            Stream inputStream = new MemoryStream(Properties.Resources.as4_encrypted_message);
            var serializer = new MimeMessageSerializer(new SoapEnvelopeSerializer());

            return serializer.DeserializeAsync(
                inputStream,
                "multipart/related; boundary=\"MIMEBoundary_64ed729f813b10a65dfdc363e469e2206ff40c4aa5f4bd11\"",
                CancellationToken.None);
        }
    }
}