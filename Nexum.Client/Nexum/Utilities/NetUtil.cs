using System;
using System.Net;
using System.Runtime.CompilerServices;

namespace Nexum.Client.Utilities
{
    internal static class NetUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsUnicastEndpoint(IPEndPoint addrPort)
        {
            if (addrPort.Port == 0 || (ushort)addrPort.Port == ushort.MaxValue)
                return false;

            Span<byte> addressBytes = stackalloc byte[4];
            if (!addrPort.Address.TryWriteBytes(addressBytes, out int bytesWritten) || bytesWritten != 4)
                return false;

            uint ipValue = (uint)(addressBytes[0] | (addressBytes[1] << 8) | (addressBytes[2] << 16) |
                                  (addressBytes[3] << 24));
            return ipValue != 0 && ipValue != uint.MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsSameHost(IPEndPoint a, IPEndPoint b)
        {
            return a.Address.Equals(b.Address);
        }

        internal static bool IsSameLan(IPEndPoint a, IPEndPoint b)
        {
            Span<byte> addr1 = stackalloc byte[4];
            Span<byte> addr2 = stackalloc byte[4];

            if (!a.Address.TryWriteBytes(addr1, out int written1) || written1 != 4)
                return false;
            if (!b.Address.TryWriteBytes(addr2, out int written2) || written2 != 4)
                return false;

            if (addr1[0] == 127 && addr2[0] == 127)
                return true;
            int subnetBits;

            if (addr1[0] == 10 && addr2[0] == 10)
                subnetBits = 8;
            else if (addr1[0] == 172 && addr2[0] == 172 &&
                     addr1[1] >= 16 && addr1[1] <= 31 &&
                     addr2[1] >= 16 && addr2[1] <= 31)
                subnetBits = 12;
            else if (addr1[0] == 192 && addr1[1] == 168 &&
                     addr2[0] == 192 && addr2[1] == 168)
                subnetBits = 24;
            else if (addr1[0] == 169 && addr1[1] == 254 &&
                     addr2[0] == 169 && addr2[1] == 254)
                subnetBits = 16;
            else
                subnetBits = 24;
            int fullBytes = subnetBits / 8;
            int remainingBits = subnetBits % 8;
            for (int i = 0; i < fullBytes; i++)
                if (addr1[i] != addr2[i])
                    return false;
            if (remainingBits > 0 && fullBytes < 4)
            {
                byte mask = (byte)(0xFF << (8 - remainingBits));
                if ((addr1[fullBytes] & mask) != (addr2[fullBytes] & mask))
                    return false;
            }

            return true;
        }
    }
}
