using System;
using System.Diagnostics;
using System.Threading;
using DotNetty.Transport.Channels;

namespace Nexum.Core
{
    internal sealed class EventLoopScheduler
    {
        private readonly Action _callbackSimple;
        private readonly Action<double> _callbackWithElapsed;
        private readonly TimeSpan _interval;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private IEventLoop _eventLoop;
        private volatile int _isRunning;
        private double _lastTickTime;

        internal EventLoopScheduler(TimeSpan interval, Action<double> callback)
        {
            _interval = interval;
            _callbackWithElapsed = callback ?? throw new ArgumentNullException(nameof(callback));
            _callbackSimple = null;
        }

        internal EventLoopScheduler(TimeSpan interval, Action callback)
        {
            _interval = interval;
            _callbackSimple = callback ?? throw new ArgumentNullException(nameof(callback));
            _callbackWithElapsed = null;
        }

        internal bool IsRunning => _isRunning == 1;

        internal static EventLoopScheduler StartIfNeeded(
            EventLoopScheduler existing,
            TimeSpan interval,
            Action callback,
            IEventLoop eventLoop)
        {
            if (existing != null && existing.IsRunning)
                return existing;

            if (eventLoop == null)
                return existing;

            var scheduler = new EventLoopScheduler(interval, callback);
            scheduler.Start(eventLoop);
            return scheduler;
        }

        internal static EventLoopScheduler StartIfNeeded(
            EventLoopScheduler existing,
            TimeSpan interval,
            Action<double> callback,
            IEventLoop eventLoop)
        {
            if (existing != null && existing.IsRunning)
                return existing;

            if (eventLoop == null)
                return existing;

            var scheduler = new EventLoopScheduler(interval, callback);
            scheduler.Start(eventLoop);
            return scheduler;
        }

        internal void Start(IEventLoop eventLoop)
        {
            if (eventLoop == null)
                throw new ArgumentNullException(nameof(eventLoop));

            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
                return;

            _eventLoop = eventLoop;
            _stopwatch.Restart();
            _lastTickTime = 0;

            ScheduleNextTick();
        }

        internal void Stop()
        {
            Interlocked.Exchange(ref _isRunning, 0);
            _eventLoop = null;
        }

        private void ScheduleNextTick()
        {
            var eventLoop = _eventLoop;
            if (eventLoop == null || _isRunning != 1)
                return;

            eventLoop.Schedule(ExecuteTick, this, _interval);
        }

        private static void ExecuteTick(object state)
        {
            var scheduler = (EventLoopScheduler)state;
            scheduler.DoTick();
        }

        private void DoTick()
        {
            if (_isRunning != 1)
                return;

            double currentTime = _stopwatch.Elapsed.TotalSeconds;
            double elapsedTime = currentTime - _lastTickTime;
            _lastTickTime = currentTime;

            try
            {
                if (_callbackWithElapsed != null)
                    _callbackWithElapsed(elapsedTime);
                else
                    _callbackSimple?.Invoke();
            }
            catch
            {
            }

            ScheduleNextTick();
        }
    }
}
