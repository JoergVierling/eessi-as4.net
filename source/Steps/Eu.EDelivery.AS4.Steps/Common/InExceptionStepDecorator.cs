﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Builders.Entities;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Entities;
using Eu.EDelivery.AS4.Exceptions;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Repositories;
using NLog;

namespace Eu.EDelivery.AS4.Steps.Common
{
    /// <summary>
    /// Exception Handling Step: acts as Decorator for the <see cref="CompositeStep"/>
    /// Responsibility: describes what to do in case an exception occurs within a AS4 Notify operation
    /// </summary>
    public class InExceptionStepDecorator : IStep
    {
        private readonly IStep _step;

        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private InternalMessage _internalMessage;

        /// <summary>
        /// Initializes a new instance of the <see cref="InExceptionStepDecorator"/> class
        /// with a a decorated <paramref name="step"/>
        /// </summary>
        /// <param name="step"></param>
        public InExceptionStepDecorator(IStep step)
        {
            this._step = step;
        }

        /// <summary>
        /// Start executing Step
        /// so it can be catched
        /// </summary>
        /// <param name="internalMessage"></param>        
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<StepResult> ExecuteAsync(InternalMessage internalMessage, CancellationToken cancellationToken)
        {
            try
            {
                return await this._step.ExecuteAsync(internalMessage, cancellationToken);
            }
            catch (AS4Exception exception)
            {
                using (var context = Registry.Instance.CreateDatastoreContext())
                {
                    var repository = new DatastoreRepository(context);
                    this._internalMessage = internalMessage;
                    await HandleInExceptionAsync(exception, repository);
                }

                return StepResult.Failed(exception);
            }
        }

        private async Task HandleInExceptionAsync(AS4Exception exception, IDatastoreRepository repository)
        {
            this._logger.Info($"{this._internalMessage.Prefix} Handling AS4 Exception...");
            foreach (string messageId in exception.MessageIds)
                await TryHandleInExceptionAsync(exception, messageId, repository);
        }

        private async Task TryHandleInExceptionAsync(AS4Exception exception, string messageId, IDatastoreRepository repository)
        {
            try
            {
                InException inException = CreateInException(exception, messageId);

                await repository.InsertInExceptionAsync(inException);
                await UpdateInMessageAsync(messageId, exception.ExceptionType, repository);
            }
            catch (Exception)
            {
                this._logger.Error($"{this._internalMessage.Prefix} Cannot Update Datastore with OutException");
            }
        }

        private static InException CreateInException(AS4Exception exception, string messageId)
        {
            return new InExceptionBuilder()
                .WithAS4Exception(exception)
                .WithEbmsMessageId(messageId)
                .Build();
        }

        private static async Task UpdateInMessageAsync(string messageId, ExceptionType exceptionType, IDatastoreRepository repository)
        {
            await repository.UpdateInMessageAsync(messageId,
                message =>
                {
                    message.Status = InStatus.Exception;
                    message.ExceptionType = exceptionType;
                });
        }
    }
}