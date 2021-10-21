﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Eu.EDelivery.AS4.Model.Internal;
using log4net;
using Timer = System.Timers.Timer;

namespace Eu.EDelivery.AS4.Receivers
{
    /// <summary>
    /// Receive in a exponential interval <see cref="IntervalRequest"/> instances.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [NotConfigurable]
    public abstract class ExponentialIntervalReceiver<T> : IReceiver where T : IntervalRequest
    {
        private readonly IDictionary<DateTime, List<T>> _runSchedule;
        private readonly List<T> _intervalRequests;
        private readonly Timer _timer;

        private static readonly ILog Logger = LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );

        protected enum Interval
        {
            Reset,
            Increase
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExponentialIntervalReceiver{T}"/> class.
        /// </summary>
        protected ExponentialIntervalReceiver()
        {
            _runSchedule = new Dictionary<DateTime, List<T>>();
            _intervalRequests = new List<T>();
            _timer = new Timer {Enabled = false};
            _timer.Elapsed += TimerElapsed;
        }

        private void TimerElapsed(object sender, ElapsedEventArgs eventArgs)
        {
            _timer.Stop();

            List<T> intervalRequests = SelectAllRequestsForThisEvent(eventArgs);
           Logger.Debug($"{intervalRequests.Count} request(s) will send on '{eventArgs.SignalTime}'");
            RemoveAllSelectedRequestsForThisEvent(eventArgs);

            WaitForAllRequests(intervalRequests);
            DetermineNextRuns(intervalRequests);
        }

        private List<T> SelectAllRequestsForThisEvent(ElapsedEventArgs eventArgs)
        {
            return _runSchedule.Where(s => s.Key <= eventArgs.SignalTime).SelectMany(p => p.Value).ToList();
        }

        private void RemoveAllSelectedRequestsForThisEvent(ElapsedEventArgs eventArgs)
        {
            List<DateTime> keys = _runSchedule.Keys.Where(k => k <= eventArgs.SignalTime).ToList();
            keys.ForEach(k => _runSchedule.Remove(k));
        }

        private void WaitForAllRequests(IEnumerable<T> intervalRequests)
        {
            var tasks = new List<Task>();

            foreach (T intervalRequest in intervalRequests)
            {
                intervalRequest.CalculateNewInterval();

                tasks.Add(Task.Run(() => OnIntervalRequestReceived(intervalRequest)));
            }

            TryWaitAll(tasks);
        }

        private static void TryWaitAll(List<Task> tasks)
        {
            try
            {
                Task.WaitAll(tasks.ToArray());
            }
            catch (AggregateException exception)
            {
                exception.Handle(e => e is TaskCanceledException);
            }
        }

        private async Task OnIntervalRequestReceived(T intervalRequest)
        {
            if (await OnRequestReceived(intervalRequest) == Interval.Reset)
            {
                intervalRequest.ResetInterval();
            }
        }

        private void DetermineNextRuns(IEnumerable<T> intervalRequests)
        {
            DateTime currentTime = DateTime.Now;

            foreach (T intervalRequest in intervalRequests)
            {
                AddIntervalRequest(currentTime + intervalRequest.CurrentInterval, intervalRequest);
            }

            if (_runSchedule.Any())
            {
                _timer.Interval = CalculateNextTriggerInterval(currentTime);
                _timer.Start();
            }
        }

        private void AddIntervalRequest(DateTime dateTime, T intervalRequest)
        {
            DateTime timeTrimmedOnSeconds = TrimOnSeconds(dateTime);

            if (_runSchedule.ContainsKey(timeTrimmedOnSeconds) == false)
            {
                _runSchedule.Add(timeTrimmedOnSeconds, new List<T>());
            }

            _runSchedule[timeTrimmedOnSeconds].Add(intervalRequest);
        }

        private static DateTime TrimOnSeconds(DateTime dateTime)
        {
            return new DateTime(
                dateTime.Year,
                dateTime.Month,
                dateTime.Day,
                dateTime.Hour,
                dateTime.Minute,
                dateTime.Second);
        }

        private double CalculateNextTriggerInterval(DateTime now)
        {
            DateTime firstRunDate = _runSchedule.Min(t => t.Key);
            TimeSpan triggerTime = firstRunDate - now;

            return triggerTime.TotalMilliseconds <= 0 ? 1 : triggerTime.TotalMilliseconds;
        }

        /// <summary>
        /// Add a interval request to the schedule.
        /// </summary>
        /// <param name="intervalRequest"></param>
        protected void AddIntervalRequest(T intervalRequest)
        {
            _intervalRequests.Add(intervalRequest);
        }

        /// <summary>
        /// Start the <see cref="ExponentialIntervalReceiver{T}"/> with pulling.
        /// </summary>
        protected void StartInterval()
        {
            _timer.Enabled = true;
            DetermineNextRuns(_intervalRequests);
        }

        /// <summary>
        /// Configure the receiver with a given settings dictionary.
        /// </summary>
        /// <param name="settings"></param>
        public abstract void Configure(IEnumerable<Setting> settings);

        /// <summary>
        /// <paramref name="intervalRequest"/> is received.
        /// </summary>
        /// <param name="intervalRequest"></param>
        /// <returns></returns>
        protected abstract Task<Interval> OnRequestReceived(T intervalRequest);

        /// <summary>
        /// Start receiving on a configured Target
        /// Received messages will be send to the given Callback
        /// </summary>
        /// <param name="messageCallback"></param>
        /// <param name="cancellationToken"></param>
        public abstract void StartReceiving(
            Func<ReceivedMessage, CancellationToken, Task<MessagingContext>> messageCallback,
            CancellationToken cancellationToken);

        /// <summary>
        /// Stop the receiver from pulling.
        /// </summary>
        public void StopReceiving()
        {
            _timer.Enabled = false;
            _timer.Dispose();
        }
    }
}