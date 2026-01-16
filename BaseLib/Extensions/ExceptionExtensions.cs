using System;
using System.Runtime.ExceptionServices;

namespace BaseLib.Extensions
{
    public static class ExceptionExtensions
    {
        public static Exception Rethrow(this Exception @this)
        {
            ExceptionDispatchInfo.Capture(@this).Throw();
            return null;
        }
    }
}
