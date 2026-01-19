using System;
using System.Net;
using System.Runtime.CompilerServices;
using DotNetty.Transport.Channels;
using Nexum.Core;
using Serilog;

namespace Nexum.Client
{
    public class P2PMember
    {
        internal readonly ILogger Logger;
        internal readonly MtuDiscovery MtuDiscovery = new MtuDiscovery();
        internal readonly NetClient Owner;
        internal readonly UdpPacketDefragBoard UdpDefragBoard = new UdpPacketDefragBoard();
        internal readonly UdpPacketFragBoard UdpFragBoard = new UdpPacketFragBoard();

        private double _nextPingTime;
        private double _nextTimeSyncTime;
        internal bool EnableDirectP2P = true;

        internal double IndirectServerTimeDiff;
        internal bool IsClosed;
        internal double JitterInternal;
        internal double LastPeerServerPing;
        internal double LastPingInternal;
        internal double LastUdpReceivedTime;

        internal bool LocalPortReuseSuccess;
        internal bool P2PHolepunchInitiated;
        internal bool P2PHolepunchNotified;
        internal bool P2PHolepunchStarted;

        internal object P2PMutex = new object();
        internal int PeerBindPort;
        internal double PeerFrameRateInternal;

        internal IPEndPoint PeerLocalToRemoteSocket;
        internal IPEndPoint PeerRemoteToLocalSocket;
        internal double PeerServerJitterInternal;
        internal double PeerServerPingInternal;

        internal IChannel PeerUdpChannel;
        internal IEventLoopGroup PeerUdpEventLoopGroup;
        internal IPEndPoint PeerUdpLocalSocket;

        internal Guid PeerUdpMagicNumber;
        internal IPEndPoint PeerUdpSocket;
        internal double RecentPing;

        internal uint SelfFrameNumber;
        internal IPEndPoint SelfLocalToRemoteSocket;
        internal IPEndPoint SelfRemoteToLocalSocket;
        internal IPEndPoint SelfUdpLocalSocket;
        internal IPEndPoint SelfUdpSocket;

        internal ReliableUdpHost ToPeerReliableUdp;

        internal P2PMember(NetClient owner, uint groupId, uint hostId)
        {
            Owner = owner;
            GroupId = groupId;
            HostId = hostId;
            Logger = owner.Logger.ForContext("P2PMember", hostId);

            UdpDefragBoard.LocalHostId = owner.HostId;
            UdpFragBoard.MtuDiscovery = MtuDiscovery;
            UdpFragBoard.DefragBoard = UdpDefragBoard;
        }

        public double Ping => RecentPing;

        public double Jitter => JitterInternal;

        public double PeerTimeDifference => IndirectServerTimeDiff;

        public double PeerFrameRate => PeerFrameRateInternal;

        public double ServerPing => PeerServerPingInternal;

        public double ServerJitter => PeerServerJitterInternal;

        public uint GroupId { get; }
        public uint HostId { get; }

        public bool DirectP2P { get; internal set; }

        public bool DirectP2PReady => DirectP2P && PeerUdpChannel != null && PeerUdpChannel.Active &&
                                      PeerLocalToRemoteSocket != null;

        internal void InitializeReliableUdp(uint senderFirstFrameNumber, uint receiverExpectedFrameNumber)
        {
            ToPeerReliableUdp = new ReliableUdpHost(senderFirstFrameNumber, receiverExpectedFrameNumber)
            {
                GetAbsoluteTime = () => Owner.GetAbsoluteTime(),
                GetRecentPing = () => RecentPing > 0 ? RecentPing : 0.1,
                GetUdpSendBufferPacketFilledCount = () => 0,
                IsReliableChannel = () => !DirectP2P,
                SendOneFrameToUdpLayer = SendReliableUdpFrame
            };

            ToPeerReliableUdp.OnFailed += OnReliableUdpFailed;
        }

