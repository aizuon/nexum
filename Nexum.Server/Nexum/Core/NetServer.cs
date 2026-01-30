using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BaseLib.Extensions;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nexum.Core;
using Nexum.Core.Configuration;
using Nexum.Core.DotNetty.Codecs;
using Nexum.Core.Events;
using Nexum.Core.Holepunching;
using Nexum.Core.Message.S2C;
using Nexum.Core.Message.X2X;
using Nexum.Core.Rmi.S2C;
using Nexum.Core.Routing;
using Nexum.Core.Serialization;
using Nexum.Core.Utilities;
using Nexum.Server.P2P;
using Nexum.Server.Sessions;
using Nexum.Server.Udp;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Security;
using Serilog;
using SerilogConstants = Serilog.Core.Constants;

namespace Nexum.Server.Core
{
    public class NetServer : NetCore
    {
        public delegate void OnRmiReceiveDelegate(NetSession session, NetMessage message, ushort rmiId);

        public delegate void OnSessionConnectedDelegate(NetSession session);

        public delegate void OnSessionConnectingDelegate(NetSession session);

        public delegate void OnSessionDisconnectedDelegate(NetSession session);

        public delegate void OnSessionHandshakingDelegate(NetSession session);

        private static readonly TimeSpan ConnectTimeoutCheckInterval = TimeSpan.FromSeconds(2);

        private static readonly IPEndPoint DummyEndPoint = new IPEndPoint(IPAddress.Parse("255.255.255.255"), 65535);

        private EventLoopScheduler _connectTimeoutScheduler;
        private EventLoopScheduler _retryUdpOrHolepunchScheduler;
        private UdpSocket[] _udpSocketsCache;

        public OnRmiReceiveDelegate OnRmiReceive = (_, _, _) => { };

        public OnSessionConnectedDelegate OnSessionConnected = _ => { };

        public OnSessionConnectingDelegate OnSessionConnecting = _ => { };

        public OnSessionDisconnectedDelegate OnSessionDisconnected = _ => { };

        public OnSessionHandshakingDelegate OnSessionHandshaking = _ => { };

        internal Guid ServerInstanceGuid = Guid.NewGuid();

        public NetServer(string serverName, Guid serverGuid, NetSettings netSettings = null, bool allowDirectP2P = true)
        {
            ServerName = serverName;
            ServerGuid = serverGuid;

            Logger = Log.ForContext(SerilogConstants.SourceContextPropertyName, $"{ServerName}Server");
            RSA = RSA.Create(2048);
            NetSettings = netSettings ?? new NetSettings();
            AllowDirectP2P = allowDirectP2P;
        }

        internal bool UdpEnabled => UdpSockets.Count > 0;

        public IReadOnlyDictionary<uint, NetSession> Sessions => SessionsInternal;

        public IReadOnlyDictionary<uint, P2PGroup> P2PGroups => P2PGroupsInternal;

        internal ConcurrentDictionary<uint, NetSession> SessionsInternal { get; } =
            new ConcurrentDictionary<uint, NetSession>();

        internal ConcurrentDictionary<uint, P2PGroup> P2PGroupsInternal { get; } =
            new ConcurrentDictionary<uint, P2PGroup>();

        internal new NetSettings NetSettings { get; }

        internal bool AllowDirectP2P { get; }

        internal HostIdFactory HostIdFactory { get; } = new HostIdFactory();

        internal ConcurrentDictionary<Guid, NetSession> MagicNumberSessions { get; } =
            new ConcurrentDictionary<Guid, NetSession>();

        internal ConcurrentDictionary<ushort, NetSession> UdpSessions { get; } =
            new ConcurrentDictionary<ushort, NetSession>();

        internal ConcurrentDictionary<uint, UdpSocket> UdpSockets { get; } =
            new ConcurrentDictionary<uint, UdpSocket>();


        internal IPAddress IPAddress { get; private set; }

        public event EventHandler<SessionConnectionStateChangedEventArgs> SessionConnectionStateChanged;

        public void ImportRsaKey(string xmlString)
        {
            if (string.IsNullOrWhiteSpace(xmlString))
                throw new ArgumentNullException(nameof(xmlString));

            var newRsa = RSA.Create(2048);
            newRsa.FromXmlString(xmlString);

            RSA?.Dispose();
            RSA = newRsa;

            Logger.Debug("RSA key imported from XML format");
        }

