﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Factories;
using Eu.EDelivery.AS4.Model.Core;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Model.PMode;
using Eu.EDelivery.AS4.Serialization;
using Eu.EDelivery.AS4.Validators;
using FluentValidation.Results;
using log4net;

namespace Eu.EDelivery.AS4.Transformers
{
    /// <summary>
    /// <see cref="ITransformer" /> implementation that's responsible for transformation PMode models to Pull Messages
    /// instances.
    /// </summary>
    public class PModeToPullRequestTransformer : ITransformer
    {
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        /// <summary>
        /// Configures the <see cref="ITransformer"/> implementation with specific user-defined properties.
        /// </summary>
        /// <param name="properties">The properties.</param>
        public void Configure(IDictionary<string, string> properties) { }

        /// <summary>
        /// Transform a given <see cref="ReceivedMessage" /> to a Canonical <see cref="MessagingContext" /> instance.
        /// </summary>
        /// <param name="receivedMessage">Given message to transform.</param>
        /// <returns></returns>
        public Task<MessagingContext> TransformAsync(ReceivedMessage receivedMessage)
        {
            if (receivedMessage == null)
            {
                throw new ArgumentNullException(nameof(receivedMessage));
            }

            if (receivedMessage.UnderlyingStream == null)
            {
                throw new InvalidDataException(
                    $"Invalid incoming request stream received from {receivedMessage?.Origin}");
            }

            return CreatePullRequest(receivedMessage);
        }

        private static async Task<MessagingContext> CreatePullRequest(ReceivedMessage receivedMessage)
        {
            SendingProcessingMode pmode = await DeserializeValidPMode(receivedMessage);

            Logger.Info($"Prepare sending PullRequest with MPC=\"{Config.Encode(pmode?.MessagePackaging?.Mpc)}\"");
            AS4Message pullRequestMessage = AS4Message.Create(new PullRequest(IdentifierFactory.Instance.Create(), pmode?.MessagePackaging?.Mpc), pmode);

            return new MessagingContext(pullRequestMessage, MessagingContextMode.PullReceive) {SendingPMode = pmode};
        }

        private static async Task<SendingProcessingMode> DeserializeValidPMode(ReceivedMessage receivedMessage)
        {
            SendingProcessingMode pmode =
                await AS4XmlSerializer.FromStreamAsync<SendingProcessingMode>(receivedMessage.UnderlyingStream);
            
            ValidationResult result = SendingProcessingModeValidator.Instance.Validate(pmode);

            if (result.IsValid)
            {
                return pmode;
            }

            throw CreateInvalidPModeException(pmode, result);
        }

        private static InvalidDataException CreateInvalidPModeException(IPMode pmode, ValidationResult result)
        {
            var errorMessage = result.AppendValidationErrorsToErrorMessage($"Receiving PMode {pmode.Id} is not valid");

            Logger.Error(Config.Encode(errorMessage));
            
            return new InvalidDataException(errorMessage);
        }
    }
}