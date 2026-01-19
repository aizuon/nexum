using System.Collections.Generic;

namespace Nexum.Core
{
    internal sealed class DefraggingPacket
    {
        public byte[] AssembledData { get; set; }
        public bool[] FragmentReceivedFlags { get; set; }
        public int FragmentsReceivedCount { get; set; }
        public int TotalFragmentCount { get; set; }
        public double CreatedTime { get; set; }
        public int InferredMtu { get; set; }
        public bool MtuConfirmed { get; set; }
        public Dictionary<uint, BufferedFragment> BufferedFragments { get; set; }

        public bool IsComplete => MtuConfirmed && FragmentsReceivedCount == TotalFragmentCount;
    }
}
