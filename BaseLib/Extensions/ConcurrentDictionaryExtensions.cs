using System.Collections.Concurrent;

namespace BaseLib.Extensions
{
    public static class ConcurrentDictionaryExtensions
    {
        public static bool Remove<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> @this, TKey key)
        {
            return @this.TryRemove(key, out _);
        }

        public static TValue GetValueOrDefault<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> @this, TKey key)
        {
            return @this.TryGetValue(key, out var value) ? value : default(TValue);
        }
    }
}
