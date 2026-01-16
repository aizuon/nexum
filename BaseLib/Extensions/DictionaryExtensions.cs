using System;
using System.Collections.Generic;

namespace BaseLib.Extensions
{
    public static class DictionaryExtensions
    {
        public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key, TValue value)
        {
            if (!CollectionExtensions.TryAdd(source, key, value))
                return false;
            return true;
        }

        public static bool TryRemove<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key)
        {
            if (!source.Remove(key))
                return false;
            return true;
        }

        public static TValue AddOrUpdate<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key,
            Func<TKey, TValue> addValueFactory, Func<TKey, TValue, TValue> updateValueFactory)
        {
            if (source.TryGetValue(key, out var value))
                return source[key] = updateValueFactory(key, value);
            var val2 = addValueFactory(key);
            source.Add(key, val2);
            return val2;
        }

        public static TValue AddOrUpdate<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key, TValue addValue,
            Func<TKey, TValue, TValue> updateValueFactory)
        {
            if (source.TryGetValue(key, out var value))
                return source[key] = updateValueFactory(key, value);
            source.Add(key, addValue);
            return addValue;
        }
    }
}
