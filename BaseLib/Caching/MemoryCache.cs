using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace BaseLib.Caching
{
    public sealed class MemoryCache : ICache
    {
        private static readonly long TicksPerSecond = Stopwatch.Frequency;
        private static readonly long StartTimestamp = Stopwatch.GetTimestamp();

        private readonly ConcurrentDictionary<string, CacheEntry> _cache =
            new ConcurrentDictionary<string, CacheEntry>();

        public void AddOrUpdate(string key, object value, TimeSpan ttl)
        {
            long expireTicks =
                ttl.Ticks > 0 ? GetCurrentTicks() + ttl.Ticks * TicksPerSecond / TimeSpan.TicksPerSecond : 0;
            var entry = new CacheEntry(expireTicks, value);
            _cache[key] = entry;
        }

        public void AddOrUpdate(string key, object value, long ttlSeconds)
        {
            long expireTicks = ttlSeconds > 0 ? GetCurrentTicks() + ttlSeconds * TicksPerSecond : 0;
            var entry = new CacheEntry(expireTicks, value);
            _cache[key] = entry;
        }

        public void AddOrUpdate(string key, object value)
        {
            var entry = new CacheEntry(0, value);
            _cache[key] = entry;
        }

        public T Get<T>(string key) where T : class
        {
            object obj = Get(key);
            return obj as T;
        }

        public object Get(string key)
        {
            if (!_cache.TryGetValue(key, out var value))
                return null;
            if (value.ExpireTicks == 0)
                return value.Item;
            if (value.ExpireTicks > GetCurrentTicks())
                return value.Item;
            _cache.TryRemove(key, out _);
            return null;
        }

        public bool Remove(string key)
        {
            return _cache.TryRemove(key, out _);
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public void Dispose()
        {
            Clear();
        }

        private static long GetCurrentTicks()
        {
            return Stopwatch.GetTimestamp() - StartTimestamp;
        }

        private readonly struct CacheEntry
        {
            public readonly long ExpireTicks;
            public readonly object Item;

            public CacheEntry(long expireTicks, object item)
            {
                ExpireTicks = expireTicks;
                Item = item;
            }
        }
    }

    public interface ICache : IDisposable
    {
        void AddOrUpdate(string key, object value, TimeSpan ts);

        void AddOrUpdate(string key, object value, long ttl);

        void AddOrUpdate(string key, object value);

        T Get<T>(string key) where T : class;

        object Get(string key);

        bool Remove(string key);

        void Clear();
    }
}
