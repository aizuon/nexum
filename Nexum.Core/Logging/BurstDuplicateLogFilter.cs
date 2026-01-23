using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using Serilog.Core;
using Serilog.Events;

namespace Nexum.Core.Logging
{
    internal sealed class BurstDuplicateLogFilter : ILogEventFilter
    {
        private readonly ConcurrentDictionary<string, long> _lastSeen = new ConcurrentDictionary<string, long>();
        private readonly int _maxKeys;
        private readonly Func<LogEvent, bool> _shouldApply;
        private readonly long _staleMs;
        private readonly long _windowMs;

        private int _cleanupCounter;

        internal BurstDuplicateLogFilter(TimeSpan window, Func<LogEvent, bool> shouldApply, int maxKeys = 4096,
            TimeSpan? staleAfter = null)
        {
            if (window <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(window), window, "Window must be positive");

            _shouldApply = shouldApply ?? throw new ArgumentNullException(nameof(shouldApply));
            _windowMs = (long)Math.Ceiling(window.TotalMilliseconds);
            _staleMs = (long)Math.Ceiling((staleAfter ?? TimeSpan.FromSeconds(15)).TotalMilliseconds);
            _maxKeys = Math.Max(64, maxKeys);
        }

        public bool IsEnabled(LogEvent logEvent)
        {
            if (logEvent.Level >= LogEventLevel.Information)
                return true;

            if (!_shouldApply(logEvent))
                return true;

            long now = Environment.TickCount64;
            string key = ComputeKey(logEvent);

            if (_lastSeen.TryGetValue(key, out long last))
                if (now - last < _windowMs)
                    return false;

            _lastSeen[key] = now;
            MaybeCleanup(now);
            return true;
        }

        internal static BurstDuplicateLogFilter CreateForNexumHolepunching(TimeSpan? window = null)
        {
            return new BurstDuplicateLogFilter(
                window ?? TimeSpan.FromMilliseconds(500),
                ShouldApplyForNexumHolepunching
            );
        }

        private static bool ShouldApplyForNexumHolepunching(LogEvent logEvent)
        {
            string template = logEvent.MessageTemplate.Text;

            return StartsWithAny(template,
                "ServerHolepunchAck",
                "PeerUdpServerHolepunchAck",
                "PeerUdpPeerHolepunch",
                "PeerUdpPeerHolepunchAck",
                "PeerUdpNotifyHolepunchSuccess",
                "PeerUdpServerHolepunch"
            );
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            foreach (string prefix in prefixes)
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                    return true;

            return false;
        }

        private static string ComputeKey(LogEvent logEvent)
        {
            var sb = new StringBuilder(256);

            sb.Append((int)logEvent.Level);
            sb.Append('|');
            sb.Append(logEvent.MessageTemplate.Text);

            if (logEvent.Properties.Count > 0)
            {
                sb.Append('|');

                foreach (var kvp in logEvent.Properties.OrderBy(p => p.Key,
                             StringComparer.Ordinal))
                {
                    sb.Append(kvp.Key);
                    sb.Append('=');
                    sb.Append(kvp.Value);
                    sb.Append(';');
                }
            }

            return sb.ToString();
        }

        private void MaybeCleanup(long now)
        {
            if (_lastSeen.Count <= _maxKeys)
                return;

            if ((Interlocked.Increment(ref _cleanupCounter) & 0xFF) != 0)
                return;

            foreach (var kvp in _lastSeen)
                if (now - kvp.Value > _staleMs)
                    _lastSeen.TryRemove(kvp.Key, out _);
        }
    }
}
