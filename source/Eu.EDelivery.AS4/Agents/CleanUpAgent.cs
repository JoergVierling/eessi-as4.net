﻿using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Common;
using Eu.EDelivery.AS4.Entities;
using Eu.EDelivery.AS4.Model.Internal;
using Eu.EDelivery.AS4.Strategies.Database;
using log4net;
using Eu.EDelivery.AS4.Extensions;

namespace Eu.EDelivery.AS4.Agents
{
    /// <summary>
    /// <see cref="IAgent"/> implementation that runs a Clean Up job every day.
    /// This job consists of deleting messages that are inserted older that the given retention period (local configuration settings specifies this in days).
    /// </summary>
    /// <seealso cref="IAgent" />
    internal class CleanUpAgent : IAgent
    {
        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        private readonly Func<DatastoreContext> _storeExpression;
        private readonly TimeSpan _retentionPeriod;

        /// <summary>
        /// Initializes a new instance of the <see cref="CleanUpAgent" /> class.
        /// </summary>
        /// <param name="storeExpression">The store expression.</param>
        /// <param name="retentionPeriod">The retention period.</param>
        public CleanUpAgent(Func<DatastoreContext> storeExpression, TimeSpan retentionPeriod)
        {
            if (storeExpression == null)
            {
                throw new ArgumentNullException(nameof(storeExpression));
            }

            _storeExpression = storeExpression;
            _retentionPeriod = retentionPeriod;
        }

        /// <summary>
        /// Gets the agent configuration.
        /// </summary>
        /// <value>The agent configuration.</value>
        public AgentConfig AgentConfig { get; } = new AgentConfig("Clean Up Agent");

        /// <summary>
        /// Starts the specified agent.
        /// </summary>
        /// <param name="cancellation">The cancellation.</param>
        /// <returns></returns>
        public async Task Start(CancellationToken cancellation)
        {
            Logger.Trace($"{AgentConfig.Name} Started");
            Logger.Debug($"Will clean up entries older than: \"{DateTimeOffset.Now.Subtract(_retentionPeriod)}\"");

            try
            {
                await Observable.Interval(TimeSpan.FromDays(1), TaskPoolScheduler.Default)
                    .StartWith(0)
                    .Do(_ => StartCleaningMessagesTables())
                    .ToTask(cancellation);
            }
            catch (TaskCanceledException)
            {
                Logger.Trace($"{AgentConfig.Name} Stopped!");
            }
        }

        private void StartCleaningMessagesTables()
        {
            using (DatastoreContext context = _storeExpression())
            {
                var allowedOperations = new[]
                {
                    Operation.Delivered,
                    Operation.Forwarded,
                    Operation.Notified,
                    Operation.Sent,
                    Operation.NotApplicable,
                    Operation.Undetermined
                };

                foreach (string table in DatastoreTable.DomainEntityTables)
                {
                    context.NativeCommands
                           .BatchDeleteOverRetentionPeriod(table, _retentionPeriod, allowedOperations);
                }
            }
        }

        /// <summary>
        /// Stops this agent.
        /// </summary>
        public void Stop() { }

        public Task<MessagingContext> Process(MessagingContext message, CancellationToken cancellation)
        {
            throw new NotImplementedException();
        }
    }
}
