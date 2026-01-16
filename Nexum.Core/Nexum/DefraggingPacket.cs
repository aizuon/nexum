namespace Nexum.Core
{
    internal sealed class DefraggingPacket
    {
        public byte[] AssembledData { get; set; }

        public bool[] FragmentReceivedFlags { get; set; }

        public int FragmentsReceivedCount { get; set; }

        public int TotalFragmentCount { get; set; }

        public double CreatedTime { get; set; }

        public bool IsComplete => FragmentsReceivedCount == TotalFragmentCount;
    }
}
