using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using BaseLib.Extensions;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nexum.Core;
using Serilog;
using Constants = Serilog.Core.Constants;
using BurstDuplicateLogger = Nexum.Core.Logging.BurstDuplicateLogger;

namespace Nexum.Server
{
    internal readonly struct P2PRecycleInfo
    {
        internal P2PRecycleInfo(IPEndPoint sendAddr, IPEndPoint recvAddr, DateTimeOffset timestamp)
        {
            SendAddr = sendAddr;
            RecvAddr = recvAddr;
            Timestamp = timestamp;
        }

        internal IPEndPoint SendAddr { get; }
        internal IPEndPoint RecvAddr { get; }
        internal DateTimeOffset Timestamp { get; }
    }

    public class NetSession : IDisposable
    {
        private readonly object _stateLock = new object();

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        internal readonly ConcurrentDictionary<uint, int> LastSuccessfulP2PLocalPorts =
            new ConcurrentDictionary<uint, int>();

        internal readonly ConcurrentDictionary<uint, P2PRecycleInfo> LastSuccessfulP2PRecycleInfos =
            new ConcurrentDictionary<uint, P2PRecycleInfo>();

        internal readonly ConcurrentQueue<PendingPeerHolepunchRequest> PendingPeerHolepunchRequests =
            new ConcurrentQueue<PendingPeerHolepunchRequest>();

        private ConnectionState _connectionState = ConnectionState.Disconnected;

        private EventLoopScheduler _reliableUdpScheduler;

        internal volatile bool IsDisposed;

        internal NetSession(NetServer server, uint hostId, IChannel channel)
        {
            Server = server;
            HostId = hostId;
            Channel = (ISocketChannel)channel;
            var remoteEndPoint = (IPEndPoint)Channel.RemoteAddress;
            RemoteEndPoint = remoteEndPoint.ToIPv4EndPoint();

            var localEndPoint = (IPEndPoint)Channel.LocalAddress;
            LocalEndPoint = localEndPoint.ToIPv4EndPoint();

            UdpDefragBoard = new UdpPacketDefragBoard { LocalHostId = (uint)Core.HostId.Server };

            UdpFragBoard.DefragBoard = UdpDefragBoard;

            Logger = BurstDuplicateLogger.WrapForNexumHolepunching(
                Log.ForContext("HostId", HostId)
                    .ForContext("EndPoint", RemoteEndPoint.Address.ToString())
                    .ForContext(Constants.SourceContextPropertyName, $"{server.ServerName}Session({HostId})"));

            CreatedTime = GetAbsoluteTime();
        }

        public uint HostId { get; }

        internal NetServer Server { get; }

        public IPEndPoint LocalEndPoint { get; }

        public IPEndPoint RemoteEndPoint { get; }

        public P2PGroup P2PGroup { get; internal set; }

        public IPEndPoint UdpEndPoint { get; private set; }

        public IPEndPoint UdpLocalEndPoint { get; private set; }

        public bool IsConnected => Channel != null && Channel.Open && Channel.Active;

        public ILogger Logger { get; }

        internal object UdpHolepunchLock { get; } = new object();

        internal object UdpInitLock { get; } = new object();

        internal ISocketChannel Channel { get; set; }

        internal NetCrypt Crypt { get; set; }

        internal Guid HolepunchMagicNumber { get; set; }

        internal DateTimeOffset LastUdpPing { get; set; }

        internal DateTimeOffset LastUdpSetupAttempt { get; set; }

        internal ReliableUdpHost ToClientReliableUdp { get; set; }

        internal UdpPacketFragBoard UdpFragBoard { get; } = new UdpPacketFragBoard();

        internal UdpPacketDefragBoard UdpDefragBoard { get; }

        public double Ping => ClientUdpRecentPing;

        public double Jitter => ClientUdpJitter;

        internal double ClientUdpLastPing { get; set; }

        internal double ClientUdpRecentPing { get; set; }

        internal double ClientUdpJitter { get; set; }

        internal bool UdpEnabled { get; set; }

        internal bool UdpSessionInitialized { get; set; }

        internal UdpSocket UdpSocket { get; set; }

        internal uint UdpRetryCount { get; set; }

        internal double CreatedTime { get; }

        internal bool ConnectTimeoutSent { get; set; }

        internal IPEndPoint UdpEndPointInternal
        {
            set => UdpEndPoint = value;
        }

        internal IPEndPoint UdpLocalEndPointInternal
        {
            set => UdpLocalEndPoint = value;
        }

        public ConnectionState ConnectionState
        {
            get
            {
                lock (_stateLock)
                {
                    return _connectionState;
                }
            }
        }

        public void Dispose()
        {
            IsDisposed = true;
            SetConnectionState(ConnectionState.Disconnected);
            Logger.Debug("Session disposed for hostId = {HostId}", HostId);
            P2PGroup?.Leave(this);

            _reliableUdpScheduler?.Stop();
            _reliableUdpScheduler = null;

            if (ToClientReliableUdp != null)
            {
                ToClientReliableUdp.OnFailed -= OnToClientReliableUdpFailed;
                ToClientReliableUdp = null;
            }

            Crypt?.Dispose();
            Crypt = null;

            Channel?.CloseAsync();
            Channel = null;
        }

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        public double GetAbsoluteTime()
        {
            return _stopwatch.Elapsed.TotalSeconds;
        }

        public void RmiToClient(ushort rmiId, NetMessage message, EncryptMode ecMode = EncryptMode.Secure)
        {
            var data = new NetMessage();
            data.Reliable = true;
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;
            if (message.Compress)
                data.Compress = true;

            data.Write(MessageType.RMI);
            data.Write(rmiId);
            data.Write(message);
            NexumToClient(data);
        }