        private void SendReliableUdpFrame(ReliableUdpFrame frame)
        {
            var msg = ReliableUdpHelper.BuildFrameMessage(frame);
            NexumToPeer(msg);
        }

        internal void HandleRemoteDisconnect()
        {
            Logger.Debug("Remote peer {HostId} disconnected, falling back to relay", HostId);
            FallbackP2PToRelay(false);
        }

        private void OnReliableUdpFailed()
        {
            Logger.Warning("Reliable UDP to peer {HostId} failed, falling back to relay and requesting re-holepunch",
                HostId);

            FallbackP2PToRelay();
        }

        internal void ProcessReceivedReliableUdpFrame(ReliableUdpFrame frame)
        {
            ToPeerReliableUdp?.TakeReceivedFrame(frame);
        }

        internal void FrameMove(double elapsedTime)
        {
            double currentTime = Owner.GetAbsoluteTime();
            ToPeerReliableUdp?.FrameMove(elapsedTime);
            UdpDefragBoard.PruneStalePackets(currentTime);

            if (!DirectP2P || PeerLocalToRemoteSocket == null)
                return;

            if (LastUdpReceivedTime > 0)
            {
                double timeSinceLastUdp = currentTime - LastUdpReceivedTime;
                if (timeSinceLastUdp > ReliableUdpConfig.FallbackP2PUdpToTcpTimeout)
                {
                    Logger.Warning(
                        "P2P UDP timeout detected for peer {HostId} ({TimeSinceLastUdp:F1}s since last packet), falling back to relay",
                        HostId, timeSinceLastUdp);

                    FallbackP2PToRelay();
                    return;
                }
            }

            if (currentTime >= _nextPingTime)
            {
                SendP2PPing();
                _nextPingTime = currentTime + Core.SysUtil.Lerp(
                    ReliableUdpConfig.P2PPingInterval * 0.5,
                    ReliableUdpConfig.P2PPingInterval,
                    Random.Shared.NextDouble());
            }

            if (currentTime >= _nextTimeSyncTime)
            {
                SendTimeSyncPing();
                _nextTimeSyncTime = currentTime + Core.SysUtil.Lerp(
                    ReliableUdpConfig.P2PTimeSyncInterval * 0.5,
                    ReliableUdpConfig.P2PTimeSyncInterval,
                    Random.Shared.NextDouble());
            }
        }

        private void SendTimeSyncPing()
        {
            var pingMsg = new NetMessage();
            pingMsg.Write(Owner.GetAbsoluteTime());
            pingMsg.Write(Owner.RecentFrameRate);

            RmiToPeer((ushort)NexumOpCode.ReportServerTimeAndFrameRateAndPing, pingMsg, reliable: true);
        }

        private void FallbackP2PToRelay(bool firstChance = true)
        {
            lock (P2PMutex)
            {
                if (!DirectP2P)
                    return;

                DirectP2P = false;
                P2PHolepunchInitiated = false;
                P2PHolepunchNotified = false;
                P2PHolepunchStarted = false;
                LastUdpReceivedTime = 0;

                if (ToPeerReliableUdp != null)
                {
                    ToPeerReliableUdp.OnFailed -= OnReliableUdpFailed;
                    ToPeerReliableUdp = null;
                }

                PeerUdpChannel?.CloseAsync();
                PeerUdpChannel = null;
                PeerUdpEventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
                PeerUdpEventLoopGroup = null;

                PeerLocalToRemoteSocket = null;
                PeerRemoteToLocalSocket = null;
            }

            if (firstChance)
            {
                var notify = new NetMessage();
                notify.Write(HostId);
                notify.WriteEnum(ErrorType.P2PUdpFailed);
                Owner.RmiToServer((ushort)NexumOpCode.P2P_NotifyDirectP2PDisconnected, notify);
            }

            var jitNotify = new NetMessage();
            jitNotify.Write(HostId);
            Owner.RmiToServer((ushort)NexumOpCode.NotifyJitDirectP2PTriggered, jitNotify);
        }

