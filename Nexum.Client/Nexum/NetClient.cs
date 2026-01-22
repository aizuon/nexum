using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BaseLib;
using BaseLib.Extensions;
using DotNetty.Buffers;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nexum.Core;
using Nexum.Core.DotNetty.Codecs;
using Nexum.Core.Simulation;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Client
{
    public class NetClient : NetCore
    {
        public delegate void OnConnectedDelegate();

        public delegate void OnDisconnectedDelegate();

        public delegate void OnP2PMemberDirectConnectedDelegate(uint hostId);

        public delegate void OnP2PMemberDirectDisconnectedDelegate(uint hostId);

        public delegate void OnP2PMemberJoinDelegate(uint hostId);

        public delegate void OnP2PMemberLeaveDelegate(uint hostId);

        public delegate void OnP2PMemberRelayConnectedDelegate(uint hostId);

        public delegate void OnP2PMemberRelayDisconnectedDelegate(uint hostId);

        public delegate void OnRMIReceiveDelegate(NetMessage message, ushort rmiId);

        public delegate void OnUdpConnectedDelegate();

        public delegate void OnUdpDisconnectedDelegate();

        internal static readonly ConcurrentDictionary<ServerType, NetClient> Clients =
            new ConcurrentDictionary<ServerType, NetClient>();

        private static readonly Mutex _udpConnectMutex = new Mutex(false, "NexumUdpConnectMutex");

        private readonly object _recycleLoopLock = new object();

        private readonly object _stateLock = new object();

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        internal readonly ConcurrentDictionary<uint, byte> PendingP2PConnections =
            new ConcurrentDictionary<uint, byte>();

        internal readonly object RecvLock = new object();

        internal readonly ConcurrentDictionary<ushort, RecycledUdpSocket> RecycledSockets =
            new ConcurrentDictionary<ushort, RecycledUdpSocket>();

        internal readonly object SendLock = new object();

        internal readonly object UdpHolepunchLock = new object();

        private ConnectionState _connectionState = ConnectionState.Disconnected;

        private IEventLoopGroup _eventLoopGroup;

        private volatile bool _isDisposed;

        private bool _recycleGarbageCollectLoopRunning;

        internal ushort AimForPort = NetConfig.UdpAimForPort;

        internal NetCrypt Crypt;

        public OnConnectedDelegate OnConnected = () => { };

        public OnDisconnectedDelegate OnDisconnected = () => { };
        public OnP2PMemberDirectConnectedDelegate OnP2PMemberDirectConnected = _ => { };
        public OnP2PMemberDirectDisconnectedDelegate OnP2PMemberDirectDisconnected = _ => { };

        public OnP2PMemberJoinDelegate OnP2PMemberJoin = _ => { };
        public OnP2PMemberLeaveDelegate OnP2PMemberLeave = _ => { };
        public OnP2PMemberRelayConnectedDelegate OnP2PMemberRelayConnected = _ => { };
        public OnP2PMemberRelayDisconnectedDelegate OnP2PMemberRelayDisconnected = _ => { };
        public OnRMIReceiveDelegate OnRMIReceive = (_, _) => { };
        public OnUdpConnectedDelegate OnUdpConnected = () => { };
        public OnUdpDisconnectedDelegate OnUdpDisconnected = () => { };

        internal uint P2PFirstFrameNumber;
        internal Guid PeerUdpMagicNumber;

        internal double RecentFrameRate;
        internal double ReliablePingInterval;
        internal bool ReliablePingLoopRunning;
        internal ThreadLoop ReliableUdpLoop;
        internal IPEndPoint SelfUdpSocket;

        internal Guid ServerGuid;
        internal MtuDiscovery ServerMtuDiscovery;
        internal double ServerTimeDiff;
        internal int ServerUdpFallbackCount;
        internal double ServerUdpJitter;
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

        internal ThreadLoop UnreliablePingLoop;

        public NetClient(ServerType serverType)
        {
            ServerType = serverType;
            Logger = Log.ForContext(Constants.SourceContextPropertyName, $"{ServerType}Client");

            ServerMtuDiscovery = new MtuDiscovery();
            UdpFragBoard = new UdpPacketFragBoard { MtuDiscovery = ServerMtuDiscovery };
            UdpDefragBoard = new UdpPacketDefragBoard();

            if (ServerType == ServerType.Relay)
                P2PGroup = new P2PGroup();

            Clients.TryAdd(ServerType, this);
        }

        public double Ping => ServerUdpRecentPing;

        public double Jitter => ServerUdpJitter;

        public double FrameRate => RecentFrameRate;

        public double ServerTimeDifference => ServerTimeDiff;

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

        internal void UpdateLoggerContext(string context)
        {
            Logger = Log.ForContext(Constants.SourceContextPropertyName, context);
        }

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        public void SetPinnedServerPublicKey(byte[] derEncodedKey)
        {
            if (derEncodedKey == null || derEncodedKey.Length == 0)
                throw new ArgumentNullException(nameof(derEncodedKey));

            PinnedServerPublicKey = derEncodedKey;
            Logger.Verbose("Server public key pinned for certificate validation");
        }

        public void SetPinnedServerPublicKey(string base64Key)
        {
            if (string.IsNullOrWhiteSpace(base64Key))
                throw new ArgumentNullException(nameof(base64Key));

            PinnedServerPublicKey = Convert.FromBase64String(base64Key);
            Logger.Verbose("Server public key pinned for certificate validation");
        }

        public void ClearPinnedServerPublicKey()
        {
            PinnedServerPublicKey = null;
            Logger.Verbose("Server public key pinning disabled");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetAbsoluteTime()
        {
            return _stopwatch.Elapsed.TotalSeconds;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetServerTime()
        {
            return GetAbsoluteTime() - ServerTimeDiff;
        }

        public async Task ConnectAsync(IPEndPoint ipEndPoint)
        {
            SetConnectionState(ConnectionState.Connecting);
            Logger.Debug("Connecting to {Endpoint} with timeout {Timeout}s", ipEndPoint.ToIPv4String(),
                NetConfig.TcpSocketConnectTimeout);
            _eventLoopGroup = new MultithreadEventLoopGroup();

            var connectTask = new Bootstrap()
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
                .Option(ChannelOption.ConnectTimeout, TimeSpan.FromSeconds(NetConfig.TcpSocketConnectTimeout))
                .ConnectAsync(ipEndPoint);

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(NetConfig.TcpSocketConnectTimeout));
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                Logger.Warning("TCP connection to {Endpoint} timed out after {Timeout}s", ipEndPoint.ToIPv4String(),
                    NetConfig.TcpSocketConnectTimeout);
                SetConnectionState(ConnectionState.Disconnected);
                _eventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
                _eventLoopGroup = null;
                throw new TimeoutException(
                    $"TCP connection to {ipEndPoint.ToIPv4String()} timed out after {NetConfig.TcpSocketConnectTimeout}s");
            }

            Channel = await connectTask;
            Logger.Debug("TCP connection established to {Endpoint}", ipEndPoint.ToIPv4String());
        }

        public async Task<bool> CloseAsync(bool graceful = true)
        {
            if (!graceful)
            {
                Logger.Debug("Forcing immediate disconnect from {ServerType}", ServerType);
                Dispose();
                return false;
            }

            Logger.Debug("Initiating graceful TCP shutdown to {ServerType} with timeout {Timeout}s", ServerType,
                NetConfig.GracefulDisconnectTimeout);

            var tcs = new TaskCompletionSource<bool>();

            void OnDisconnectedHandler()
            {
                tcs.TrySetResult(true);
            }

            OnDisconnected += OnDisconnectedHandler;

            try
            {
                var shutdownTcp = new NetMessage();
                shutdownTcp.Write(new ByteArray());
                RmiToServer((ushort)NexumOpCode.ShutdownTcp, shutdownTcp);

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(NetConfig.GracefulDisconnectTimeout));
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Logger.Warning("Graceful disconnect timed out after {Timeout}s, forcing disconnect",
                        NetConfig.GracefulDisconnectTimeout);
                    Dispose();
                    return false;
                }

                Logger.Debug("Graceful disconnect completed successfully");
                Dispose();
                return true;
            }
            finally
            {
                OnDisconnected -= OnDisconnectedHandler;
            }
        }

        public void RmiToServer(ushort rmiId, NetMessage message, EncryptMode ecMode = EncryptMode.Secure)
        {
            var data = new NetMessage();
            data.Reliable = true;
            if (ecMode != EncryptMode.None)
                data.EncryptMode = ecMode;
            if (message.Compress)
                data.Compress = true;

            data.WriteEnum(MessageType.RMI);
            data.Write(rmiId);
            data.Write(message);
            NexumToServer(data);
        }

        public void RmiToServerUdpIfAvailable(ushort rmiId, NetMessage message, EncryptMode ecMode = EncryptMode.Fast,
            bool force = false,
            bool reliable = false)
        {
            var data = new NetMessage();
            data.Reliable = reliable;
            if (ecMode != EncryptMode.None)
                data.EncryptMode = ecMode;
            if (message.Compress)
                data.Compress = true;

            data.WriteEnum(MessageType.RMI);
            data.Write(rmiId);
            data.Write(message);

            NexumToServerUdpIfAvailable(data, force, reliable);
        }

        public override void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            Logger.Debug("Disposing NetClient for {ServerType}", ServerType);

            SetConnectionState(ConnectionState.Disconnected);

            UnreliablePingLoop?.Stop();
            UnreliablePingLoop = null;
            ReliablePingLoopRunning = false;
            ReliableUdpLoop?.Stop();
            ReliableUdpLoop = null;
            _recycleGarbageCollectLoopRunning = false;

            if (ToServerReliableUdp != null)
            {
                ToServerReliableUdp.OnFailed -= OnToServerReliableUdpFailed;
                ToServerReliableUdp = null;
            }

            foreach (var member in P2PGroup?.P2PMembersInternal.Values ?? Enumerable.Empty<P2PMember>())
                member.Close();
            P2PGroup?.P2PMembersInternal.Clear();
            P2PGroup = null;

            foreach (var recycled in RecycledSockets.Values)
                GarbageSocket(recycled);
            RecycledSockets.Clear();

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

            Logger.Debug("Connection state changed: {PreviousState} -> {NewState}", previousState, newState);

            var args = new ConnectionStateChangedEventArgs(previousState, newState);
            ConnectionStateChanged?.Invoke(this, args);

            if (newState == ConnectionState.Disconnected)
                OnDisconnected();
        }

        internal (IChannel Channel, IEventLoopGroup WorkerGroup, int Port, bool PortReuseSuccess)
            ConnectUdp(
                int? targetPort = null)
        {
            if (targetPort.HasValue && RecycledSockets.TryRemove((ushort)targetPort.Value, out var recycled))
            {
                if (recycled.Channel != null && recycled.Channel.Active && !recycled.Garbaged)
                {
                    recycled.RecycleTime = 0.0;
                    recycled.Garbaged = false;
                    Logger.Debug("Reusing recycled UDP socket on port {Port}", recycled.Port);
                    return (recycled.Channel, recycled.EventLoopGroup, recycled.Port, true);
                }

                GarbageSocket(recycled);
            }

            _udpConnectMutex.WaitOne();
            try
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
                            .AddLast(new UdpHandler(this, port));
                    }))
                    .Option(ChannelOption.SoRcvbuf, NetConfig.UdpIssueRecvLength)
                    .Option(ChannelOption.SoSndbuf, NetConfig.UdpSendBufferLength)
                    .Option(ChannelOption.Allocator, UnpooledByteBufferAllocator.Default)
                    .BindAsync(new IPEndPoint(LocalIP, port))
                    .GetAwaiter().GetResult();

                Logger.Debug("UDP socket bound on port {Port}", port);

                _ = channel.CloseCompletion.ContinueWith(_ =>
                {
                    Logger.Debug("UDP socket on port {Port} closed", port);
                    NetUtil.ReleasePort(port);
                });

                return (channel, workerGroup, port, portReuseSuccess);
            }
            finally
            {
                _udpConnectMutex.ReleaseMutex();
            }
        }

        internal void RecycleUdpSocket(IChannel channel, IEventLoopGroup eventLoopGroup, int port)
        {
            if (channel == null || !channel.Active)
            {
                Logger.Debug("Cannot recycle UDP socket on port {Port} - channel is null or inactive", port);
                return;
            }

            var recycled = new RecycledUdpSocket(channel, eventLoopGroup, port, GetAbsoluteTime());
            if (RecycledSockets.TryAdd((ushort)port, recycled))
            {
                Logger.Debug("Recycled UDP socket on port {Port}", port);
                StartRecycleGarbageCollectLoopIfNeeded();
            }
            else
            {
                Logger.Debug("Port {Port} already has a recycled socket, disposing the new one", port);
                GarbageSocket(recycled);
            }
        }

        internal void GarbageSocket(RecycledUdpSocket recycled)
        {
            if (recycled == null || recycled.Garbaged)
                return;

            recycled.Garbaged = true;
            Logger.Debug("Garbage collecting UDP socket on port {Port}", recycled.Port);

            recycled.Channel?.CloseAsync();
            recycled.EventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        }

        private void StartRecycleGarbageCollectLoopIfNeeded()
        {
            lock (_recycleLoopLock)
            {
                if (_recycleGarbageCollectLoopRunning)
                    return;

                _recycleGarbageCollectLoopRunning = true;
                ScheduleAsync(DoRecycleGarbageCollect, this, null,
                    TimeSpan.FromSeconds(HolepunchConfig.NatPortRecycleReuseSeconds / 2));
            }
        }

        private static void DoRecycleGarbageCollect(object context, object _)
        {
            if (context == null)
                return;

            var client = (NetClient)context;

            if (!client._recycleGarbageCollectLoopRunning)
                return;

            double currentTime = client.GetAbsoluteTime();
            double expirationTime = HolepunchConfig.NatPortRecycleReuseSeconds + 10.0;

            foreach (var kvp in client.RecycledSockets)
            {
                var recycled = kvp.Value;
                if (currentTime - recycled.RecycleTime > expirationTime)
                    if (client.RecycledSockets.TryRemove(kvp.Key, out var removed))
                    {
                        client.Logger.Debug("Garbage collecting expired recycled socket on port {Port}", removed.Port);
                        client.GarbageSocket(removed);
                    }
            }

            if (client.RecycledSockets.IsEmpty)
                lock (client._recycleLoopLock)
                {
                    client._recycleGarbageCollectLoopRunning = false;
                    return;
                }

            client.ScheduleAsync(DoRecycleGarbageCollect, client, null,
                TimeSpan.FromSeconds(HolepunchConfig.NatPortRecycleReuseSeconds / 2));
        }

        internal void CloseUdp()
        {
            Logger.Debug("Closing UDP channel for {ServerType}", ServerType);
            bool wasEnabled = UdpEnabled;
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

            ServerUdpSocket = null;
            ServerUdpLastReceivedTime = 0;
            ServerUdpLastPing = 0;
            ServerUdpRecentPing = 0;
            ServerUdpJitter = 0;

            if (wasEnabled)
                OnUdpDisconnected();
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
            Logger.Debug("Server reliable UDP initialized with firstFrameNumber = {FirstFrameNumber}",
                firstFrameNumber);
        }


        internal void StartReliablePingLoop(double interval)
        {
            if (ReliablePingLoopRunning)
                return;

            ReliablePingLoopRunning = true;
            ReliablePingInterval = interval;
            ScheduleAsync(SendReliablePingScheduled, this, null, TimeSpan.FromSeconds(interval));
        }

        internal Task ScheduleAsync(Action<object, object> action, object context, object state, TimeSpan delay)
        {
            return _eventLoopGroup?.ScheduleAsync(action, context, state, delay);
        }

        private static void SendReliablePingScheduled(object context, object _)
        {
            if (context == null)
                return;

            var client = (NetClient)context;

            if (!client.ReliablePingLoopRunning)
                return;

            if (client.ConnectionState == ConnectionState.Connected)
            {
                var reliablePingMsg = new NetMessage();
                reliablePingMsg.Write(client.RecentFrameRate);
                client.RmiToServer((ushort)NexumOpCode.ReliablePing, reliablePingMsg);
            }

            client.ScheduleAsync(SendReliablePingScheduled, client, null,
                TimeSpan.FromSeconds(client.ReliablePingInterval));
        }

        internal void StartUnreliablePingLoop()
        {
            if (UnreliablePingLoop != null)
                return;

            UnreliablePingLoop = new ThreadLoop(
                TimeSpan.FromSeconds(ReliableUdpConfig.CsPingInterval),
                SendUdpPing);
            UnreliablePingLoop.Start();
        }

        internal void SendUdpPing(TimeSpan delta)
        {
            double currentTime = GetAbsoluteTime();

            UpdateFrameRate(delta.TotalSeconds);
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

        internal bool IsFromRemoteClientPeer(IPEndPoint udpEndPoint = null, ushort filterTag = 0,
            uint relayFrom = 0)
        {
            if (udpEndPoint != null && ServerUdpSocket != null &&
                udpEndPoint.Equals(ServerUdpSocket))
                return false;
            if (FilterTag.Create((uint)Core.HostId.Server, HostId) == filterTag)
                return false;
            if (relayFrom == 0)
                return false;

            if (P2PGroup == null)
                return false;

            var member = P2PGroup.FindMember(HostId, udpEndPoint, filterTag, relayFrom);
            return member != null;
        }

        internal void NexumToServer(NetMessage data)
        {
            lock (SendLock)
            {
                if (data.Compress)
                    data = NetZip.CompressPacket(data);
                if (data.Encrypt && Crypt != null)
                    data = Crypt.CreateEncryptedMessage(data);

                var message = new NetMessage();
                message.Write((ByteArray)data);
                ToServer(message);
            }
        }

        internal void NexumToServerUdpIfAvailable(NetMessage data, bool force = false, bool reliable = false)
        {
            RequestServerUdpSocketReady_FirstTimeOnly();

            lock (SendLock)
            {
                data.Reliable = reliable;
                if (data.Compress)
                    data = NetZip.CompressPacket(data);
                if (data.Encrypt && Crypt != null)
                    data = Crypt.CreateEncryptedMessage(data);

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

        private void ToServer(NetMessage message)
        {
            ToServer(message.GetBuffer());
        }

        private void ToServer(byte[] data)
        {
            var buffer = Unpooled.WrappedBuffer(data);
            Channel.WriteAndFlushAsync(buffer);
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

            foreach (var udpMessage in UdpFragBoard.FragmentPacket(data, HostId,
                         (uint)Core.HostId.Server))
            {
                udpMessage.EndPoint = ServerUdpSocket;
                channel.WriteAndFlushAsync(udpMessage);
            }
        }

        private void SendReliableUdpFrameToServer(ReliableUdpFrame frame)
        {
            lock (SendLock)
            {
                var msg = ReliableUdpHelper.BuildFrameMessage(frame);
                ToServerUdp(msg);
            }
        }

        private void OnToServerReliableUdpFailed()
        {
            Logger.Warning(
                "ToServerReliableUdp failed after max retries, falling back to TCP and requesting re-holepunch");

            ToServerReliableUdp.OnFailed -= OnToServerReliableUdpFailed;
            ToServerReliableUdp = null;

            UdpEnabled = false;

            CloseUdp();

            RmiToServer((ushort)NexumOpCode.C2S_RequestCreateUdpSocket, new NetMessage());
        }

        private void ReliableUdpFrameMove(TimeSpan delta)
        {
            double elapsedTime = delta.TotalSeconds;
            double currentTime = GetAbsoluteTime();

            ToServerReliableUdp?.FrameMove(elapsedTime);
            UdpDefragBoard.PruneStalePackets(currentTime);

            if (P2PGroup != null)
                foreach (var member in P2PGroup.P2PMembersInternal.Values)
                    if (!member.IsClosed)
                        member.FrameMove(elapsedTime);
        }

        private void UpdateFrameRate(double deltaSeconds)
        {
            if (deltaSeconds > 0)
            {
                double instantFrameRate = 1.0 / deltaSeconds;
                RecentFrameRate = RecentFrameRate > 0
                    ? Core.SysUtil.Lerp(RecentFrameRate, instantFrameRate, ReliableUdpConfig.LagLinearProgrammingFactor)
                    : instantFrameRate;
            }
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

            bool wasEnabled = UdpEnabled;
            UdpEnabled = false;

            CloseUdp();

            if (wasEnabled)
                OnUdpDisconnected();

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
        }
    }
}