        public void ImportRsaKey(RSAParameters parameters)
        {
            var newRsa = RSA.Create(2048);
            newRsa.ImportParameters(parameters);

            RSA?.Dispose();
            RSA = newRsa;

            Logger.Debug("RSA key imported from RSAParameters");
        }

        public void ImportRsaPrivateKey(byte[] privateKey)
        {
            if (privateKey == null || privateKey.Length == 0)
                throw new ArgumentNullException(nameof(privateKey));

            var newRsa = RSA.Create(2048);

            try
            {
                newRsa.ImportPkcs8PrivateKey(privateKey, out _);
            }
            catch (Exception)
            {
                newRsa.ImportRSAPrivateKey(privateKey, out _);
            }

            RSA?.Dispose();
            RSA = newRsa;

            Logger.Debug("RSA private key imported");
        }

        public void ImportRsaPrivateKeyFromPem(string pemString)
        {
            if (string.IsNullOrWhiteSpace(pemString))
                throw new ArgumentNullException(nameof(pemString));

            var newRsa = RSA.Create(2048);
            newRsa.ImportFromPem(pemString.AsSpan());

            RSA?.Dispose();
            RSA = newRsa;

            Logger.Debug("RSA private key imported from PEM");
        }

        public byte[] ExportRsaPublicKey()
        {
            lock (RSALock)
            {
                var pubKey = DotNetUtilities.GetRsaPublicKey(RSA.ExportParameters(false));
                var pubKeyStruct = new RsaPublicKeyStructure(pubKey.Modulus, pubKey.Exponent);
                return pubKeyStruct.GetDerEncoded();
            }
        }

        public string ExportRsaPublicKeyBase64()
        {
            return Convert.ToBase64String(ExportRsaPublicKey());
        }

        public P2PGroup CreateP2PGroup()
        {
            var group = new P2PGroup(this);
            P2PGroupsInternal.TryAdd(group.HostId, group);
            return group;
        }

        public async Task ListenAsync(IPEndPoint endPoint, uint[] udpListenerPorts = null)
        {
            EventLoopGroup = new MultithreadEventLoopGroup();

            IPAddress = endPoint.Address;

            Channel = await new ServerBootstrap()
                .Group(EventLoopGroup)
                .ChannelFactory(() => new TcpServerSocketChannel(AddressFamily.InterNetwork))
                .Handler(new ActionChannelInitializer<IServerSocketChannel>(_ => { }))
                .ChildHandler(new ActionChannelInitializer<ISocketChannel>(ch =>
                {
                    ch.Pipeline.AddLast(new SessionHandler(this));
                    if (NetSettings.IdleTimeout > 0)
                        ch.Pipeline.AddLast(new IdleStateHandler(0, 0, (int)NetSettings.IdleTimeout));
                    ch.Pipeline.AddLast(new NexumFrameDecoder((int)NetSettings.MessageMaxLength));
                    ch.Pipeline.AddLast(new NexumFrameEncoder());
                    ch.Pipeline.AddLast(new NetServerAdapter(this));
                }))
                .ChildOption(ChannelOption.TcpNodelay, !NetConfig.EnableNagleAlgorithm)
                .ChildOption(ChannelOption.SoRcvbuf, NetConfig.TcpIssueRecvLength)
                .ChildOption(ChannelOption.SoSndbuf, NetConfig.TcpSendBufferLength)
                .ChildOption(ChannelOption.AllowHalfClosure, false)
                .ChildAttribute(ChannelAttributes.Session, default(NetSession))
                .BindAsync(endPoint).ConfigureAwait(false);

            if (udpListenerPorts != null)
            {
                foreach (uint port in udpListenerPorts)
                {
                    var udpSocket = new UdpSocket(this);
                    await udpSocket.ListenAsync(((IPEndPoint)Channel.LocalAddress).Address.MapToIPv4(), (int)port,
                        EventLoopGroup).ConfigureAwait(false);
                    UdpSockets.TryAdd(port, udpSocket);
                }

                _udpSocketsCache = UdpSockets.Values.ToArray();
            }

            StartRetryUdpOrHolepunchScheduler();
            StartConnectTimeoutScheduler();

            Logger.Debug("Listening on {Endpoint}", endPoint.ToIPv4String());

            Logger.Debug(
                "Server started: DirectP2P={AllowDirectP2P}, UDP ports={UdpPortCount}, Encryption={EncryptionKeyLength}bit",
                AllowDirectP2P,
                udpListenerPorts?.Length ?? 0,
                NetSettings.EncryptedMessageKeyLength);
        }

