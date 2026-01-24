using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace Nexum.Client
{
    internal static class NetUtil
    {
        private static readonly Mutex PortMutex = new Mutex(false, "NexumAvailablePortMutex");
        private static readonly HashSet<int> _reservedPorts = new HashSet<int>();
        private static readonly List<(int Start, int End)> _windowsExcludedRanges = new List<(int, int)>();
        private static bool _excludedRangesInitialized;

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

        internal static int GetAvailablePort(int startingPort)
        {
            PortMutex.WaitOne();
            try
            {
                if (startingPort < IPEndPoint.MinPort)
                    startingPort = IPEndPoint.MinPort;

                if (startingPort > IPEndPoint.MaxPort)
                    throw new ArgumentOutOfRangeException(nameof(startingPort));

                InitializeExcludedPortRanges();

                var ipProps = IPGlobalProperties.GetIPGlobalProperties();
                var used = new HashSet<int>();

                foreach (var c in ipProps.GetActiveTcpConnections())
                    used.Add(c.LocalEndPoint.Port);

                foreach (var l in ipProps.GetActiveTcpListeners())
                    used.Add(l.Port);

                foreach (var l in ipProps.GetActiveUdpListeners())
                    used.Add(l.Port);

                for (int port = startingPort; port <= IPEndPoint.MaxPort; port++)
                {
                    if (used.Contains(port))
                        continue;

                    if (IsPortInExcludedRange(port))
                        continue;

                    if (!_reservedPorts.Add(port))
                        continue;

                    return port;
                }

                throw new InvalidOperationException("No available ports.");
            }
            finally
            {
                PortMutex.ReleaseMutex();
            }
        }

        private static void InitializeExcludedPortRanges()
        {
            if (_excludedRangesInitialized)
                return;

            _excludedRangesInitialized = true;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "int ipv4 show excludedportrange protocol=udp",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return;

                var readStdOutTask = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(3000))
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch (Exception)
                    {
                    }

                    return;
                }

                if (!readStdOutTask.Wait(1000))
                    return;

                string output = readStdOutTask.Result;

                var regex = new Regex(@"^\s*(\d+)\s+(\d+)", RegexOptions.Multiline);
                foreach (Match match in regex.Matches(output))
                    if (int.TryParse(match.Groups[1].Value, out int start) &&
                        int.TryParse(match.Groups[2].Value, out int end))
                        _windowsExcludedRanges.Add((start, end));
            }
            catch (Exception)
            {
            }
        }

        private static bool IsPortInExcludedRange(int port)
        {
            foreach ((int start, int end) in _windowsExcludedRanges)
                if (port >= start && port <= end)
                    return true;

            return false;
        }


        internal static void ReleasePort(int port)
        {
            PortMutex.WaitOne();
            try
            {
                _reservedPorts.Remove(port);
            }
            finally
            {
                PortMutex.ReleaseMutex();
            }
        }
    }
}
