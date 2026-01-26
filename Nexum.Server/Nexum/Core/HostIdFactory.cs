using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Nexum.Core.Routing;

namespace Nexum.Server.Core
{
    internal sealed class HostIdFactory
    {
        private readonly ConcurrentStack<uint> _pool = new ConcurrentStack<uint>();
        private long _counter = (long)HostId.Last - 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal uint New()
        {
            return _pool.TryPop(out uint hostId) ? hostId : (uint)Interlocked.Increment(ref _counter);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Free(uint hostId)
        {
            _pool.Push(hostId);
        }
    }
}
