using System;
using System.Collections.Generic;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Nexum.Core.Logging
{
    public sealed class BurstDuplicateLogger : ILogger
    {
        private readonly ILogEventFilter _filter;
        private readonly ILogger _inner;

        internal BurstDuplicateLogger(ILogger inner, ILogEventFilter filter)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        }

        public bool IsEnabled(LogEventLevel level)
        {
            return _inner.IsEnabled(level);
        }

        public void Write(LogEvent logEvent)
        {
            if (!_filter.IsEnabled(logEvent))
                return;

            _inner.Write(logEvent);
        }

        public void Write(LogEventLevel level, string messageTemplate)
        {
            WriteInternal(level, null, messageTemplate, Array.Empty<object>());
        }

        public void Write<T>(LogEventLevel level, string messageTemplate, T propertyValue)
        {
            WriteInternal(level, null, messageTemplate, new object[] { propertyValue });
        }

        public void Write<T0, T1>(LogEventLevel level, string messageTemplate, T0 propertyValue0, T1 propertyValue1)
        {
            WriteInternal(level, null, messageTemplate, new object[] { propertyValue0, propertyValue1 });
        }

        public void Write<T0, T1, T2>(LogEventLevel level, string messageTemplate, T0 propertyValue0,
            T1 propertyValue1, T2 propertyValue2)
        {
            WriteInternal(level, null, messageTemplate,
                new object[] { propertyValue0, propertyValue1, propertyValue2 });
        }

        public void Write(LogEventLevel level, string messageTemplate, params object[] propertyValues)
        {
            WriteInternal(level, null, messageTemplate, propertyValues);
        }

        public void Write(LogEventLevel level, Exception exception, string messageTemplate)
        {
            WriteInternal(level, exception, messageTemplate, Array.Empty<object>());
        }

        public void Write<T>(LogEventLevel level, Exception exception, string messageTemplate, T propertyValue)
        {
            WriteInternal(level, exception, messageTemplate, new object[] { propertyValue });
        }

        public void Write<T0, T1>(LogEventLevel level, Exception exception, string messageTemplate, T0 propertyValue0,
            T1 propertyValue1)
        {
            WriteInternal(level, exception, messageTemplate, new object[] { propertyValue0, propertyValue1 });
        }

        public void Write<T0, T1, T2>(LogEventLevel level, Exception exception, string messageTemplate,
            T0 propertyValue0, T1 propertyValue1, T2 propertyValue2)
        {
            WriteInternal(level, exception, messageTemplate,
                new object[] { propertyValue0, propertyValue1, propertyValue2 });
        }

        public void Write(LogEventLevel level, Exception exception, string messageTemplate,
            params object[] propertyValues)
        {
            WriteInternal(level, exception, messageTemplate, propertyValues);
        }

        public bool BindMessageTemplate(string messageTemplate, object[] propertyValues,
            out MessageTemplate parsedTemplate, out IEnumerable<LogEventProperty> boundProperties)
        {
            return _inner.BindMessageTemplate(messageTemplate, propertyValues, out parsedTemplate, out boundProperties);
        }

        public bool BindProperty(string propertyName, object value, bool destructureObjects,
            out LogEventProperty property)
        {
            return _inner.BindProperty(propertyName, value, destructureObjects, out property);
        }

        public ILogger ForContext(ILogEventEnricher enricher)
        {
            return new BurstDuplicateLogger(_inner.ForContext(enricher), _filter);
        }

        public ILogger ForContext(IEnumerable<ILogEventEnricher> enrichers)
        {
            return new BurstDuplicateLogger(_inner.ForContext(enrichers), _filter);
        }

        public ILogger ForContext(string propertyName, object value, bool destructureObjects = false)
        {
            return new BurstDuplicateLogger(_inner.ForContext(propertyName, value, destructureObjects), _filter);
        }

        public ILogger ForContext<TSource>()
        {
            return new BurstDuplicateLogger(_inner.ForContext<TSource>(), _filter);
        }

        public ILogger ForContext(Type source)
        {
            return new BurstDuplicateLogger(_inner.ForContext(source), _filter);
        }

        public static ILogger WrapForNexumHolepunching(ILogger inner, TimeSpan? window = null)
        {
            var filter = BurstDuplicateLogFilter.CreateForNexumHolepunching(window);
            return new BurstDuplicateLogger(inner, filter);
        }

        private void WriteInternal(LogEventLevel level, Exception exception, string messageTemplate,
            object[] propertyValues)
        {
            if (level >= LogEventLevel.Information)
            {
                _inner.Write(level, exception, messageTemplate, propertyValues);
                return;
            }

            if (!_inner.IsEnabled(level))
                return;

            if (!TryCreateSyntheticLogEvent(level, exception, messageTemplate, propertyValues, out var synthetic))
            {
                _inner.Write(level, exception, messageTemplate, propertyValues);
                return;
            }

            if (!_filter.IsEnabled(synthetic))
                return;

            _inner.Write(level, exception, messageTemplate, propertyValues);
        }

        private bool TryCreateSyntheticLogEvent(LogEventLevel level, Exception exception, string messageTemplate,
            object[] propertyValues, out LogEvent logEvent)
        {
            logEvent = null;

            if (messageTemplate == null)
                return false;

            if (!_inner.BindMessageTemplate(messageTemplate, propertyValues, out var parsed,
                    out var bound))
                return false;

            var properties = new List<LogEventProperty>();
            properties.AddRange(bound);

            logEvent = new LogEvent(DateTimeOffset.UtcNow, level, exception, parsed, properties);
            return true;
        }
    }
}
