using System;

namespace BaseLib.Patterns
{
    public static class Singleton<T> where T : class
    {
        private static Lazy<T> _instance = new Lazy<T>(() => (T)Activator.CreateInstance(typeof(T), true));

        public static T Instance => _instance.Value;

        public static void SetInstance(T instance)
        {
            _instance = new Lazy<T>(() => instance);
        }
    }

    public abstract class SingletonBase<T> where T : SingletonBase<T>
    {
        private static Lazy<T> _instance = new Lazy<T>(() => (T)Activator.CreateInstance(typeof(T), true));

        public static T Instance => _instance.Value;

        protected static void SetInstance(T instance)
        {
            _instance = new Lazy<T>(() => instance);
        }
    }
}
