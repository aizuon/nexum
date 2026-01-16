using System;
using Serilog.Core;
using Serilog.Events;

namespace BaseLib
{
    public sealed class ContextEnricher : ILogEventEnricher
    {
        private const int MaxLength = 20;
        private const string EmptyContext = "NULL";

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

            if (ctx.Length > MaxLength)
                ctx = ctx[..MaxLength];

            string newCtx;
            int ctxLen = ctx.Length;

            if (ctxLen < MaxLength)
            {
                int paddingTotal = MaxLength - ctxLen;
                int leftPadding = (paddingTotal + 1) / 2;

                newCtx = string.Create(MaxLength, (ctxString, ctxLen, leftPadding), static (span, state) =>
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
                newCtx = ctxString != null ? ctxString[..MaxLength] : EmptyContext;
            }

            var eventType = propertyFactory.CreateProperty("SrcContext", newCtx);
            logEvent.AddPropertyIfAbsent(eventType);
        }
    }
}
