using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BaseLib;
using BaseLib.Extensions;
using DotNetty.Buffers;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nexum.Core;
using Nexum.Core.Simulation;
using NexumCore.DotNetty.Codecs;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Client
{
    public class NetClient : NetCore
    {
        public delegate void OnConnectedDelegate();

        public delegate void OnConnectingDelegate();

        public delegate void OnConnectionCompleteDelegate();

        public delegate void OnDisconnectedDelegate();

        public delegate void OnHandshakingDelegate();

        public delegate void OnRMIReceiveDelegate(NetMessage message, ushort rmiId);

        internal static readonly ConcurrentDictionary<ServerType, NetClient> Clients =
            new ConcurrentDictionary<ServerType, NetClient>();

        private readonly object _stateLock = new object();

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        internal readonly ConcurrentDictionary<uint, byte> PendingP2PConnections =
            new ConcurrentDictionary<uint, byte>();

        internal readonly object RecvLock = new object();
        internal readonly object SendLock = new object();

        internal readonly object UdpHolepunchLock = new object();
        private ConnectionState _connectionState = ConnectionState.Disconnected;

        private IEventLoopGroup _eventLoopGroup;

        internal ushort AimForPort = NetConfig.UdpAimForPort;

        internal NetCrypt Crypt;

        public OnConnectedDelegate OnConnected = () => { };

        public OnConnectingDelegate OnConnecting = () => { };
        public OnConnectionCompleteDelegate OnConnectionComplete = () => { };

        public OnDisconnectedDelegate OnDisconnected = () => { };

        public OnHandshakingDelegate OnHandshaking = () => { };
        public OnRMIReceiveDelegate OnRMIReceive = (message, rmiId) => { };

        internal uint P2PFirstFrameNumber;
        internal Guid PeerUdpMagicNumber;

        internal ThreadLoop PingLoop;
        internal ThreadLoop ReliableUdpLoop;
        internal IPEndPoint SelfUdpSocket;

        internal Guid ServerGuid;
        internal MtuDiscovery ServerMtuDiscovery;
        internal double ServerTimeDiff;
        internal int ServerUdpFallbackCount;

        internal double ServerUdpLastPing;
        internal double ServerUdpLastReceivedTime;

        internal bool ServerUdpReadyWaiting;
        internal double ServerUdpRecentPing;
        internal IPEndPoint ServerUdpSocket;
        internal bool ServerUdpSocketFailed;

        internal ReliableUdpHost ToServerReliableUdp;

        internal IChannel UdpChannel;
        internal UdpPacketDefragBoard UdpDefragBoard;
        internal IEventLoopGroup UdpEventLoopGroup;
        internal UdpPacketFragBoard UdpFragBoard;

        internal Guid UdpMagicNumber;

        public NetClient(ServerType serverType)
        {
            ServerType = serverType;
            Logger = Log.ForContext(Constants.SourceContextPropertyName, ServerType + "Client");

            ServerMtuDiscovery = new MtuDiscovery();
            UdpFragBoard = new UdpPacketFragBoard { MtuDiscovery = ServerMtuDiscovery };
            UdpDefragBoard = new UdpPacketDefragBoard();

            if (ServerType == ServerType.Relay)
                P2PGroup = new P2PGroup();

            Clients.TryAdd(ServerType, this);
        }

        public static Action<IChannelPipeline> UdpPipelineConfigurator { get; set; }


        public NetworkProfile NetworkSimulationProfile { get; set; }

        internal byte[] PinnedServerPublicKey { get; private set; }

        public uint HostId { get; internal set; }

        public P2PGroup P2PGroup { get; internal set; }

        public bool UdpEnabled { get; internal set; }

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

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        public void SetPinnedServerPublicKey(byte[] derEncodedKey)
        {
            if (derEncodedKey == null || derEncodedKey.Length == 0)
                throw new ArgumentNullException(nameof(derEncodedKey));

            PinnedServerPublicKey = derEncodedKey;
            Logger.Information("Server public key pinned for certificate validation");
        }

        public void SetPinnedServerPublicKey(string base64Key)
        {
            if (string.IsNullOrWhiteSpace(base64Key))
                throw new ArgumentNullException(nameof(base64Key));

            PinnedServerPublicKey = Convert.FromBase64String(base64Key);
            Logger.Information("Server public key pinned for certificate validation");
        }

        public void ClearPinnedServerPublicKey()
        {
            PinnedServerPublicKey = null;
            Logger.Information("Server public key pinning disabled");
        }

        internal bool ValidateServerPublicKey(byte[] serverPublicKey)
        {
            if (PinnedServerPublicKey == null)
                return true;

            if (serverPublicKey == null)
                return false;

            if (PinnedServerPublicKey.Length != serverPublicKey.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < PinnedServerPublicKey.Length; i++)
                diff |= PinnedServerPublicKey[i] ^ serverPublicKey[i];

            return diff == 0;
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

            Logger.Information("Connection state changed: {PreviousState} -> {NewState}", previousState, newState);

            var args = new ConnectionStateChangedEventArgs(previousState, newState);
            ConnectionStateChanged?.Invoke(this, args);

            switch (newState)
            {
                case ConnectionState.Connecting:
                    OnConnecting();
                    break;
                case ConnectionState.Handshaking:
                    OnHandshaking();
                    break;
                case ConnectionState.Connected:
                    OnConnected();
                    break;
                case ConnectionState.Disconnected:
                    OnDisconnected();
                    break;
            }
        }

        public async Task ConnectAsync(IPEndPoint ipEndPoint)
        {
            SetConnectionState(ConnectionState.Connecting);
            Logger.Debug("Connecting to {Endpoint}", ipEndPoint.ToIPv4String());
            _eventLoopGroup = new MultithreadEventLoopGroup();
            Channel = await new Bootstrap()
                .Group(_eventLoopGroup)
                .Channel<TcpSocketChannel>()
                .Handler(new ActionChannelInitializer<ISocketChannel>(channel =>
                {
                    channel.Pipeline.AddLast(new NexumFrameDecoder(NetConfig.MessageMaxLength));
                    channel.Pipeline.AddLast(new NexumFrameEncoder());
                    channel.Pipeline.AddLast(new NetClientAdapter(this));
                }))
                .Option(ChannelOption.TcpNodelay, !NetConfig.EnableNagleAlgorithm)
                .Option(ChannelOption.SoRcvbuf, NetConfig.TcpIssueRecvLength)
                .Option(ChannelOption.SoSndbuf, NetConfig.TcpSendBufferLength)
                .Option(ChannelOption.AllowHalfClosure, false)
                .Option(ChannelOption.Allocator, UnpooledByteBufferAllocator.Default)
                .ConnectAsync(ipEndPoint);

            Logger.Information("TCP connection established to {Endpoint}", ipEndPoint.ToIPv4String());
        }

        internal (IChannel Channel, IEventLoopGroup WorkerGroup, int Port, bool PortReuseSuccess) ConnectUdp(
            int? targetPort = null)
        {
            var workerGroup = new SingleThreadEventLoop();
            int aimPort = targetPort ?? AimForPort;
            int port = NetUtil.GetAvailablePort(aimPort);
            bool portReuseSuccess = targetPort.HasValue && port == targetPort.Value;
            Logger.Debug("Allocating UDP port {Port}", port);
            AimForPort = (ushort)(port + 1);
            var channel = new Bootstrap()
                .Group(workerGroup)
                .Channel<SocketDatagramChannel>()
                .Handler(new ActionChannelInitializer<IChannel>(ch =>
                {
                    if (NetworkSimulationProfile != null && !NetworkSimulationProfile.IsIdeal)
                    {
                        var handler = new SimulatedUdpChannelHandler(NetworkSimulationProfile);
                        ch.Pipeline.AddFirst("network-simulation", handler);
                        NetworkSimulationStats.RegisterHandler(ch, handler);
                    }

                    UdpPipelineConfigurator?.Invoke(ch.Pipeline);

                    ch.Pipeline
                        .AddLast(new UdpFrameDecoder(NetConfig.MessageMaxLength))
                        .AddLast(new UdpFrameEncoder())
                        .AddLast(new UdpHandler(this));
                }))
                .Option(ChannelOption.SoRcvbuf, NetConfig.UdpIssueRecvLength)
                .Option(ChannelOption.SoSndbuf, NetConfig.UdpSendBufferLength)
                .Option(ChannelOption.Allocator, UnpooledByteBufferAllocator.Default)
                .BindAsync(new IPEndPoint(LocalIP, port))
                .GetAwaiter()
                .GetResult();

            Logger.Information("UDP socket bound on port {Port}", port);

            channel.CloseCompletion.ContinueWith(_ =>
            {
                Logger.Debug("UDP socket on port {Port} closed", port);
                NetUtil.ReleasePort(port);
            });

            return (channel, workerGroup, port, portReuseSuccess);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double GetAbsoluteTime()
        {
            return _stopwatch.Elapsed.TotalSeconds;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetServerTime()
        {
            return GetAbsoluteTime() - ServerTimeDiff;
        }

        internal void StartReliableUdpLoop()
        {
            if (ReliableUdpLoop != null)
                return;

            ReliableUdpLoop = new ThreadLoop(
                TimeSpan.FromMilliseconds(ReliableUdpConfig.FrameMoveInterval * 1000),
                ReliableUdpFrameMove);
            ReliableUdpLoop.Start();
        }

        internal void InitializeToServerReliableUdp(uint firstFrameNumber)
        {
            if (ToServerReliableUdp != null)
                return;

            ToServerReliableUdp = new ReliableUdpHost(firstFrameNumber)
            {
                GetAbsoluteTime = GetAbsoluteTime,
                GetRecentPing = () => ServerUdpRecentPing > 0 ? ServerUdpRecentPing : 0.05,
                SendOneFrameToUdpLayer = SendReliableUdpFrameToServer,
                IsReliableChannel = () => false
            };

            ToServerReliableUdp.OnFailed += OnToServerReliableUdpFailed;
            Logger.Information("Server reliable UDP initialized with firstFrameNumber = {FirstFrameNumber}",
                firstFrameNumber);
        }

        private void SendReliableUdpFrameToServer(ReliableUdpFrame frame)
        {
            var msg = ReliableUdpHelper.BuildFrameMessage(frame);
            ToServerUdp(msg);
        }

        private void OnToServerReliableUdpFailed()
        {
            Logger.Warning(
                "ToServerReliableUdp failed after max retries, falling back to TCP and requesting re-holepunch");

            ToServerReliableUdp.OnFailed -= OnToServerReliableUdpFailed;
            ToServerReliableUdp = null;

            UdpEnabled = false;

            Task.Run(() =>
            {
                CloseUdp();

                RmiToServer((ushort)NexumOpCode.C2S_RequestCreateUdpSocket, new NetMessage());
            });
        }

        internal void StopReliableUdpLoop()
        {
            ReliableUdpLoop?.Stop();
            ReliableUdpLoop = null;
        }

        private void ReliableUdpFrameMove(TimeSpan delta)
        {
            double elapsedTime = delta.TotalSeconds;
            double currentTime = GetAbsoluteTime();

            ToServerReliableUdp?.FrameMove(elapsedTime);
            UdpDefragBoard.PruneStalePackets(currentTime);

            if (P2PGroup != null)
                foreach (var member in P2PGroup.P2PMembers.Values)
                    if (!member.IsClosed)
                        member.FrameMove(elapsedTime);
        }

        internal void SendUdpPing(TimeSpan delta)
        {
            double currentTime = GetAbsoluteTime();

            CheckServerUdpTimeout(currentTime);

            int paddingSize = ServerMtuDiscovery.GetProbePaddingSize(currentTime);

            var unreliablePing = new NetMessage();
            unreliablePing.WriteEnum(MessageType.UnreliablePing);
            unreliablePing.Write(currentTime);
            unreliablePing.Write(ServerUdpRecentPing);

            if (paddingSize > 0)
            {
                unreliablePing.Write(paddingSize);
                unreliablePing.WriteZeroes(paddingSize);
            }
            else
            {
                unreliablePing.Write(0);
            }

            NexumToServerUdpIfAvailable(unreliablePing, true);
        }

        private void CheckServerUdpTimeout(double currentTime)
        {
            if (!UdpEnabled || ServerUdpLastReceivedTime <= 0)
                return;

            double timeSinceLastUdp = currentTime - ServerUdpLastReceivedTime;
            if (timeSinceLastUdp > ReliableUdpConfig.FallbackServerUdpToTcpTimeout)
            {
                Logger.Warning(
                    "Server UDP timeout detected ({TimeSinceLastUdp:F1}s since last packet), falling back to TCP",
                    timeSinceLastUdp);

                FirstChanceFallbackServerUdpToTcp();
            }
        }

        private void FirstChanceFallbackServerUdpToTcp()
        {
            if (!UdpEnabled)
                return;

            UdpEnabled = false;

            Task.Run(() =>
            {
                CloseUdp();
                RmiToServer((ushort)NexumOpCode.NotifyUdpToTcpFallbackByClient, new NetMessage());

                if (ServerUdpFallbackCount < ReliableUdpConfig.ServerUdpRepunchMaxTrialCount)
                {
                    ServerUdpFallbackCount++;

                    RmiToServer((ushort)NexumOpCode.C2S_RequestCreateUdpSocket, new NetMessage());
                }
                else
                {
                    Logger.Warning("Server UDP max repunch attempts ({MaxAttempts}) reached, staying on TCP",
                        ReliableUdpConfig.ServerUdpRepunchMaxTrialCount);
                }
            });
        }

        public void RmiToServer(ushort rmiId, NetMessage message, EncryptMode ecMode = EncryptMode.Secure)
        {
            var data = new NetMessage();
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;

            data.WriteEnum(MessageType.RMI);
            data.Write(rmiId);
            data.Write(message);
            NexumToServer(data);
        }

        internal void NexumToServer(NetMessage data)
        {
            lock (SendLock)
            {
                if (data.Compress)
                    data = NetZip.CompressPacket(data);
                if (data.Encrypt)
                {
                    byte[] encryptedBuffer = Crypt.Encrypt(data.GetBuffer(), data.EncryptMode);
                    var netMessage = new NetMessage();
                    netMessage.WriteEnum(MessageType.Encrypted);
                    netMessage.WriteEnum(data.EncryptMode);
                    netMessage.Write(new ByteArray(encryptedBuffer));
                    data = netMessage;
                }

                var message = new NetMessage();
                message.Write((ByteArray)data);
                ToServer(message);
            }
        }

        private void ToServer(NetMessage message)
        {
            ToServer(message.GetBuffer());
        }

        private void ToServer(byte[] data)
        {
            var buffer = Unpooled.WrappedBuffer(data);
            Channel.WriteAndFlushAsync(buffer);
        }

        public void RmiToServerUdpIfAvailable(ushort rmiId, NetMessage message, EncryptMode ecMode = EncryptMode.Fast,
            bool reliable = false)
        {
            var data = new NetMessage();
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;

            data.WriteEnum(MessageType.RMI);
            data.Write(rmiId);
            data.Write(message);

            NexumToServerUdpIfAvailable(data, reliable: reliable);
        }

        internal void NexumToServerUdpIfAvailable(NetMessage data, bool force = false, bool reliable = false)
        {
            RequestServerUdpSocketReady_FirstTimeOnly();

            lock (SendLock)
            {
                data.Reliable = reliable;
                if (data.Compress)
                    data = NetZip.CompressPacket(data);
                if (data.Encrypt)
                {
                    byte[] encryptedBuffer = Crypt.Encrypt(data.GetBuffer(), data.EncryptMode);
                    var netMessage = new NetMessage();
                    netMessage.WriteEnum(MessageType.Encrypted);
                    netMessage.WriteEnum(data.EncryptMode);
                    netMessage.Write(new ByteArray(encryptedBuffer));
                    data = netMessage;
                }

                if ((UdpEnabled || force) && UdpChannel != null && ServerUdpSocket != null)
                {
                    if (reliable && ToServerReliableUdp != null)
                    {
                        byte[] wrappedData = ReliableUdpHelper.WrapPayload(data.GetBuffer());
                        ToServerReliableUdp.Send(wrappedData, wrappedData.Length);
                    }
                    else
                    {
                        ToServerUdp(data);
                    }
                }
                else if (!force)
                {
                    NexumToServer(data);
                }
            }
        }

        internal void RequestServerUdpSocketReady_FirstTimeOnly()
        {
            if (UdpChannel != null || ServerUdpReadyWaiting || ServerUdpSocketFailed)
                return;

            ServerUdpReadyWaiting = true;
            RmiToServer((ushort)NexumOpCode.C2S_RequestCreateUdpSocket, new NetMessage());
        }

        private void ToServerUdp(NetMessage message)
        {
            ToServerUdp(message.GetBuffer());
        }

        private void ToServerUdp(byte[] data)
        {
            var channel = UdpChannel;
            if (channel == null || !channel.Active || ServerUdpSocket == null)
            {
                Logger.Verbose("UDP Channel not ready, packet dropped");
                return;
            }

            foreach (var udpMessage in UdpFragBoard.FragmentPacket(data, data.Length, HostId,
                         (uint)Core.HostId.Server))
            {
                udpMessage.EndPoint = ServerUdpSocket;
                channel.WriteAndFlushAsync(udpMessage);
            }
        }

        public void Close()
        {
            Logger.Information("Initiating graceful TCP shutdown to {ServerType}", ServerType);
            var shutdownTcp = new NetMessage();

            shutdownTcp.Write(new ByteArray());

            RmiToServer((ushort)NexumOpCode.ShutdownTcp, shutdownTcp);
        }

        internal void CloseUdp()
        {
            Logger.Information("Closing UDP channel for {ServerType}", ServerType);
            UdpEnabled = false;
            SelfUdpSocket = null;

            if (ToServerReliableUdp != null)
            {
                ToServerReliableUdp.OnFailed -= OnToServerReliableUdpFailed;
                ToServerReliableUdp = null;
            }

            UdpChannel?.CloseAsync();
            UdpChannel = null;
            UdpEventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            UdpEventLoopGroup = null;
        }

        public override void Dispose()
        {
            Logger.Information("Disposing NetClient for {ServerType}", ServerType);

            SetConnectionState(ConnectionState.Disconnected);

            PingLoop?.Stop();
            StopReliableUdpLoop();

            if (ToServerReliableUdp != null)
            {
                ToServerReliableUdp.OnFailed -= OnToServerReliableUdpFailed;
                ToServerReliableUdp = null;
            }

            foreach (var member in P2PGroup?.P2PMembers.Values ?? Enumerable.Empty<P2PMember>())
                member.Close();
            P2PGroup?.P2PMembers.Clear();

            UdpChannel?.CloseAsync();
            UdpChannel = null;
            UdpEventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            UdpEventLoopGroup = null;
            Channel?.CloseAsync();
            Channel = null;
            _eventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            _eventLoopGroup = null;

            Clients.TryRemove(ServerType, out _);

            Crypt?.Dispose();
            Crypt = null;

            base.Dispose();
        }
    }
}