        public override void Dispose()
        {
            Logger.Debug("Shutting down {ServerName} server with {SessionCount} active sessions",
                ServerName, SessionsInternal.Count);

            _connectTimeoutScheduler?.Stop();
            _connectTimeoutScheduler = null;
            _retryUdpOrHolepunchScheduler?.Stop();
            _retryUdpOrHolepunchScheduler = null;

            foreach (var soc in UdpSockets.Values)
                soc.Close();
            UdpSockets.Clear();

            foreach (var session in SessionsInternal.Values)
                session.Channel?.CloseAsync();
            SessionsInternal.Clear();
            MagicNumberSessions.Clear();
            UdpSessions.Clear();

            Channel?.CloseAsync();
            Channel = null;
            EventLoopGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            EventLoopGroup = null;

            base.Dispose();
        }

        internal void OnSessionConnectionStateChanged(NetSession session, ConnectionState previousState,
            ConnectionState newState)
        {
            var args = new SessionConnectionStateChangedEventArgs(session.HostId, previousState, newState);
            SessionConnectionStateChanged?.Invoke(this, args);

            switch (newState)
            {
                case ConnectionState.Connecting:
                    OnSessionConnecting(session);
                    break;
                case ConnectionState.Handshaking:
                    OnSessionHandshaking(session);
                    break;
                case ConnectionState.Connected:
                    OnSessionConnected(session);
                    break;
                case ConnectionState.Disconnected:
                    OnSessionDisconnected(session);
                    break;
            }
        }

        private void StartConnectTimeoutScheduler()
        {
            _connectTimeoutScheduler = EventLoopScheduler.StartIfNeeded(
                _connectTimeoutScheduler,
                ConnectTimeoutCheckInterval,
                CheckConnectTimeouts,
                Channel?.EventLoop);
        }

        private void StartRetryUdpOrHolepunchScheduler()
        {
            _retryUdpOrHolepunchScheduler = EventLoopScheduler.StartIfNeeded(
                _retryUdpOrHolepunchScheduler,
                TimeSpan.FromMilliseconds(HolepunchConfig.RetryIntervalMs),
                RetryUdpOrHolepunchIfRequired,
                Channel?.EventLoop);
        }

        internal void InitiateUdpSetup(NetSession session)
        {
            if (!UdpEnabled)
                return;

            if (session.UdpEnabled)
                return;

            if (session.UdpSocket != null)
                return;

            session.Logger.Debug("Initiating UDP setup for hostId = {HostId}", session.HostId);

            SendUdpSocketRequest(session);
        }

        internal UdpSocket GetRandomUdpSocket()
        {
            int socketIndex = Random.Shared.Next(_udpSocketsCache.Length);
            return _udpSocketsCache[socketIndex];
        }

        internal void InitiateP2PConnections(NetSession session)
        {
            if (!AllowDirectP2P)
                return;

            if (NetSettings.DirectP2PStartCondition == DirectP2PStartCondition.Jit)
                return;

            if (!session.UdpEnabled)
                return;

            if (session.P2PGroup == null)
                return;

            if (!session.P2PGroup.P2PMembersInternal.TryGetValue(session.HostId, out var member))
                return;

            foreach (var stateToTarget in member.ConnectionStates.Values)
            {
                stateToTarget.RemotePeer.ConnectionStates.TryGetValue(session.HostId, out var stateFromTarget);

                if (stateFromTarget == null)
                    continue;

                if (!stateToTarget.IsJoined || !stateFromTarget.IsJoined)
                    continue;
                if (!stateToTarget.RemotePeer.Session.UdpEnabled)
                    continue;

                bool shouldInitialize;
                lock (stateToTarget.StateLock)
                {
                    shouldInitialize = !stateToTarget.IsInitialized;
                }

                if (!shouldInitialize)
                    continue;

                session.Logger.Debug("Initiating immediate P2P connection between {HostId} and {TargetHostId}",
                    session.HostId, stateToTarget.RemotePeer.Session.HostId);

                InitializeP2PConnection(session, stateToTarget, stateFromTarget);
            }
        }

