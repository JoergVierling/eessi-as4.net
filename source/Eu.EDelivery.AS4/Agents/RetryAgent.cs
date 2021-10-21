﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Extensions;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Entities;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Model.PMode;
using Eu.EDelivery.AS4.Receivers;
using Eu.EDelivery.AS4.Repositories;
using Eu.EDelivery.AS4.Serialization;
using Eu.EDelivery.AS4.Services;
using log4net;
using MessageExchangePattern = Eu.EDelivery.AS4.Entities.MessageExchangePattern;
using NotSupportedException = System.NotSupportedException;
using RetryReliability = Eu.EDelivery.AS4.Entities.RetryReliability;

namespace Eu.EDelivery.AS4.Agents
{
    internal class RetryAgent : IAgent
    {
        private readonly IReceiver _receiver;
        private readonly Func<DatastoreContext> _createContext;

        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryAgent"/> class.
        /// </summary>
        /// <param name="receiver">The receiver used to retrieve <see cref="RetryReliability"/> entities</param>
        /// <param name="createContext">The factory creating a <see cref="DatastoreContext"/></param>
        internal RetryAgent(
            IReceiver receiver, 
            Func<DatastoreContext> createContext)
        {
            if (receiver == null)
            {
                throw new ArgumentNullException(nameof(receiver));
            }

            if (createContext == null)
            {
                throw new ArgumentNullException(nameof(createContext));
            }

            _receiver = receiver;
            _createContext = createContext;
        }

        /// <summary>
        /// Gets the agent configuration.
        /// </summary>
        /// <value>The agent configuration.</value>
        public AgentConfig AgentConfig { get; } = new AgentConfig("Retry Agent");

        /// <summary>
        /// Starts the specified agent.
        /// </summary>
        /// <param name="cancellation">The cancellation.</param>
        /// <returns></returns>
        public async Task Start(CancellationToken cancellation)
        {
            Logger.Trace(AgentConfig.Name + " Started");

            await Task.Factory.StartNew(
                () => _receiver.StartReceiving(OnReceivedAsync, cancellation),
                TaskCreationOptions.LongRunning);
        }

        private Task<MessagingContext> OnReceivedAsync(ReceivedMessage rm, CancellationToken ct)
        {
            try
            {
                if (rm is ReceivedEntityMessage rem && rem.Entity is RetryReliability rr)
                {
                    using (DatastoreContext ctx = _createContext())
                    {
                        var repo = new DatastoreRepository(ctx);
                        OnReceivedEntity(rr, repo);
                        ctx.SaveChanges();
                    }
                }
                else
                {
                    throw new NotSupportedException(
                        $"Only {nameof(ReceivedEntityMessage)} implementations are allowed");
                }
            }
            catch (Exception ex)
            {
                // TODO: must the agent be stopped?
                Logger.Error(ex);
            }

            return Task.FromResult(
                new MessagingContext(rm, MessagingContextMode.Unknown));
        }

        private static void OnReceivedEntity(RetryReliability rr, DatastoreRepository repo)
        {
            (long refToEntityId, Entity entityType) = GetRefToEntityIdWithType(rr);
            Operation op = GetRefEntityOperation(repo, refToEntityId, entityType);

            if (op == Operation.ToBeRetried && rr.CurrentRetryCount < rr.MaxRetryCount)
            {
                var t = rr.RetryType;
                Operation updateOperation =
                    t == RetryType.Delivery ? Operation.ToBeDelivered :
                    t == RetryType.Notification ? Operation.ToBeNotified :
                    t == RetryType.Send ? Operation.ToBeSent : 
                    t == RetryType.PiggyBack ? Operation.ToBePiggyBacked : throw new InvalidOperationException($"Unknown RetryType: {t}");

                Logger.Debug($"({Config.Encode(rr.RetryType)}) Update {Config.Encode(entityType)} to retry again"
                    + $"{Environment.NewLine} -> Set messages's Operation={updateOperation}"
                    + $"{Environment.NewLine} -> Update retry info {{CurrentRetry={rr.CurrentRetryCount + 1}, Status=Pending, LastRetryTime=Now}}");

                UpdateRefEntityOperation(repo, refToEntityId, entityType, updateOperation);

                repo.UpdateRetryReliability(rr.Id, r =>
                {
                    r.CurrentRetryCount = r.CurrentRetryCount + 1;
                    r.LastRetryTime = DateTimeOffset.Now;
                });

            }
            else if (rr.CurrentRetryCount >= rr.MaxRetryCount)
            {
                Logger.Debug(
                    $"({Config.Encode(rr.RetryType)}) Retry operation is completed, no new retries will happen"
                    + $"{Environment.NewLine} -> Update {entityType}'s Operation=DeadLettered"
                    + $"{Environment.NewLine} -> Update retry cycle {{Status=Completed}}");

                UpdateRefEntityOperation(repo, refToEntityId, entityType, Operation.DeadLettered);
                repo.UpdateRetryReliability(rr.Id, r => r.Status = RetryStatus.Completed);

                if (rr.RetryType == RetryType.Send)
                {
                    InsertDeadLetteredError(refToEntityId, repo);
                }
            }
        }

