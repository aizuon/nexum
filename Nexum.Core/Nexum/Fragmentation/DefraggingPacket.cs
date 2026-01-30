using System.Collections.Generic;
using System.Threading;

namespace Nexum.Core.Fragmentation
{
    internal sealed class DefraggingPacket
    {
        internal SpinLock Lock = new SpinLock(false);
        internal byte[] AssembledData { get; set; }
        internal bool[] FragmentReceivedFlags { get; set; }
        internal int FragmentsReceivedCount { get; set; }
        internal int TotalFragmentCount { get; set; }
        internal double CreatedTime { get; set; }
        internal int InferredMtu { get; set; }
        internal bool MtuConfirmed { get; set; }
        internal Dictionary<uint, BufferedFragment> BufferedFragments { get; set; }

        internal bool IsComplete => MtuConfirmed && FragmentsReceivedCount == TotalFragmentCount;
    }

    internal readonly struct BufferedFragment
    {
        internal readonly byte[] Data;

        internal BufferedFragment(byte[] data)
        {
            Data = data;
        }
    }
}