        internal void ProcessPendingPeerHolepunchRequests(NetSession session)
        {
            while (session.PendingPeerHolepunchRequests.TryDequeue(out var request))
            {
                if (request.SenderSession.IsDisposed)
                    continue;

                IPEndPoint capturedSenderUdpEndpoint;
                lock (request.SenderSession.UdpHolepunchLock)
                {
                    if (!request.SenderSession.UdpEnabled)
                        continue;

                    if (request.SenderSession.UdpSocket?.Channel == null)
                        continue;

                    var senderUdpEndpoint = request.SenderSession.UdpEndPoint;
                    if (senderUdpEndpoint == null)
                        continue;
                    capturedSenderUdpEndpoint = senderUdpEndpoint;
                }

                session.Logger.Debug(
                    "ProcessPendingPeerHolepunchRequests => processing queued request from hostId = {SenderHostId}, magicNumber = {MagicNumber}",
                    request.SenderSession.HostId,
                    request.MagicNumber);

                var peerUdpServerHolepunchAckMsg = new PeerUdp_ServerHolepunchAck
                {
                    MagicNumber = request.MagicNumber,
                    EndPoint = capturedSenderUdpEndpoint,
                    TargetHostId = session.HostId
                }.Serialize();
                request.SenderSession.NexumToClient(peerUdpServerHolepunchAckMsg);
                HolepunchHelper.SendBurstMessages(
                    peerUdpServerHolepunchAckMsg,
                    msg => request.SenderSession.NexumToClient(msg)
                );
            }
        }

        internal void EnsureP2PConnectionInitialized(NetSession session, P2PConnectionState stateToTarget,
            P2PConnectionState stateFromTarget)
        {
            InitializeP2PConnection(session, stateToTarget, stateFromTarget);
        }

        private void CheckConnectTimeouts()
        {
            if (Channel == null)
                return;

            foreach (var session in SessionsInternal.Values)
            {
                if (!session.IsConnected)
                {
                    session.Dispose();
                    continue;
                }

                if (session.ConnectionState == ConnectionState.Handshaking && !session.ConnectTimeoutSent)
                {
                    double timeSinceCreated = session.GetAbsoluteTime() - session.CreatedTime;

                    if (timeSinceCreated >= NetConfig.ConnectTimeout)
                    {
                        session.Logger.Warning(
                            "Connect timeout for hostId = {HostId} ({TimeSinceCreated:F1}s since connection, timeout = {Timeout}s)",
                            session.HostId, timeSinceCreated, NetConfig.ConnectTimeout);

                        session.ConnectTimeoutSent = true;

                        var connectTimedoutMsg = new ConnectServerTimedout();
                        session.NexumToClient(connectTimedoutMsg);

                        session.Dispose();
                    }
                }
            }
        }

