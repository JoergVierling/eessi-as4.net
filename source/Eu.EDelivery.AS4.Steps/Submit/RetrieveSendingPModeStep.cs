﻿using System;
using System.ComponentModel;
using System.Configuration;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Extensions;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Model.PMode;
using Eu.EDelivery.AS4.Validators;
using log4net;

namespace Eu.EDelivery.AS4.Steps.Submit
{
    /// <summary>
    /// Add the retrieved PMode to the <see cref="Model.Submit.SubmitMessage" />
    /// after the PMode is verified
    /// </summary>
    [Info("Retrieve SendingPMode")]
    [Description("Retrieve the SendingPMode that must be used to send the AS4Message")]
    public class RetrieveSendingPModeStep : IStep
    {
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );
        private readonly IConfig _config;

        /// <summary>
        /// Initializes a new instance of the <see cref="RetrieveSendingPModeStep" /> class
        /// </summary>
        public RetrieveSendingPModeStep() : this(Config.Instance) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RetrieveSendingPModeStep" /> class
        /// Create a new Retrieve PMode Step with a given <see cref="IConfig" />
        /// </summary>
        /// <param name="config">
        /// </param>
        public RetrieveSendingPModeStep(IConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            _config = config;
        }

        /// <summary>
        /// Retrieve the PMode that must be used to send the SubmitMessage that is in the current Messagingcontext />
        /// </summary>
        /// <param name="messagingContext"></param>
        /// <returns></returns>
        public async Task<StepResult> ExecuteAsync(MessagingContext messagingContext)
        {
            if (messagingContext == null)
            {
                throw new ArgumentNullException(nameof(messagingContext));
            }

            if (messagingContext.SubmitMessage == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RetrieveSendingPModeStep)} requires an SubmitMessage to retrieve the SendingPMode from but no SubmitMessage is present in the MessagingContext");
            }

            messagingContext.SubmitMessage.PMode = RetrieveSendPMode(messagingContext);
            messagingContext.SendingPMode = messagingContext.SubmitMessage.PMode;

            return await StepResult.SuccessAsync(messagingContext);
        }

        private SendingProcessingMode RetrieveSendPMode(MessagingContext message)
        {
            SendingProcessingMode pmode = RetrievePMode(message);
            ValidatePMode(pmode);

            return pmode;
        }
        private SendingProcessingMode RetrievePMode(MessagingContext context)
        {
            string processingModeId = RetrieveProcessingModeId(context.SubmitMessage.Collaboration);

            SendingProcessingMode pmode = _config.GetSendingPMode(processingModeId);

            Logger.Info($"{Config.Encode(context.LogTag)} SendingPMode \"{Config.Encode(pmode.Id)}\" was successfully retrieved for SubmitMessage");

            return pmode;
        }

        private string RetrieveProcessingModeId(Model.Common.CollaborationInfo collaborationInfo)
        {
            if (collaborationInfo == null)
            {
                Logger.Error(
                    "SubmitMessage is incomplete to retrieve the SendingPMode because the Collaboration.AgreementRef element is missing");
                
                throw new InvalidOperationException(
                    "SubmitMessage is incomplete to retrieve the SendingPMode because the Collaboration.AgreementRef element is missing");
            }

            return collaborationInfo.AgreementRef?.PModeId;
        }

        private static void ValidatePMode(SendingProcessingMode pmode)
        {
            SendingProcessingModeValidator.Instance.Validate(pmode).Result(
                onValidationSuccess: result => Logger.Trace($"SendingPMode {Config.Encode(pmode.Id)} is valid for Submit Message"),
                onValidationFailed: result =>
                {
                    string description = 
                        result.AppendValidationErrorsToErrorMessage(
                            $"SendingPMode {pmode.Id} was invalid and cannot be used to assign to the SubmitMessage: ");

                    Logger.Error(Config.Encode(description));

                    throw new ConfigurationErrorsException(description);
                });
        }
    }
}