        private void SendP2PPing()
        {
            double currentTime = Owner.GetAbsoluteTime();
            int paddingSize = MtuDiscovery.GetProbePaddingSize(currentTime);

            var pingMsg = new NetMessage();
            pingMsg.WriteEnum(MessageType.P2PRequestIndirectServerTimeAndPing);
            pingMsg.Write(currentTime);

            if (paddingSize > 0)
            {
                pingMsg.Write(paddingSize);
                pingMsg.WriteZeroes(paddingSize);
            }
            else
            {
                pingMsg.Write(0);
            }

            NexumToPeer(pingMsg);
        }

        public void RmiToPeer(ushort rmiId, NetMessage message, bool forceRelay = false, bool reliable = false)
        {
            var data = new NetMessage();

            data.WriteEnum(MessageType.RMI);
            data.Write(rmiId);
            data.Write(message);

            if (DirectP2P && !forceRelay)
            {
                if (reliable && ToPeerReliableUdp != null)
                {
                    byte[] wrappedPayload = ReliableUdpHelper.WrapPayload(data.GetBuffer());
                    ToPeerReliableUdp.Send(wrappedPayload, wrappedPayload.Length);
                }
                else
                {
                    NexumToPeer(data);
                }

                return;
            }

            if (reliable)
            {
                var reliableRelay = new NetMessage();
                reliableRelay.WriteEnum(MessageType.ReliableRelay1);
                reliableRelay.WriteScalar(1);
                reliableRelay.Write(HostId);
                reliableRelay.Write(SelfFrameNumber);
                SelfFrameNumber++;

                var wrappedData = new NetMessage();
                wrappedData.Write(Constants.TcpSplitter);
                wrappedData.Write((ByteArray)data);

                reliableRelay.Write((ByteArray)wrappedData);

                Owner.NexumToServer(reliableRelay);
            }
            else
            {
                var unreliableRelay = new NetMessage();
                unreliableRelay.WriteEnum(MessageType.UnreliableRelay1);
                unreliableRelay.WriteEnum(MessagePriority.Ring0);
                unreliableRelay.WriteScalar(0);
                unreliableRelay.WriteScalar(1);
                unreliableRelay.Write(HostId);
                unreliableRelay.Write((ByteArray)data);

                Owner.NexumToServerUdpIfAvailable(unreliableRelay);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void NexumToPeer(NetMessage data, IPEndPoint endPoint = null)
        {
            lock (Owner.SendLock)
            {
                ToPeer(data, endPoint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ToPeer(NetMessage message, IPEndPoint endPoint = null)
        {
            ToPeer(message.GetBuffer(), endPoint);
        }

        private void ToPeer(byte[] data, IPEndPoint endPoint = null)
        {
            var dest = PeerLocalToRemoteSocket ?? endPoint;
            var channel = PeerUdpChannel;
            if (channel == null || !channel.Active || dest == null)
            {
                Logger.Verbose("P2P UDP Channel not ready for hostId = {HostId}, packet dropped", HostId);
                return;
            }

            foreach (var udpMessage in UdpFragBoard.FragmentPacket(data, data.Length, Owner.HostId, HostId))
            {
                udpMessage.EndPoint = dest;
                channel.WriteAndFlushAsync(udpMessage);
            }
        }

        internal void Close()
        {
            Logger.Information("Closing P2P connection to hostId = {HostId}", HostId);
            IsClosed = true;
            DirectP2P = false;
            P2PHolepunchInitiated = false;
            P2PHolepunchNotified = false;
            P2PHolepunchStarted = false;

            if (ToPeerReliableUdp != null)
            {
                ToPeerReliableUdp.OnFailed -= OnReliableUdpFailed;
                ToPeerReliableUdp = null;
            }

            PeerUdpChannel?.CloseAsync();
            PeerUdpChannel = null;
            PeerUdpEventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            PeerUdpEventLoopGroup = null;
        }
    }
}