        private void RetryUdpOrHolepunchIfRequired()
        {
            if (Channel == null)
                return;

            if (!UdpEnabled)
                return;

            var now = DateTimeOffset.Now;
            foreach (var group in P2PGroupsInternal.Values)
            foreach (var member in group.P2PMembersInternal.Values)
            {
                var session = member.Session;
                if (session.UdpSocket != null)
                {
                    var timeSinceLastPing = now - session.LastUdpPing;
                    var timeSinceSetupAttempt = now - session.LastUdpSetupAttempt;
                    int backoffSeconds =
                        Math.Min(
                            (int)HolepunchConfig.UdpSetupRetrySeconds *
                            (1 << (int)session.UdpRetryCount), 30);

                    if (!session.UdpEnabled)
                    {
                        if (timeSinceSetupAttempt >= TimeSpan.FromSeconds(backoffSeconds) &&
                            session.UdpRetryCount < HolepunchConfig.MaxRetryAttempts)
                        {
                            session.UdpRetryCount++;

                            MagicNumberSessions.TryRemove(session.HolepunchMagicNumber, out _);

                            lock (session.UdpInitLock)
                            {
                                if (session.UdpSessionInitialized)
                                {
                                    session.UdpSessionInitialized = false;
                                    session.UdpEndPointInternal = null;
                                }
                            }

                            UdpSessions.TryRemove(
                                FilterTag.Create(session.HostId, (uint)HostId.Server),
                                out _);

                            session.Logger.Debug("Retrying UDP setup for {HostId} (last attempt {Seconds}s ago)",
                                session.HostId, (int)timeSinceSetupAttempt.TotalSeconds);

                            SendUdpSocketRequest(session);
                        }
                    }
                    else if (timeSinceLastPing >= TimeSpan.FromSeconds(HolepunchConfig.UdpPingTimeoutSeconds))
                    {
                        session.Logger.Debug("Fallback to TCP relay by server => {HostId}", session.HostId);
                        session.ResetUdp();
                        UdpSessions.TryRemove(FilterTag.Create(session.HostId, (uint)HostId.Server), out _);
                        session.RmiToClient(new NotifyUdpToTcpFallbackByServer());
                    }
                }

                if (!AllowDirectP2P)
                    continue;

                foreach (var stateToTarget in member.ConnectionStates.Values)
                {
                    stateToTarget.RemotePeer.ConnectionStates.TryGetValue(session.HostId, out var stateFromTarget);

                    if (stateFromTarget == null)
                        continue;

                    if (!stateToTarget.IsJoined || !stateFromTarget.IsJoined)
                        continue;

                    if (!stateToTarget.RemotePeer.Session.UdpEnabled || !session.UdpEnabled)
                        continue;

                    bool isInitialized = false;
                    bool shouldRetry = false;
                    bool sessionNeedsRenew = false;
                    bool targetNeedsRenew = false;

                    HolepunchHelper.WithOrderedLocks(
                        session.HostId,
                        stateToTarget.RemotePeer.Session.HostId,
                        stateToTarget.StateLock,
                        stateFromTarget.StateLock,
                        () =>
                        {
                            isInitialized = stateToTarget.IsInitialized;
                            if (isInitialized)
                            {
                                var diff = now - stateToTarget.LastHolepunch;
                                int backoffSeconds =
                                    Math.Min(
                                        (int)HolepunchConfig.P2PSetupRetrySeconds *
                                        (1 << (int)stateToTarget.RetryCount), 60);

                                if (stateToTarget.HolepunchSuccess)
                                    return;

                                if (diff >= TimeSpan.FromSeconds(backoffSeconds) &&
                                    stateToTarget.RetryCount < HolepunchConfig.MaxRetryAttempts)
                                {
                                    shouldRetry = true;
                                    stateToTarget.RetryCount++;
                                    stateFromTarget.RetryCount++;

                                    stateToTarget.JitTriggered = stateFromTarget.JitTriggered = false;
                                    stateToTarget.NewConnectionSent = stateFromTarget.NewConnectionSent = false;
                                    stateToTarget.EstablishSent = stateFromTarget.EstablishSent = false;

                                    sessionNeedsRenew = true;
                                    targetNeedsRenew = true;

                                    stateToTarget.PeerUdpHolepunchSuccess = false;
                                    stateFromTarget.PeerUdpHolepunchSuccess = false;
                                    stateToTarget.LastHolepunch = stateFromTarget.LastHolepunch = now;
                                }
                            }
                        });

                    if (isInitialized && shouldRetry)
                    {
                        if (sessionNeedsRenew)
                        {
                            session.Logger.Debug(
                                "Trying to reconnect P2P to {TargetHostId} {RetryCount}/{MaxCount}",
                                stateToTarget.RemotePeer.Session.HostId, stateToTarget.RetryCount, HolepunchConfig
                                    .MaxRetryAttempts);
                            SendRenewP2PConnectionState(session, stateToTarget.RemotePeer.Session.HostId);
                        }

                        if (targetNeedsRenew)
                        {
                            stateToTarget.RemotePeer.Session.Logger.Debug(
                                "Trying to reconnect P2P to {TargetHostId} {RetryCount}/{MaxCount}",
                                session.HostId, stateToTarget.RetryCount, HolepunchConfig.MaxRetryAttempts);
                            SendRenewP2PConnectionState(stateToTarget.RemotePeer.Session, session.HostId);
                        }
                    }
                    else if (!isInitialized)
                    {
                        if (NetSettings.DirectP2PStartCondition == DirectP2PStartCondition.Jit)
                            continue;

                        session.Logger.Debug("Initialize P2P with {TargetHostId}",
                            stateToTarget.RemotePeer.Session.HostId);
                        stateToTarget.RemotePeer.Session.Logger.Debug("Initialize P2P with {TargetHostId}",
                            session.HostId);

                        InitializeP2PConnection(session, stateToTarget, stateFromTarget);
                    }
                }
            }
        }

