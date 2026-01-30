using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BaseLib.Extensions;
using DotNetty.Buffers;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nexum.Client.P2P;
using Nexum.Client.Udp;
using Nexum.Core;
using Nexum.Core.Configuration;
using Nexum.Core.Crypto;
using Nexum.Core.DotNetty.Codecs;
using Nexum.Core.Events;
using Nexum.Core.Message.X2X;
using Nexum.Core.Mtu;
using Nexum.Core.ReliableUdp;
using Nexum.Core.Rmi.C2S;
using Nexum.Core.Routing;
using Nexum.Core.Serialization;
using Nexum.Core.Simulation;
using Nexum.Core.Udp;
using Nexum.Core.Utilities;
using Serilog;
using Constants = Serilog.Core.Constants;
using BurstDuplicateLogger = Nexum.Core.Logging.BurstDuplicateLogger;

namespace Nexum.Client.Core
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

        public delegate void OnRmiReceiveDelegate(NetMessage message, ushort rmiId);

        public delegate void OnUdpConnectedDelegate();

        public delegate void OnUdpDisconnectedDelegate();

        internal static readonly List<NetClient> Clients = new List<NetClient>();

        internal static readonly object ClientsLock = new object();

        private readonly object _stateLock = new object();

        internal readonly ConcurrentDictionary<uint, byte> PendingP2PConnections =
            new ConcurrentDictionary<uint, byte>();

        internal readonly ConcurrentDictionary<int, RecycledUdpSocket> RecycledSockets =
            new ConcurrentDictionary<int, RecycledUdpSocket>();

        internal readonly object UdpHolepunchLock = new object();

        private ConnectionState _connectionState = ConnectionState.Disconnected;

        private volatile bool _isDisposed;

        private EventLoopScheduler _recycleGarbageCollectScheduler;
        private EventLoopScheduler _reliablePingScheduler;
        private EventLoopScheduler _toServerReliableUdpScheduler;
        private EventLoopScheduler _unreliablePingScheduler;

        internal NetCrypt Crypt;

        public OnConnectedDelegate OnConnected = () => { };

        public OnDisconnectedDelegate OnDisconnected = () => { };
        public OnP2PMemberDirectConnectedDelegate OnP2PMemberDirectConnected = _ => { };
        public OnP2PMemberDirectDisconnectedDelegate OnP2PMemberDirectDisconnected = _ => { };

        public OnP2PMemberJoinDelegate OnP2PMemberJoin = _ => { };
        public OnP2PMemberLeaveDelegate OnP2PMemberLeave = _ => { };
        public OnP2PMemberRelayConnectedDelegate OnP2PMemberRelayConnected = _ => { };
        public OnP2PMemberRelayDisconnectedDelegate OnP2PMemberRelayDisconnected = _ => { };
        public OnRmiReceiveDelegate OnRmiReceive = (_, _) => { };
        public OnUdpConnectedDelegate OnUdpConnected = () => { };
        public OnUdpDisconnectedDelegate OnUdpDisconnected = () => { };

        internal uint P2PFirstFrameNumber;
        internal Guid PeerUdpMagicNumber;

        internal double RecentFrameRate;
        internal double ReliablePingInterval;
        internal IPEndPoint SelfUdpSocket;

        internal Guid ServerInstanceGuid;
        internal MtuDiscovery ServerMtuDiscovery;

        internal ReliableUdpHost ServerReliableUdp;
        internal double ServerTimeDiff;
        internal int ServerUdpFallbackCount;
        internal double ServerUdpJitter;
        internal double ServerUdpLastPing;
        internal double ServerUdpLastReceivedTime;
        internal double ServerUdpRecentPing;
        internal bool ServerUdpRequested;
        internal IPEndPoint ServerUdpSocket;

        internal IChannel UdpChannel;

        internal Guid UdpMagicNumber;

        public NetClient(string serverName, Guid serverGuid)
        {
            ServerName = serverName;
            ServerGuid = serverGuid;
            Logger = BurstDuplicateLogger.WrapForNexumHolepunching(
                Log.ForContext(Constants.SourceContextPropertyName, $"{ServerName}Client"));

            ServerMtuDiscovery = new MtuDiscovery();

            lock (ClientsLock)
            {
                Clients.Add(this);
            }
        }

        public double Ping => ServerUdpRecentPing;

        public double Jitter => ServerUdpJitter;

        public double FrameRate => RecentFrameRate;

        public double ServerTimeDifference => ServerTimeDiff;

        public static Action<IChannelPipeline> UdpPipelineConfigurator { get; set; }

        public NetworkProfile NetworkSimulationProfile { get; set; }

        internal byte[] PinnedServerPublicKey { get; private set; }

        public uint HostId { get; internal set; }

        public P2PGroup P2PGroup { get; internal set; } = new P2PGroup();

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
            Logger = BurstDuplicateLogger.WrapForNexumHolepunching(
                Log.ForContext(Constants.SourceContextPropertyName, context));
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
        public double GetServerTime()
        {
            return GetAbsoluteTime() - ServerTimeDiff;
        }

        public async Task ConnectAsync(IPEndPoint ipEndPoint)
        {
            SetConnectionState(ConnectionState.Connecting);
            Logger.Debug("Connecting to {Endpoint} with timeout {Timeout}s", ipEndPoint.ToIPv4String(),
                NetConfig.TcpSocketConnectTimeout);
            EventLoopGroup = new MultithreadEventLoopGroup(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));

            var connectTask = new Bootstrap()
                .Group(EventLoopGroup)
                .ChannelFactory(() => new TcpSocketChannel(AddressFamily.InterNetwork))
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
            var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                Logger.Warning("TCP connection to {Endpoint} timed out after {Timeout}s", ipEndPoint.ToIPv4String(),
                    NetConfig.TcpSocketConnectTimeout);
                SetConnectionState(ConnectionState.Disconnected);
                EventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
                EventLoopGroup = null;
                throw new TimeoutException(
                    $"TCP connection to {ipEndPoint.ToIPv4String()} timed out after {NetConfig.TcpSocketConnectTimeout}s");
            }

            Channel = await connectTask.ConfigureAwait(false);
            Logger.Debug("TCP connection established to {Endpoint}", ipEndPoint.ToIPv4String());
        }

        public async Task<bool> CloseAsync(bool graceful = true)
        {
            if (!graceful)
            {
                Logger.Debug("Forcing immediate disconnect from {ServerName}", ServerName);
                Dispose();
                return false;
            }

            Logger.Debug("Initiating graceful TCP shutdown to {ServerName} with timeout {Timeout}s", ServerName,
                NetConfig.GracefulDisconnectTimeout);

            var tcs = new TaskCompletionSource<bool>();

            void OnDisconnectedHandler()
            {
                tcs.TrySetResult(true);
            }

            OnDisconnected += OnDisconnectedHandler;

            try
            {
                RmiToServer(new ShutdownTcp());

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(NetConfig.GracefulDisconnectTimeout));
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);

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
            var data = new RmiMessage { RmiId = rmiId, Data = message }.Serialize();
            data.Reliable = true;
            if (ecMode != EncryptMode.None)
                data.EncryptMode = ecMode;
            if (message.Compress)
                data.Compress = true;

            NexumToServer(data);
        }

        private void RmiToServer(NetMessage message, EncryptMode ecMode = EncryptMode.Secure)
        {
            message.Reliable = true;
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;

            NexumToServer(message);
        }

        public void RmiToServer(INetRmi rmi, EncryptMode ecMode = EncryptMode.Secure)
        {
            RmiToServer(rmi.Serialize(), ecMode);
        }

        public void RmiToServerUdpIfAvailable(ushort rmiId, NetMessage message, EncryptMode ecMode = EncryptMode.Fast,
            bool force = false,
            bool reliable = false)
        {
            var data = new RmiMessage { RmiId = rmiId, Data = message }.Serialize();
            data.Reliable = reliable;
            if (ecMode != EncryptMode.None)
                data.EncryptMode = ecMode;
            if (message.Compress)
                data.Compress = true;

            NexumToServerUdpIfAvailable(data, force, reliable);
        }

        private void RmiToServerUdpIfAvailable(NetMessage message, EncryptMode ecMode = EncryptMode.Fast,
            bool force = false,
            bool reliable = false)
        {
            message.Reliable = reliable;
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;

            NexumToServerUdpIfAvailable(message, force, reliable);
        }

        public void RmiToServerUdpIfAvailable(INetRmi rmi, EncryptMode ecMode = EncryptMode.Fast,
            bool force = false, bool reliable = false)
        {
            RmiToServerUdpIfAvailable(rmi.Serialize(), ecMode, force, reliable);
        }

        public override void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            Logger.Debug("Disposing NetClient for {ServerName}", ServerName);

            SetConnectionState(ConnectionState.Disconnected);

            _unreliablePingScheduler?.Stop();
            _unreliablePingScheduler = null;
            _reliablePingScheduler?.Stop();
            _reliablePingScheduler = null;
            _toServerReliableUdpScheduler?.Stop();
            _toServerReliableUdpScheduler = null;
            _recycleGarbageCollectScheduler?.Stop();
            _recycleGarbageCollectScheduler = null;

            if (ServerReliableUdp != null)
            {
                ServerReliableUdp.OnFailed -= OnServerReliableUdpFailed;
                ServerReliableUdp = null;
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
            Channel?.CloseAsync();
            Channel = null;
            EventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            EventLoopGroup = null;

            lock (ClientsLock)
            {
                Clients.Remove(this);
            }

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

        internal async Task<(IChannel Channel, int Port, bool PortReuseSuccess)>
            ConnectUdpAsync(int? targetPort = null)
        {
            if (targetPort.HasValue && RecycledSockets.TryRemove(targetPort.Value, out var recycled))
            {
                if (recycled.Channel != null && recycled.Channel.Active && !recycled.Garbaged)
                {
                    recycled.RecycleTime = 0.0;
                    recycled.Garbaged = false;
                    Logger.Debug("Reusing recycled UDP socket on port {Port}", recycled.Port);
                    return (recycled.Channel, recycled.Port, true);
                }

                GarbageSocket(recycled);
            }

            bool IsUdpBindFailure(Exception ex)
            {
                if (ex is ChannelException || ex is SocketException)
                    return true;

                if (ex is AggregateException agg)
                    return agg.Flatten().InnerExceptions.All(IsUdpBindFailure);

                return ex.InnerException != null && IsUdpBindFailure(ex.InnerException);
            }

            async Task<IChannel> BindAsync(int port)
            {
                var defragDecoder = new UdpDefragmentationDecoder(this, NetConfig.MessageMaxLength);

                return await new Bootstrap()
                    .Group(EventLoopGroup)
                    .ChannelFactory(() => new SocketDatagramChannel(AddressFamily.InterNetwork))
                    .Handler(new ActionChannelInitializer<IChannel>(ch =>
                    {
                        if (NetworkSimulationProfile != null &&
                            !NetworkSimulationProfile.IsIdeal)
                        {
                            var handler =
                                new SimulatedUdpChannelHandler(NetworkSimulationProfile);
                            ch.Pipeline.AddFirst("network-simulation", handler);
                            NetworkSimulationStats.RegisterHandler(ch, handler);
                        }

                        UdpPipelineConfigurator?.Invoke(ch.Pipeline);

                        ch.Pipeline
                            .AddLast(new UdpFrameDecoder(NetConfig.MessageMaxLength))
                            .AddLast(defragDecoder)
                            .AddLast(new UdpFrameEncoder())
                            .AddLast(new UdpFragmentationEncoder { DefragDecoder = defragDecoder })
                            .AddLast(new ReliableUdpCodecHandler())
                            .AddLast(new UdpHandler(this, port));
                    }))
                    .Option(ChannelOption.SoRcvbuf, NetConfig.UdpIssueRecvLength)
                    .Option(ChannelOption.SoSndbuf, NetConfig.UdpSendBufferLength)
                    .Option(ChannelOption.Allocator, UnpooledByteBufferAllocator.Default)
                    .BindAsync(new IPEndPoint(LocalIP, port));
            }

            if (targetPort.HasValue)
            {
                int port = targetPort.Value;

                try
                {
                    var channel = await BindAsync(port);

                    Logger.Debug("UDP socket bound on target port {Port}", port);

                    return (channel, port, true);
                }
                catch (Exception ex) when (IsUdpBindFailure(ex))
                {
                    Logger.Debug(
                        "UDP bind failed on target port {Port}, falling back to OS-assigned port",
                        port);
                }
            }

            try
            {
                var channel = await BindAsync(0);

                int assignedPort =
                    ((IPEndPoint)channel.LocalAddress).Port;

                Logger.Debug(
                    "UDP socket bound on OS-assigned port {Port}",
                    assignedPort);

                return (channel, assignedPort, false);
            }
            catch (Exception ex) when (IsUdpBindFailure(ex))
            {
                throw new InvalidOperationException(
                    "Failed to bind UDP socket using OS-assigned port",
                    ex);
            }
        }

        internal void RecycleUdpSocket(IChannel channel, int port)
        {
            if (channel == null || !channel.Active)
            {
                Logger.Debug("Cannot recycle UDP socket on port {Port} - channel is null or inactive", port);
                return;
            }

            var recycled = new RecycledUdpSocket(channel, port, GetAbsoluteTime());
            if (RecycledSockets.TryAdd(port, recycled))
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
        }

        private void StartRecycleGarbageCollectLoopIfNeeded()
        {
            _recycleGarbageCollectScheduler = EventLoopScheduler.StartIfNeeded(
                _recycleGarbageCollectScheduler,
                TimeSpan.FromSeconds(HolepunchConfig.NatPortRecycleReuseSeconds / 2),
                DoRecycleGarbageCollect,
                Channel?.EventLoop);
        }

        private void DoRecycleGarbageCollect()
        {
            if (_isDisposed)
            {
                _recycleGarbageCollectScheduler?.Stop();
                return;
            }

            double currentTime = GetAbsoluteTime();
            double expirationTime = HolepunchConfig.NatPortRecycleReuseSeconds + 10.0;

            foreach (var kvp in RecycledSockets)
            {
                var recycled = kvp.Value;
                if (currentTime - recycled.RecycleTime > expirationTime)
                    if (RecycledSockets.TryRemove(kvp.Key, out var removed))
                    {
                        Logger.Debug("Garbage collecting expired recycled socket on port {Port}", removed.Port);
                        GarbageSocket(removed);
                    }
            }

            if (RecycledSockets.IsEmpty)
            {
                _recycleGarbageCollectScheduler?.Stop();
                _recycleGarbageCollectScheduler = null;
            }
        }

        internal void CloseUdp()
        {
            Logger.Debug("Closing UDP channel for {ServerName}", ServerName);
            bool wasEnabled = UdpEnabled;
            UdpEnabled = false;
            SelfUdpSocket = null;

            _unreliablePingScheduler?.Stop();
            _unreliablePingScheduler = null;
            _reliablePingScheduler?.Stop();
            _reliablePingScheduler = null;
            _toServerReliableUdpScheduler?.Stop();
            _toServerReliableUdpScheduler = null;
            _recycleGarbageCollectScheduler?.Stop();
            _recycleGarbageCollectScheduler = null;

            if (ServerReliableUdp != null)
            {
                ServerReliableUdp.OnFailed -= OnServerReliableUdpFailed;
                ServerReliableUdp = null;
            }

            foreach (var recycled in RecycledSockets.Values)
                GarbageSocket(recycled);
            RecycledSockets.Clear();

            UdpChannel?.CloseAsync();
            UdpChannel = null;

            ServerUdpSocket = null;
            ServerUdpLastReceivedTime = 0;
            ServerUdpLastPing = 0;
            ServerUdpRecentPing = 0;
            ServerUdpJitter = 0;
            ServerUdpRequested = false;

            if (wasEnabled)
                OnUdpDisconnected();
        }

        internal void StartReliableUdpLoop()
        {
            _toServerReliableUdpScheduler = EventLoopScheduler.StartIfNeeded(
                _toServerReliableUdpScheduler,
                TimeSpan.FromMilliseconds(ReliableUdpConfig.FrameMoveInterval * 1000),
                ReliableUdpFrameMove,
                Channel?.EventLoop);
        }

        internal void InitializeServerReliableUdp(uint firstFrameNumber)
        {
            if (ServerReliableUdp != null)
                return;

            ServerReliableUdp = new ReliableUdpHost(firstFrameNumber)
            {
                GetAbsoluteTime = GetAbsoluteTime,
                GetRecentPing = () => ServerUdpRecentPing > 0 ? ServerUdpRecentPing : 0.05,
                SendOneFrameToUdpLayer = ToServerReliableUdp,
                IsReliableChannel = () => false
            };

            ServerReliableUdp.OnFailed += OnServerReliableUdpFailed;
            Logger.Debug("Server reliable UDP initialized with firstFrameNumber = {FirstFrameNumber}",
                firstFrameNumber);
        }


        internal void StartReliablePingLoop(double interval)
        {
            ReliablePingInterval = interval;
            _reliablePingScheduler = EventLoopScheduler.StartIfNeeded(
                _reliablePingScheduler,
                TimeSpan.FromSeconds(interval),
                SendReliablePing,
                Channel?.EventLoop);
        }

        private void SendReliablePing()
        {
            if (_isDisposed)
                return;

            if (ConnectionState == ConnectionState.Connected)
                RmiToServer(new ReliablePing());
        }

        internal void StartUnreliablePingLoop()
        {
            _unreliablePingScheduler = EventLoopScheduler.StartIfNeeded(
                _unreliablePingScheduler,
                TimeSpan.FromSeconds(ReliableUdpConfig.CsPingInterval),
                SendUnreliablePing,
                Channel?.EventLoop);
        }

        private void SendUnreliablePing()
        {
            if (_isDisposed)
                return;

            double currentTime = GetAbsoluteTime();

            CheckServerUdpTimeout(currentTime);

            int paddingSize = ServerMtuDiscovery.GetProbePaddingSize(currentTime);

            var unreliablePing = new NetMessage();
            unreliablePing.Write(MessageType.UnreliablePing);
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
            if (FilterTag.Create((uint)Nexum.Core.Routing.HostId.Server, HostId) == filterTag)
                return false;
            if (relayFrom == 0)
                return false;

            if (P2PGroup == null)
                return false;

            var member = P2PGroup.FindMember(HostId, udpEndPoint, filterTag, relayFrom);
            return member != null;
        }

        public void NexumToServer(INetCoreMessage msg)
        {
            NexumToServer(msg.Serialize());
        }

        internal void NexumToServer(NetMessage data)
        {
            if (data.Compress)
                data = NetZip.CompressPacket(data);
            if (data.Encrypt && Crypt != null)
                data = Crypt.CreateEncryptedMessage(data);

            var message = new NetMessage();
            message.Write((ByteArray)data);
            ToServer(message);
        }

        internal void NexumToServerUdpIfAvailable(NetMessage data, bool force = false, bool reliable = false)
        {
            RequestServerUdpSocketReady_FirstTimeOnly();

            data.Reliable = reliable;
            if (data.Compress)
                data = NetZip.CompressPacket(data);
            if (data.Encrypt && Crypt != null)
                data = Crypt.CreateEncryptedMessage(data);

            if ((UdpEnabled || force) && UdpChannel != null && ServerUdpSocket != null)
            {
                if (reliable && ServerReliableUdp != null)
                {
                    byte[] wrappedData = ReliableUdpHelper.WrapPayload(data.GetBufferSpan());
                    ServerReliableUdp.Send(wrappedData, wrappedData.Length);
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

        internal void RequestServerUdpSocketReady_FirstTimeOnly()
        {
            if (UdpChannel != null || ServerUdpRequested)
                return;

            ServerUdpRequested = true;
            RmiToServer(new C2S_RequestCreateUdpSocket());
        }

        private void ToServer(NetMessage message)
        {
            var buffer = Unpooled.WrappedBuffer(message.GetBufferUnsafe(), 0, message.Length);
            Channel.WriteAndFlushAsync(buffer);
        }

        private void ToServerUdp(NetMessage message)
        {
            if (!TryGetServerUdpChannel(out var channel))
            {
                Logger.Verbose("UDP Channel not ready, packet dropped");
                return;
            }

            var packet = new OutboundUdpPacket
            {
                Content = Unpooled.WrappedBuffer(message.GetBufferUnsafe(), 0, message.Length),
                EndPoint = ServerUdpSocket,
                FilterTag = FilterTag.Create(HostId, (uint)Nexum.Core.Routing.HostId.Server),
                Mtu = ServerMtuDiscovery.ConfirmedMtu
            };
            channel.WriteAndFlushAsync(packet);
        }

        private void ToServerReliableUdp(ReliableUdpFrame frame)
        {
            if (!TryGetServerUdpChannel(out var channel))
            {
                Logger.Verbose("UDP Channel not ready, reliable frame dropped");
                return;
            }

            var outboundFrame = new OutboundReliableUdpFrame
            {
                Frame = frame,
                DestEndPoint = ServerUdpSocket,
                FilterTag = FilterTag.Create(HostId, (uint)Nexum.Core.Routing.HostId.Server),
                Mtu = ServerMtuDiscovery.ConfirmedMtu
            };
            channel.WriteAndFlushAsync(outboundFrame);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetServerUdpChannel(out IChannel channel)
        {
            channel = UdpChannel;
            return channel != null && channel.Active && ServerUdpSocket != null;
        }

        private void OnServerReliableUdpFailed()
        {
            Logger.Warning(
                "ServerReliableUdp failed after max retries, falling back to TCP and requesting re-holepunch");

            ServerReliableUdp.OnFailed -= OnServerReliableUdpFailed;
            ServerReliableUdp = null;

            UdpEnabled = false;

            CloseUdp();

            RmiToServer(new C2S_RequestCreateUdpSocket());
        }

        private void ReliableUdpFrameMove(double elapsedTime)
        {
            if (_isDisposed)
                return;

            UpdateFrameRate(elapsedTime);
            ServerReliableUdp?.FrameMove(elapsedTime);
        }

        private void UpdateFrameRate(double deltaSeconds)
        {
            if (deltaSeconds > 0)
            {
                double instantFrameRate = 1.0 / deltaSeconds;
                RecentFrameRate = RecentFrameRate > 0
                    ? SysUtil.Lerp(RecentFrameRate, instantFrameRate, ReliableUdpConfig.LagLinearProgrammingFactor)
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

            RmiToServer(new NotifyUdpToTcpFallbackByClient());

            if (ServerUdpFallbackCount < ReliableUdpConfig.ServerUdpRepunchMaxTrialCount)
            {
                ServerUdpFallbackCount++;

                RmiToServer(new C2S_RequestCreateUdpSocket());
            }
            else
            {
                Logger.Warning("Server UDP max repunch attempts ({MaxAttempts}) reached, staying on TCP",
                    ReliableUdpConfig.ServerUdpRepunchMaxTrialCount);
            }
        }
    }
}
