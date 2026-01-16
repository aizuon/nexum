using System.Collections.Concurrent;
using System.Threading;
using Nexum.Core;

namespace Nexum.Server
{
    internal class HostIdFactory
    {
        private readonly ConcurrentStack<uint> _pool = new ConcurrentStack<uint>();
        private long _counter = (long)HostId.Last - 1;

        internal uint New()
        {
            return _pool.TryPop(out uint hostId) ? hostId : (uint)Interlocked.Increment(ref _counter);
        }

        internal void Free(uint hostId)
        {
            _pool.Push(hostId);
        }
    }
}