        private void SendUdpSocketRequest(NetSession session)
        {
            session.LastUdpSetupAttempt = DateTimeOffset.Now;

            var socket = GetRandomUdpSocket();

            session.UdpSocket = socket;
            session.HolepunchMagicNumber = Guid.NewGuid();
            MagicNumberSessions.TryAdd(session.HolepunchMagicNumber, session);

            session.RmiToClient(new S2C_RequestCreateUdpSocket
            {
                UdpSocket = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)socket.Channel.LocalAddress).Port)
            });
        }

        private static void InitializeP2PConnection(NetSession session, P2PConnectionState stateToTarget,
            P2PConnectionState stateFromTarget)
        {
            var targetSession = stateToTarget.RemotePeer.Session;

            bool shouldInitialize = HolepunchHelper.WithOrderedLocks(
                session.HostId,
                targetSession.HostId,
                stateToTarget.StateLock,
                stateFromTarget.StateLock,
                () =>
                {
                    if (stateToTarget.IsInitialized)
                        return false;

                    stateToTarget.LastHolepunch = stateFromTarget.LastHolepunch = DateTimeOffset.Now;
                    stateToTarget.IsInitialized = stateFromTarget.IsInitialized = true;
                    return true;
                });

            if (!shouldInitialize)
                return;
            bool canRecycle = false;

            var recycleToTarget = default(P2PRecycleInfo);
            var recycleToSession = default(P2PRecycleInfo);

            if (session.UdpEnabled && targetSession.UdpEnabled &&
                stateToTarget.LocalPortReuseSuccess && stateFromTarget.LocalPortReuseSuccess &&
                session.LastSuccessfulP2PRecycleInfos.TryGetValue(targetSession.HostId, out recycleToTarget) &&
                targetSession.LastSuccessfulP2PRecycleInfos.TryGetValue(session.HostId, out recycleToSession) &&
                session.UdpLocalEndPoint != null && session.UdpEndPoint != null &&
                targetSession.UdpLocalEndPoint != null && targetSession.UdpEndPoint != null &&
                recycleToTarget.SendAddr != null && recycleToTarget.RecvAddr != null &&
                recycleToSession.SendAddr != null && recycleToSession.RecvAddr != null)
            {
                var now = DateTimeOffset.UtcNow;
                var recycleWindow = TimeSpan.FromSeconds(HolepunchConfig.NatPortRecycleReuseSeconds);
                bool withinWindow = now - recycleToTarget.Timestamp <= recycleWindow &&
                                    now - recycleToSession.Timestamp <= recycleWindow;

                canRecycle = withinWindow;
            }

            if (canRecycle)
            {
                HolepunchHelper.WithOrderedLocks(
                    session.HostId,
                    targetSession.HostId,
                    stateToTarget.StateLock,
                    stateFromTarget.StateLock,
                    () =>
                    {
                        stateToTarget.PeerUdpHolepunchSuccess = true;
                        stateFromTarget.PeerUdpHolepunchSuccess = true;
                        stateToTarget.HolepunchSuccess = true;
                        stateFromTarget.HolepunchSuccess = true;
                        stateToTarget.EstablishSent = true;
                        stateFromTarget.EstablishSent = true;
                    });

                session.Logger.Debug("P2PRecycleComplete => recycled=true for targetHostId = {TargetHostId}",
                    targetSession.HostId);
                targetSession.Logger.Debug("P2PRecycleComplete => recycled=true for targetHostId = {TargetHostId}",
                    session.HostId);

                SendP2PRecycleComplete(
                    session,
                    targetSession.HostId,
                    true,
                    targetSession.UdpLocalEndPoint,
                    targetSession.UdpEndPoint,
                    recycleToTarget.SendAddr,
                    recycleToTarget.RecvAddr);
                SendP2PRecycleComplete(
                    targetSession,
                    session.HostId,
                    true,
                    session.UdpLocalEndPoint,
                    session.UdpEndPoint,
                    recycleToSession.SendAddr,
                    recycleToSession.RecvAddr);
            }
            else
            {
                SendP2PRecycleComplete(session, targetSession.HostId, false,
                    DummyEndPoint, DummyEndPoint, DummyEndPoint, DummyEndPoint);
                SendP2PRecycleComplete(targetSession, session.HostId, false,
                    DummyEndPoint, DummyEndPoint, DummyEndPoint, DummyEndPoint);
            }
        }

        private static void SendP2PRecycleComplete(NetSession session, uint targetHostId, bool recycled,
            IPEndPoint internalAddr, IPEndPoint externalAddr, IPEndPoint sendAddr, IPEndPoint recvAddr)
        {
            session.RmiToClient(new P2PRecycleComplete
            {
                HostId = targetHostId,
                Recycled = recycled,
                InternalAddr = internalAddr ?? DummyEndPoint,
                ExternalAddr = externalAddr ?? DummyEndPoint,
                SendAddr = sendAddr ?? DummyEndPoint,
                RecvAddr = recvAddr ?? DummyEndPoint
            });
        }

        private static void SendRenewP2PConnectionState(NetSession session, uint targetHostId)
        {
            session.RmiToClient(new RenewP2PConnectionState { HostId = targetHostId });
        }
    }
}
