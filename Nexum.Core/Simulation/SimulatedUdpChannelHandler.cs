using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace Nexum.Core.Simulation
{
    public class SimulatedUdpChannelHandler : ChannelHandlerAdapter
    {
        private readonly ConcurrentDictionary<IPAddress, DateTime> _allowedAddresses =
            new ConcurrentDictionary<IPAddress, DateTime>();

        private readonly ConcurrentDictionary<IPEndPoint, DateTime> _allowedEndpoints =
            new ConcurrentDictionary<IPEndPoint, DateTime>();

        private readonly NetworkProfile _profile;
        private readonly Random _random;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        private readonly ConcurrentDictionary<IPEndPoint, int> _symmetricPortMappings =
            new ConcurrentDictionary<IPEndPoint, int>();

        private readonly object _syncLock = new object();

        private long _bytesSentThisSecond;

        private bool _inBurstLoss;
        private double _lastBandwidthResetTime;
        private int _nextSymmetricPort = 30000;
        private long _packetsDelayed;
        private long _packetsDropped;
        private long _packetsNatFiltered;

        private long _packetsReceived;
        private long _packetsSent;

        public SimulatedUdpChannelHandler(NetworkProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _random = profile.RandomSeed.HasValue
                ? new Random(profile.RandomSeed.Value)
                : new Random();
        }

        public (long Received, long Dropped, long Sent, long Delayed, long NatFiltered) GetStatistics()
        {
            return (_packetsReceived, _packetsDropped, _packetsSent, _packetsDelayed, _packetsNatFiltered);
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            if (_profile.IsIdeal)
            {
                base.ChannelRead(context, message);
                return;
            }

            Interlocked.Increment(ref _packetsReceived);

            if (_profile.NatType != NatType.None && _profile.SimulateInbound)
            {
                var senderEndpoint = GetSenderEndpoint(message);
                if (senderEndpoint != null && !IsAllowedByNat(senderEndpoint))
                {
                    Interlocked.Increment(ref _packetsNatFiltered);

                    if (message is DatagramPacket packet)
                        packet.Content.Release();
                    else if (message is UdpMessage udpMessage)
                        udpMessage.Content.Release();

                    return;
                }
            }

            if (!_profile.SimulateInbound)
            {
                base.ChannelRead(context, message);
                return;
            }

            if (ShouldDropPacket())
            {
                Interlocked.Increment(ref _packetsDropped);

                if (message is DatagramPacket packet)
                    packet.Content.Release();
                else if (message is UdpMessage udpMessage)
                    udpMessage.Content.Release();

                return;
            }

            int delayMs = CalculateDelay();
            bool shouldReorder = ShouldReorder();

            if (shouldReorder)
                delayMs += _profile.ReorderDelayMs;

            if (delayMs > 0)
            {
                Interlocked.Increment(ref _packetsDelayed);

                if (message is DatagramPacket packet)
                    packet.Content.Retain();
                else if (message is UdpMessage udpMessage)
                    udpMessage.Content.Retain();

                _ = Task.Run(async () =>
                {
                    await Task.Delay(delayMs);
                    try
                    {
                        if (context.Channel.Active)
                        {
                            base.ChannelRead(context, message);
                        }
                        else
                        {
                            if (message is DatagramPacket pkt)
                                pkt.Content.Release();
                            else if (message is UdpMessage udpMsg)
                                udpMsg.Content.Release();
                        }
                    }
                    catch (Exception ex) when (ex is ClosedChannelException
                                                   or ObjectDisposedException)
                    {
                        if (message is DatagramPacket pkt)
                            pkt.Content.SafeRelease();
                        else if (message is UdpMessage udpMsg)
                            udpMsg.Content.SafeRelease();
                    }
                    catch (Exception)
                    {
                        if (message is DatagramPacket pkt)
                            pkt.Content.SafeRelease();
                        else if (message is UdpMessage udpMsg)
                            udpMsg.Content.SafeRelease();
                    }
                });
            }
            else
            {
                base.ChannelRead(context, message);
            }

            if (ShouldDuplicate())
            {
                if (message is DatagramPacket packet)
                {
                    packet.Content.Retain();
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(_random.Next(1, 10));
                        try
                        {
                            if (context.Channel.Active)
                                base.ChannelRead(context, message);
                            else
                                packet.Content.Release();
                        }
                        catch
                        {
                            packet.Content.SafeRelease();
                        }
                    });
                }
                else if (message is UdpMessage udpMessage)
                {
                    udpMessage.Content.Retain();
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(_random.Next(1, 10));
                        try
                        {
                            if (context.Channel.Active)
                                base.ChannelRead(context, message);
                            else
                                udpMessage.Content.Release();
                        }
                        catch
                        {
                            udpMessage.Content.SafeRelease();
                        }
                    });
                }
            }
        }

        public override Task WriteAsync(IChannelHandlerContext context, object message)
        {
            if (_profile.IsIdeal)
                return base.WriteAsync(context, message);

            Interlocked.Increment(ref _packetsSent);

            if (_profile.NatType != NatType.None)
            {
                var destinationEndpoint = GetDestinationEndpoint(message);
                if (destinationEndpoint != null)
                    RecordOutboundDestination(destinationEndpoint);
            }

            if (!_profile.SimulateOutbound)
                return base.WriteAsync(context, message);

            if (ShouldDropPacket())
            {
                Interlocked.Increment(ref _packetsDropped);

                if (message is DatagramPacket packet)
                    packet.Content.Release();
                else if (message is UdpMessage udpMessage)
                    udpMessage.Content.Release();

                return Task.CompletedTask;
            }

            if (_profile.BandwidthLimitBytesPerSecond > 0)
            {
                int packetSize = GetPacketSize(message);
                if (!TryConsumeBandwidth(packetSize))
                {
                    Interlocked.Increment(ref _packetsDropped);

                    if (message is DatagramPacket packet)
                        packet.Content.Release();
                    else if (message is UdpMessage udpMessage)
                        udpMessage.Content.Release();

                    return Task.CompletedTask;
                }
            }

            int delayMs = CalculateDelay();
            bool shouldReorder = ShouldReorder();

            if (shouldReorder)
                delayMs += _profile.ReorderDelayMs;

            if (delayMs > 0)
            {
                Interlocked.Increment(ref _packetsDelayed);

                return Task.Run(async () =>
                {
                    await Task.Delay(delayMs);
                    try
                    {
                        if (context.Channel.Active)
                        {
                            await base.WriteAsync(context, message);
                        }
                        else
                        {
                            if (message is DatagramPacket pkt)
                                pkt.Content.Release();
                            else if (message is UdpMessage udpMsg)
                                udpMsg.Content.Release();
                        }
                    }
                    catch (Exception ex) when (ex is ClosedChannelException
                                                   or ObjectDisposedException)
                    {
                        if (message is DatagramPacket pkt)
                            pkt.Content.SafeRelease();
                        else if (message is UdpMessage udpMsg)
                            udpMsg.Content.SafeRelease();
                    }
                    catch (Exception)
                    {
                        if (message is DatagramPacket pkt)
                            pkt.Content.SafeRelease();
                        else if (message is UdpMessage udpMsg)
                            udpMsg.Content.SafeRelease();
                    }
                });
            }

            return base.WriteAsync(context, message);
        }

        private bool ShouldDropPacket()
        {
            lock (_syncLock)
            {
                if (_profile.BurstLossStartProbability > 0 || _profile.BurstLossContinueProbability > 0)
                {
                    if (_inBurstLoss)
                    {
                        if (_random.NextDouble() < _profile.BurstLossContinueProbability)
                            return true;

                        _inBurstLoss = false;
                    }
                    else if (_random.NextDouble() < _profile.BurstLossStartProbability)
                    {
                        _inBurstLoss = true;
                        return true;
                    }
                }

                return _random.NextDouble() < _profile.PacketLossRate;
            }
        }

        private int CalculateDelay()
        {
            if (_profile.LatencyMs == 0 && _profile.JitterMs == 0)
                return 0;

            int jitter = _profile.JitterMs > 0
                ? _random.Next(-_profile.JitterMs, _profile.JitterMs + 1)
                : 0;

            return Math.Max(0, _profile.LatencyMs + jitter);
        }

        private bool ShouldReorder()
        {
            return _profile.PacketReorderRate > 0 && _random.NextDouble() < _profile.PacketReorderRate;
        }

        private bool ShouldDuplicate()
        {
            return _profile.PacketDuplicateRate > 0 && _random.NextDouble() < _profile.PacketDuplicateRate;
        }

        private bool TryConsumeBandwidth(int bytes)
        {
            lock (_syncLock)
            {
                double currentTime = _stopwatch.Elapsed.TotalSeconds;

                if (currentTime - _lastBandwidthResetTime >= 1.0)
                {
                    _bytesSentThisSecond = 0;
                    _lastBandwidthResetTime = currentTime;
                }

                if (_bytesSentThisSecond + bytes > _profile.BandwidthLimitBytesPerSecond)
                    return false;

                _bytesSentThisSecond += bytes;
                return true;
            }
        }

        private static int GetPacketSize(object message)
        {
            return message switch
            {
                DatagramPacket packet => packet.Content.ReadableBytes,
                UdpMessage udpMessage => udpMessage.Content.ReadableBytes,
                IByteBuffer buffer => buffer.ReadableBytes,
                _ => 100
            };
        }

        #region NAT Simulation

        private void RecordOutboundDestination(IPEndPoint destination)
        {
            var now = DateTime.UtcNow;

            switch (_profile.NatType)
            {
                case NatType.FullCone:
                    _allowedAddresses.TryAdd(IPAddress.Any, now);
                    break;

                case NatType.AddressRestricted:
                    _allowedAddresses[destination.Address] = now;
                    break;

                case NatType.PortRestricted:
                    _allowedEndpoints[destination] = now;
                    break;

                case NatType.Symmetric:
                    _allowedEndpoints[destination] = now;
                    if (!_symmetricPortMappings.ContainsKey(destination))
                    {
                        int mappedPort = Interlocked.Increment(ref _nextSymmetricPort);
                        _symmetricPortMappings[destination] = mappedPort;
                    }

                    break;
            }

            CleanupStaleNatEntries(now);
        }

        private bool IsAllowedByNat(IPEndPoint sender)
        {
            switch (_profile.NatType)
            {
                case NatType.None:
                    return true;

                case NatType.FullCone:
                    return _allowedAddresses.ContainsKey(IPAddress.Any);

                case NatType.AddressRestricted:
                    return _allowedAddresses.ContainsKey(sender.Address);

                case NatType.PortRestricted:
                    return _allowedEndpoints.ContainsKey(sender);

                case NatType.Symmetric:
                    if (!_allowedEndpoints.ContainsKey(sender))
                        return false;
                    return _random.NextDouble() > 0.3;

                default:
                    return true;
            }
        }

        private static IPEndPoint GetSenderEndpoint(object message)
        {
            return message switch
            {
                DatagramPacket packet => packet.Sender as IPEndPoint,
                UdpMessage udpMessage => udpMessage.EndPoint,
                _ => null
            };
        }

        private static IPEndPoint GetDestinationEndpoint(object message)
        {
            return message switch
            {
                DatagramPacket packet => packet.Recipient as IPEndPoint,
                UdpMessage udpMessage => udpMessage.EndPoint,
                _ => null
            };
        }

        private void CleanupStaleNatEntries(DateTime now)
        {
            var cutoff = now.AddSeconds(-60);

            foreach (var kvp in _allowedAddresses)
                if (kvp.Value < cutoff)
                    _allowedAddresses.TryRemove(kvp.Key, out _);

            foreach (var kvp in _allowedEndpoints)
                if (kvp.Value < cutoff)
                {
                    _allowedEndpoints.TryRemove(kvp.Key, out _);
                    _symmetricPortMappings.TryRemove(kvp.Key, out _);
                }
        }

        #endregion
    }

    internal static class ByteBufferExtensions
    {
        public static void SafeRelease(this IByteBuffer buffer)
        {
            try
            {
                if (buffer.ReferenceCount > 0)
                    buffer.Release();
            }
            catch
            {
            }
        }
    }
}