        private static (long, Entity) GetRefToEntityIdWithType(RetryReliability r)
        {
            if (r.RefToInMessageId.HasValue)
            {
                return (r.RefToInMessageId.Value, Entity.InMessage);
            }

            if (r.RefToOutMessageId.HasValue)
            {
                return (r.RefToOutMessageId.Value, Entity.OutMessage);
            }

            if (r.RefToInExceptionId.HasValue)
            {
                return (r.RefToInExceptionId.Value, Entity.InException);
            }

            if (r.RefToOutExceptionId.HasValue)
            {
                return (r.RefToOutExceptionId.Value, Entity.OutException);
            }

            throw new InvalidOperationException(
                "Invalid 'RetryReliability' record: requries a reference to In/Out Messages/Exceptions");
        }

        private enum Entity { InMessage, OutMessage, InException, OutException }

        private static Operation GetRefEntityOperation(DatastoreRepository repo, long id, Entity type)
        {
            switch (type)
            {
                case Entity.InMessage:
                    return repo.GetInMessageData(id, m => m.Operation);
                case Entity.OutMessage:
                    return repo.GetOutMessageData(id, m => m.Operation);
                case Entity.InException:
                    return repo.GetInExceptionData(id, ex => ex.Operation);
                case Entity.OutException:
                    return repo.GetOutExceptionData(id, ex => ex.Operation);
                default:
                    throw new ArgumentOutOfRangeException(paramName: nameof(type), actualValue: type, message: null);
            }
        }

        private static void UpdateRefEntityOperation(DatastoreRepository repo, long id, Entity type, Operation o)
        {
            switch (type)
            {
                case Entity.InMessage:
                    repo.UpdateInMessage(id, m => m.Operation = o);
                    break;
                case Entity.OutMessage:
                    repo.UpdateOutMessage(id, m => m.Operation = o);
                    break;
                case Entity.InException:
                    repo.UpdateInException(id, ex => ex.Operation = o);
                    break;
                case Entity.OutException:
                    repo.UpdateOutException(id, ex => ex.Operation = o);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(paramName: nameof(type), actualValue: type, message: null);
            }
        }

        private static void InsertDeadLetteredError(long outMessageId, IDatastoreRepository repo)
        {
            Tuple<string, MessageExchangePattern, string> data =
                repo.GetOutMessageData(
                    outMessageId,
                    m => Tuple.Create(m.EbmsMessageId, m.MEP, m.PMode));

            string ebmsMessageId = data.Item1;
            MessageExchangePattern mep = data.Item2;
            var sendPMode = AS4XmlSerializer.FromString<SendingProcessingMode>(data.Item3);

            var service = new InMessageService(Config.Instance, repo);
            service.InsertDeadLetteredErrorForAsync(ebmsMessageId, mep, sendPMode);
        }

        /// <summary>
        /// Stops this agent.
        /// </summary>
        public void Stop()
        {
            Logger.Trace($"Stopping {AgentConfig.Name} ...");
            _receiver.StopReceiving();
            Logger.Trace($"Stopping {AgentConfig.Name} ...");
        }

        public Task<MessagingContext> Process(MessagingContext message, CancellationToken cancellation)
        {
            throw new NotImplementedException();
        }
    }
}
