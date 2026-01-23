using System.Collections.Generic;

namespace Nexum.Core
{
    internal sealed class DefraggingPacket
    {
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
}
