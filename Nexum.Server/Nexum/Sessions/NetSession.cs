using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using BaseLib.Extensions;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nexum.Core.Configuration;
using Nexum.Core.Crypto;
using Nexum.Core.Events;
using Nexum.Core.Message.X2X;
using Nexum.Core.ReliableUdp;
using Nexum.Core.Rmi.S2C;
using Nexum.Core.Routing;
using Nexum.Core.Serialization;
using Nexum.Core.Udp;
using Nexum.Core.Utilities;
using Nexum.Server.Core;
using Nexum.Server.P2P;
using Nexum.Server.Udp;
using Serilog;
using SerilogConstants = Serilog.Core.Constants;
using BurstDuplicateLogger = Nexum.Core.Logging.BurstDuplicateLogger;

namespace Nexum.Server.Sessions
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


            Logger = BurstDuplicateLogger.WrapForNexumHolepunching(
                Log.ForContext("HostId", HostId)
                    .ForContext("EndPoint", RemoteEndPoint.Address.ToString())
                    .ForContext(SerilogConstants.SourceContextPropertyName, $"{server.ServerName}Session({HostId})"));

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

        internal ReliableUdpHost ClientReliableUdp { get; set; }


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

            if (ClientReliableUdp != null)
            {
                ClientReliableUdp.OnFailed -= OnClientReliableUdpFailed;
                ClientReliableUdp = null;
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
            var data = new RmiMessage { RmiId = rmiId, Data = message }.Serialize();
            data.Reliable = true;
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;
            if (message.Compress)
                data.Compress = true;

            NexumToClient(data);
        }

        private void RmiToClient(NetMessage message, EncryptMode ecMode = EncryptMode.Secure)
        {
            message.Reliable = true;
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;

            NexumToClient(message);
        }

        public void RmiToClient(INetRmi rmi, EncryptMode ecMode = EncryptMode.Secure)
        {
            RmiToClient(rmi.Serialize(), ecMode);
        }

        public void NexumToClient(INetCoreMessage msg)
        {
            NexumToClient(msg.Serialize());
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
            var data = new RmiMessage { RmiId = rmiId, Data = message }.Serialize();
            data.Reliable = reliable;
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;
            if (message.Compress)
                data.Compress = true;

            NexumToClientUdpIfAvailable(data, reliable: reliable);
        }

        private void RmiToClientUdpIfAvailable(NetMessage message, EncryptMode ecMode = EncryptMode.Fast,
            bool reliable = false)
        {
            message.Reliable = reliable;
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;

            NexumToClientUdpIfAvailable(message, reliable: reliable);
        }

        public void RmiToClientUdpIfAvailable(INetRmi rmi, EncryptMode ecMode = EncryptMode.Fast,
            bool reliable = false)
        {
            RmiToClientUdpIfAvailable(rmi.Serialize(), ecMode, reliable);
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
                if (reliable && ClientReliableUdp != null)
                {
                    byte[] wrappedData = ReliableUdpHelper.WrapPayload(data.GetBufferSpan());
                    ClientReliableUdp.Send(wrappedData, wrappedData.Length);
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

        internal void InitializeClientReliableUdp(uint firstFrameNumber)
        {
            if (ClientReliableUdp != null)
                return;

            ClientReliableUdp = new ReliableUdpHost(firstFrameNumber)
            {
                GetAbsoluteTime = GetAbsoluteTime,
                GetRecentPing = () => ClientUdpRecentPing > 0 ? ClientUdpRecentPing : 0.05,
                SendOneFrameToUdpLayer = ToClientReliableUdp,
                IsReliableChannel = () => false
            };

            ClientReliableUdp.OnFailed += OnClientReliableUdpFailed;
            Logger.Debug("Client reliable UDP initialized with firstFrameNumber = {FirstFrameNumber}",
                firstFrameNumber);

            StartReliableUdpScheduler();
        }

        internal void ResetClientReliableUdp()
        {
            _reliableUdpScheduler?.Stop();
            _reliableUdpScheduler = null;

            if (ClientReliableUdp != null)
            {
                ClientReliableUdp.OnFailed -= OnClientReliableUdpFailed;
                ClientReliableUdp = null;
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
            ResetClientReliableUdp();
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

            ClientReliableUdp?.FrameMove(elapsedTime);
        }

        private void ToClient(NetMessage message)
        {
            var buffer = Unpooled.WrappedBuffer(message.GetBufferUnsafe(), 0, message.Length);
            Channel.WriteAndFlushAsync(buffer);
        }

        private void ToClientUdp(NetMessage message)
        {
            if (!TryGetClientUdpChannel(out var channel))
            {
                Logger.Verbose("UDP Channel not ready for hostId = {HostId}, packet dropped", HostId);
                return;
            }

            var packet = new OutboundUdpPacket
            {
                Content = Unpooled.WrappedBuffer(message.GetBufferUnsafe(), 0, message.Length),
                EndPoint = UdpEndPoint,
                FilterTag = FilterTag.Create((uint)Nexum.Core.Routing.HostId.Server, HostId)
            };
            channel.WriteAndFlushAsync(packet);
        }

        private void ToClientReliableUdp(ReliableUdpFrame frame)
        {
            if (!TryGetClientUdpChannel(out var channel))
            {
                Logger.Verbose("UDP Channel not ready for hostId = {HostId}, reliable frame dropped", HostId);
                return;
            }

            var outboundFrame = new OutboundReliableUdpFrame
            {
                Frame = frame,
                DestEndPoint = UdpEndPoint,
                FilterTag = FilterTag.Create((uint)Nexum.Core.Routing.HostId.Server, HostId)
            };
            channel.WriteAndFlushAsync(outboundFrame);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetClientUdpChannel(out IChannel channel)
        {
            channel = UdpSocket?.Channel;
            return channel != null && channel.Active && UdpEndPoint != null;
        }

        private void OnClientReliableUdpFailed()
        {
            Logger.Warning(
                "ClientReliableUdp failed after max retries, falling back to TCP - retry will be triggered by server");

            ResetUdp();

            RmiToClient(new NotifyUdpToTcpFallbackByServer());
        }
    }
}
