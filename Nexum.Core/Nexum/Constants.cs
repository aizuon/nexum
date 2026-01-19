using System.Text;

namespace Nexum.Core
{
    internal static class Constants
    {
        internal const ushort TcpSplitter = 0x5713;
        internal const ushort UdpFullPacketSplitter = 0xABCD;
        internal const ushort UdpFragmentSplitter = 0xABCE;
        internal const uint NetVersion = 196980;
        internal static readonly Encoding Encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252);
    }
}