        internal void NexumToClient(NetMessage data)
        {
            if (IsDisposed)
                return;

            if (data.Compress)
                data = NetZip.CompressPacket(data);
            if (data.Encrypt && Crypt != null)
                data = Crypt.CreateEncryptedMessage(data);

            var message = new NetMessage();
            message.Write((ByteArray)data);
            ToClient(message);
        }

        public void RmiToClientUdpIfAvailable(ushort rmiId, NetMessage message, EncryptMode ecMode = EncryptMode.Fast,
            bool reliable = false)
        {
            var data = new NetMessage();
            data.Reliable = reliable;
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;
            if (message.Compress)
                data.Compress = true;

            data.Write(MessageType.RMI);
            data.Write(rmiId);
            data.Write(message);

            NexumToClientUdpIfAvailable(data, reliable: reliable);
        }

        internal void NexumToClientUdpIfAvailable(NetMessage data, bool force = false, bool reliable = false)
        {
            if (IsDisposed)
                return;

            data.Reliable = reliable;
            if (data.Compress)
                data = NetZip.CompressPacket(data);
            if (data.Encrypt && Crypt != null)
                data = Crypt.CreateEncryptedMessage(data);

            if (UdpEnabled || force)
            {
                if (reliable && ToClientReliableUdp != null)
                {
                    byte[] wrappedData = ReliableUdpHelper.WrapPayload(data.GetBuffer());
                    ToClientReliableUdp.Send(wrappedData, wrappedData.Length);
                }
                else
                {
                    ToClientUdp(data);
                }
            }
            else
            {
                NexumToClient(data);
            }
        }

        internal void SetConnectionState(ConnectionState newState)
        {
            ConnectionState previousState;
            lock (_stateLock)
            {
                if (_connectionState == newState)
                    return;

                previousState = _connectionState;
                _connectionState = newState;
            }

            Logger.Debug("Connection state changed: {PreviousState} -> {NewState}", previousState, newState);

            var args = new ConnectionStateChangedEventArgs(previousState, newState);
            ConnectionStateChanged?.Invoke(this, args);

            Server?.OnSessionConnectionStateChanged(this, previousState, newState);
        }

        internal void InitializeToClientReliableUdp(uint firstFrameNumber)
        {
            if (ToClientReliableUdp != null)
                return;

            ToClientReliableUdp = new ReliableUdpHost(firstFrameNumber)
            {
                GetAbsoluteTime = GetAbsoluteTime,
                GetRecentPing = () => ClientUdpRecentPing > 0 ? ClientUdpRecentPing : 0.05,
                SendOneFrameToUdpLayer = SendReliableUdpFrameToClient,
                IsReliableChannel = () => false
            };

            ToClientReliableUdp.OnFailed += OnToClientReliableUdpFailed;
            Logger.Debug("Client reliable UDP initialized with firstFrameNumber = {FirstFrameNumber}",
                firstFrameNumber);

            StartReliableUdpScheduler();
        }

        internal void ResetToClientReliableUdp()
        {
            _reliableUdpScheduler?.Stop();
            _reliableUdpScheduler = null;

            if (ToClientReliableUdp != null)
            {
                ToClientReliableUdp.OnFailed -= OnToClientReliableUdpFailed;
                ToClientReliableUdp = null;
            }
        }

        internal void ResetUdp()
        {
            UdpEnabled = false;
            UdpSessionInitialized = false;
            UdpEndPointInternal = null;
            UdpLocalEndPointInternal = null;
            UdpRetryCount = 0;
            ClientUdpLastPing = 0;
            ClientUdpRecentPing = 0;
            ClientUdpJitter = 0;
            ResetToClientReliableUdp();
        }

        internal void StartReliableUdpScheduler()
        {
            _reliableUdpScheduler = EventLoopScheduler.StartIfNeeded(
                _reliableUdpScheduler,
                TimeSpan.FromMilliseconds(ReliableUdpConfig.FrameMoveInterval * 1000),
                ReliableUdpFrameMove,
                Channel?.EventLoop);
        }

        internal void ReliableUdpFrameMove(double elapsedTime)
        {
            if (IsDisposed)
                return;

            double currentTime = GetAbsoluteTime();
            ToClientReliableUdp?.FrameMove(elapsedTime);
            UdpDefragBoard.PruneStalePackets(currentTime);
        }

        private void ToClient(NetMessage message)
        {
            var buffer = Unpooled.WrappedBuffer(message.GetBufferUnsafe(), 0, message.Length);
            Channel.WriteAndFlushAsync(buffer);
        }

        private void ToClientUdp(NetMessage message)
        {
            var channel = UdpSocket?.Channel;
            if (channel == null || !channel.Active || UdpEndPoint == null)
            {
                Logger.Verbose("UDP Channel not ready for hostId = {HostId}, packet dropped", HostId);
                return;
            }

            foreach (var udpMessage in
                     UdpFragBoard.FragmentPacket(message, (uint)Core.HostId.Server, HostId))
            {
                udpMessage.EndPoint = UdpEndPoint;
                channel.WriteAndFlushAsync(udpMessage);
            }
        }

        private void SendReliableUdpFrameToClient(ReliableUdpFrame frame)
        {
            var msg = ReliableUdpHelper.BuildFrameMessage(frame);
            ToClientUdp(msg);
        }

        private void OnToClientReliableUdpFailed()
        {
            Logger.Warning(
                "ToClientReliableUdp failed after max retries, falling back to TCP - retry will be triggered by server");

            ResetUdp();

            RmiToClient((ushort)NexumOpCode.NotifyUdpToTcpFallbackByServer, new NetMessage());
        }
    }
}
