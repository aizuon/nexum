using System;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BaseLib.Extensions;
using DotNetty.Transport.Channels;
using Nexum.Core;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Client
{
    public class P2PMember
    {
        internal readonly ILogger Logger;
        internal readonly MtuDiscovery MtuDiscovery = new MtuDiscovery();
        internal readonly NetClient Owner;

        public readonly SemaphoreSlim P2PMutex = new SemaphoreSlim(1, 1);
        internal readonly UdpPacketDefragBoard UdpDefragBoard = new UdpPacketDefragBoard();
        internal readonly UdpPacketFragBoard UdpFragBoard = new UdpPacketFragBoard();

        private double _nextPingTime;
        private double _nextTimeSyncTime;
        private EventLoopScheduler _reliableUdpScheduler;

        internal bool EnableDirectP2P = true;

        internal double IndirectServerTimeDiff;

        internal bool IsClosed;

        internal bool JitDirectP2PTriggerSent;

        internal double JitterInternal;
        internal double LastPeerServerPing;
        internal double LastPingInternal;
        internal double LastUdpReceivedTime;

        internal bool LocalPortReuseSuccess;
        internal bool P2PHolepunchInitiated;
        internal bool P2PHolepunchNotified;
        internal bool P2PHolepunchStarted;

        internal int PeerBindPort;

        internal NetCrypt PeerCrypt;

        internal uint PeerFirstFrameNumber;

        internal double PeerFrameRateInternal;

        internal IPEndPoint PeerLocalToRemoteSocket;
        internal IPEndPoint PeerRemoteToLocalSocket;
        internal double PeerServerJitterInternal;
        internal double PeerServerPingInternal;

        internal IChannel PeerUdpChannel;
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
            Logger = Log.ForContext("HostId", HostId).ForContext(Constants.SourceContextPropertyName,
                $"{owner.ServerName}{nameof(P2PMember)}({HostId})");

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

        public void RmiToPeer(ushort rmiId, NetMessage message, EncryptMode ecMode = EncryptMode.Fast,
            bool forceRelay = false, bool reliable = false)
        {
            TryTriggerJitDirectP2PIfNeeded(forceRelay);

            var data = new NetMessage();
            data.Reliable = reliable;
            if (ecMode != EncryptMode.None)
                data.EncryptMode = ecMode;
            if (message.Compress)
                data.Compress = true;

            data.Write(MessageType.RMI);
            data.Write(rmiId);
            data.Write(message);

            if (DirectP2P && !forceRelay)
            {
                if (data.Compress)
                    data = NetZip.CompressPacket(data);
                if (data.Encrypt && PeerCrypt != null)
                    data = PeerCrypt.CreateEncryptedMessage(data);

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
                reliableRelay.Reliable = true;
                if (ecMode != EncryptMode.None)
                    reliableRelay.EncryptMode = ecMode;
                if (message.Compress)
                    reliableRelay.Compress = true;

                reliableRelay.Write(MessageType.ReliableRelay1);
                reliableRelay.WriteScalar(1);
                reliableRelay.Write(HostId);
                reliableRelay.Write(SelfFrameNumber);
                SelfFrameNumber++;

                var wrappedData = new NetMessage();
                wrappedData.Write(Core.Constants.TcpSplitter);
                wrappedData.Write((ByteArray)data);

                reliableRelay.Write((ByteArray)wrappedData);

                Owner.NexumToServer(reliableRelay);
            }
            else
            {
                var unreliableRelay = new NetMessage();
                unreliableRelay.Reliable = false;
                if (ecMode != EncryptMode.None)
                    unreliableRelay.EncryptMode = ecMode;
                if (message.Compress)
                    unreliableRelay.Compress = true;

                unreliableRelay.Write(MessageType.UnreliableRelay1);
                unreliableRelay.Write(MessagePriority.Ring0);
                unreliableRelay.WriteScalar(0);
                unreliableRelay.WriteScalar(1);
                unreliableRelay.Write(HostId);
                unreliableRelay.Write((ByteArray)data);

                Owner.NexumToServerUdpIfAvailable(unreliableRelay);
            }
        }

        private void TryTriggerJitDirectP2PIfNeeded(bool forceRelay)
        {
            if (forceRelay)
                return;

            if (!EnableDirectP2P)
                return;

            if (DirectP2P)
                return;

            if (Owner.NetSettings.DirectP2PStartCondition != DirectP2PStartCondition.Jit)
                return;

            Task.Run(async () =>
            {
                await using (await P2PMutex.EnterAsync())
                {
                    if (IsClosed || DirectP2P)
                        return;

                    if (JitDirectP2PTriggerSent)
                        return;

                    if (PeerUdpChannel == null && SelfUdpLocalSocket != null)
                    {
                        int? targetPort = SelfUdpLocalSocket.Port;
                        (var channel, int port, _) = await Owner.ConnectUdpAsync(targetPort);
                        PeerUdpChannel = channel;
                        SelfUdpLocalSocket = new IPEndPoint(Owner.LocalIP, port);
                    }

                    JitDirectP2PTriggerSent = true;

                    var notifyJitDirectP2PTriggered = new NetMessage();
                    notifyJitDirectP2PTriggered.Write(HostId);
                    Owner.RmiToServer((ushort)NexumOpCode.NotifyJitDirectP2PTriggered, notifyJitDirectP2PTriggered);
                }
            });
        }

        internal void InitializeReliableUdp(uint senderFirstFrameNumber, uint receiverExpectedFrameNumber)
        {
            PeerFirstFrameNumber = receiverExpectedFrameNumber;

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

        internal void ReinitializeReliableUdp()
        {
            if (ToPeerReliableUdp != null)
            {
                ToPeerReliableUdp.OnFailed -= OnReliableUdpFailed;
                ToPeerReliableUdp = null;
            }

            ToPeerReliableUdp = new ReliableUdpHost(Owner.P2PFirstFrameNumber, PeerFirstFrameNumber)
            {
                GetAbsoluteTime = () => Owner.GetAbsoluteTime(),
                GetRecentPing = () => RecentPing > 0 ? RecentPing : 0.1,
                GetUdpSendBufferPacketFilledCount = () => 0,
                IsReliableChannel = () => !DirectP2P,
                SendOneFrameToUdpLayer = SendReliableUdpFrame
            };

            ToPeerReliableUdp.OnFailed += OnReliableUdpFailed;
            Logger.Debug("Reinitialized reliable UDP for peer {HostId}", HostId);
        }

        internal void StartReliableUdpScheduler()
        {
            _reliableUdpScheduler = EventLoopScheduler.StartIfNeeded(
                _reliableUdpScheduler,
                TimeSpan.FromMilliseconds(ReliableUdpConfig.FrameMoveInterval * 1000),
                ReliableUdpFrameMove,
                PeerUdpChannel?.EventLoop);
        }

        internal void HandleRemoteDisconnect()
        {
            Logger.Debug("Remote peer {HostId} disconnected, falling back to relay", HostId);
            FallbackP2PToRelay(false);
        }

        internal void ProcessReceivedReliableUdpFrame(ReliableUdpFrame frame)
        {
            ToPeerReliableUdp?.TakeReceivedFrame(frame);
        }

        internal void ReliableUdpFrameMove(double elapsedTime)
        {
            if (IsClosed)
                return;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void NexumToPeer(NetMessage data, IPEndPoint endPoint = null)
        {
            ToPeer(data, endPoint);
        }

        internal void Close()
        {
            Close(false);
        }

        internal void Close(bool shouldRecycleSocket)
        {
            Logger.Debug("Closing P2P connection to hostId = {HostId}, shouldRecycleSocket = {ShouldRecycle}",
                HostId, shouldRecycleSocket);

            bool wasDirectP2P = DirectP2P;
            int localPort = SelfUdpLocalSocket?.Port ?? 0;

            IsClosed = true;
            DirectP2P = false;
            P2PHolepunchInitiated = false;
            P2PHolepunchNotified = false;
            P2PHolepunchStarted = false;
            JitDirectP2PTriggerSent = false;
            LastUdpReceivedTime = 0;

            _reliableUdpScheduler?.Stop();
            _reliableUdpScheduler = null;

            if (ToPeerReliableUdp != null)
            {
                ToPeerReliableUdp.OnFailed -= OnReliableUdpFailed;
                ToPeerReliableUdp = null;
            }

            if (shouldRecycleSocket && wasDirectP2P && PeerUdpChannel != null && PeerUdpChannel.Active && localPort > 0)
            {
                Logger.Debug("Recycling P2P UDP socket on port {Port} for hostId = {HostId}", localPort, HostId);
                Owner.RecycleUdpSocket(PeerUdpChannel, localPort);
            }
            else
            {
                PeerUdpChannel?.CloseAsync();
            }

            PeerUdpChannel = null;

            PeerLocalToRemoteSocket = null;
            PeerRemoteToLocalSocket = null;
        }

        private void SendReliableUdpFrame(ReliableUdpFrame frame)
        {
            var msg = ReliableUdpHelper.BuildFrameMessage(frame);
            NexumToPeer(msg);
        }

        private void OnReliableUdpFailed()
        {
            Logger.Warning("Reliable UDP to peer {HostId} failed, falling back to relay and requesting re-holepunch",
                HostId);

            FallbackP2PToRelay();
        }

        private void SendTimeSyncPing()
        {
            var reportServerTimeAndFrameRatePing = new NetMessage();
            reportServerTimeAndFrameRatePing.Write(Owner.GetAbsoluteTime());
            reportServerTimeAndFrameRatePing.Write(Owner.RecentFrameRate);

            RmiToPeer((ushort)NexumOpCode.ReportServerTimeAndFrameRateAndPing, reportServerTimeAndFrameRatePing,
                reliable: true);
        }

        private void FallbackP2PToRelay(bool firstChance = true)
        {
            using (P2PMutex.Enter())
            {
                if (!DirectP2P)
                    return;

                Close();
                IsClosed = false;
            }

            Owner.OnP2PMemberDirectDisconnected(HostId);
            Owner.OnP2PMemberRelayConnected(HostId);

            if (firstChance)
            {
                var notifyDirectP2PDisconnected = new NetMessage();
                notifyDirectP2PDisconnected.Write(HostId);
                notifyDirectP2PDisconnected.Write(ErrorType.P2PUdpFailed);
                Owner.RmiToServer((ushort)NexumOpCode.P2P_NotifyDirectP2PDisconnected, notifyDirectP2PDisconnected);
            }

            var notifyJitDirectP2PTriggered = new NetMessage();
            notifyJitDirectP2PTriggered.Write(HostId);
            Owner.RmiToServer((ushort)NexumOpCode.NotifyJitDirectP2PTriggered, notifyJitDirectP2PTriggered);
        }

        private void SendP2PPing()
        {
            double currentTime = Owner.GetAbsoluteTime();
            int paddingSize = MtuDiscovery.GetProbePaddingSize(currentTime);

            var p2pRequestIndirectServerTimeAndPing = new NetMessage();
            p2pRequestIndirectServerTimeAndPing.Write(MessageType.P2PRequestIndirectServerTimeAndPing);
            p2pRequestIndirectServerTimeAndPing.Write(currentTime);

            if (paddingSize > 0)
            {
                p2pRequestIndirectServerTimeAndPing.Write(paddingSize);
                p2pRequestIndirectServerTimeAndPing.WriteZeroes(paddingSize);
            }
            else
            {
                p2pRequestIndirectServerTimeAndPing.Write(0);
            }

            NexumToPeer(p2pRequestIndirectServerTimeAndPing);
        }

        private void ToPeer(NetMessage message, IPEndPoint endPoint = null)
        {
            var dest = PeerLocalToRemoteSocket ?? endPoint;
            var channel = PeerUdpChannel;
            if (channel == null || !channel.Active || dest == null)
            {
                Logger.Verbose("P2P UDP Channel not ready for hostId = {HostId}, packet dropped", HostId);
                return;
            }

            foreach (var udpMessage in UdpFragBoard.FragmentPacket(message, Owner.HostId, HostId))
            {
                udpMessage.EndPoint = dest;
                channel.WriteAndFlushAsync(udpMessage);
            }
        }
    }
}
