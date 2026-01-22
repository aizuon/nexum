using System;
using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace BaseLib
{
    public sealed class ContextEnricher : ILogEventEnricher
    {
        private const int MinLength = 24;
        private const int WindowSize = 100;
        private const string EmptyContext = "NULL";

        private readonly ConcurrentQueue<int> _recentLengths = new ConcurrentQueue<int>();
        private volatile int _currentMaxLength = MinLength;

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            LogEventPropertyValue value = null;
            foreach (var kvp in logEvent.Properties)
                if (kvp.Key == Constants.SourceContextPropertyName)
                {
                    value = kvp.Value;
                    break;
                }

            ReadOnlySpan<char> ctx;
            string ctxString = null;
            if (value != null)
            {
                ctxString = value.ToString().Replace('"', ' ');
                ctx = ctxString.AsSpan();
            }
            else
            {
                ctx = EmptyContext.AsSpan();
            }

            int originalCtxLen = ctx.Length;

            TrackLength(originalCtxLen);
            int maxLength = _currentMaxLength;

            if (ctx.Length > maxLength)
                ctx = ctx[..maxLength];

            string newCtx;
            int ctxLen = ctx.Length;

            if (ctxLen < maxLength)
            {
                int paddingTotal = maxLength - ctxLen;
                int leftPadding = (paddingTotal + 1) / 2;

                newCtx = string.Create(maxLength, (ctxString, ctxLen, leftPadding), static (span, state) =>
                {
                    span.Fill(' ');
                    var source = state.ctxString != null
                        ? state.ctxString.AsSpan(0, state.ctxLen)
                        : EmptyContext.AsSpan();
                    source.CopyTo(span[state.leftPadding..]);
                });
            }
            else
            {
                newCtx = ctxString != null ? ctxString[..maxLength] : EmptyContext;
            }

            var eventType = propertyFactory.CreateProperty("SrcContext", newCtx);
            logEvent.AddPropertyIfAbsent(eventType);
        }

        private void TrackLength(int length)
        {
            _recentLengths.Enqueue(length);

            while (_recentLengths.Count > WindowSize)
                _recentLengths.TryDequeue(out _);

            if (_recentLengths.Count > 0)
            {
                int maxSeen = MinLength;
                foreach (int len in _recentLengths)
                    if (len > maxSeen)
                        maxSeen = len;

                _currentMaxLength = maxSeen;
            }
        }
    }
}
