namespace Nexum.Client
{
    internal static class SysUtil
    {
        internal static void Swap<T>(ref T lhs, ref T rhs)
        {
            var obj = lhs;
            lhs = rhs;
            rhs = obj;
        }
    }
}
