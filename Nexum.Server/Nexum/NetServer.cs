using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BaseLib;
using BaseLib.Extensions;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nexum.Core;
using Nexum.Core.DotNetty.Codecs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Security;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Server
{
    public class NetServer : NetCore
    {
        public delegate void OnRMIReceiveDelegate(NetSession session, NetMessage message, ushort rmiId);

        public delegate void OnSessionConnectedDelegate(NetSession session);

        public delegate void OnSessionConnectingDelegate(NetSession session);

        public delegate void OnSessionDisconnectedDelegate(NetSession session);

        public delegate void OnSessionHandshakingDelegate(NetSession session);

        private static readonly TimeSpan UdpSetupRetryInterval =
            TimeSpan.FromSeconds(HolepunchConfig.UdpSetupRetrySeconds);

        private static readonly TimeSpan
            UdpPingTimeout = TimeSpan.FromSeconds(HolepunchConfig.UdpPingTimeoutSeconds);

        private static readonly IPEndPoint DummyEndPoint = new IPEndPoint(IPAddress.Parse("255.255.255.255"), 65535);
        private readonly object _udpSocketsCacheLock = new object();

        private ThreadLoop _reliableUdpLoop;
        private UdpSocket[] _udpSocketsCache;

        public OnRMIReceiveDelegate OnRMIReceive = (_, _, _) => { };

        public OnSessionConnectedDelegate OnSessionConnected = _ => { };

        public OnSessionConnectingDelegate OnSessionConnecting = _ => { };

        public OnSessionDisconnectedDelegate OnSessionDisconnected = _ => { };

        public OnSessionHandshakingDelegate OnSessionHandshaking = _ => { };

        internal Guid ServerGuid = Guid.NewGuid();

        public NetServer(ServerType serverType, NetSettings netSettings = null,
            bool allowDirectP2P = true)
        {
            ServerType = serverType;
            Logger = Log.ForContext(Constants.SourceContextPropertyName, ServerType + "Server");
            RSA = new RSACryptoServiceProvider(2048);
            NetSettings = CreateNetSettings(netSettings);
            AllowDirectP2P = allowDirectP2P;

            if (ServerType == ServerType.Relay)
                P2PGroupsInternal = new ConcurrentDictionary<uint, P2PGroup>();
        }

        internal bool UdpEnabled => UdpSockets.Count > 0;

        public IReadOnlyDictionary<uint, NetSession> Sessions => SessionsInternal;

        public IReadOnlyDictionary<uint, P2PGroup> P2PGroups => P2PGroupsInternal;

        internal ConcurrentDictionary<uint, NetSession> SessionsInternal { get; } =
            new ConcurrentDictionary<uint, NetSession>();

        internal ConcurrentDictionary<uint, P2PGroup> P2PGroupsInternal { get; }

        internal new NetSettings NetSettings { get; }

        internal bool AllowDirectP2P { get; }

        internal HostIdFactory HostIdFactory { get; } = new HostIdFactory();

        internal ConcurrentDictionary<Guid, NetSession> MagicNumberSessions { get; } =
            new ConcurrentDictionary<Guid, NetSession>();

        internal ConcurrentDictionary<ushort, NetSession> UdpSessions { get; } =
            new ConcurrentDictionary<ushort, NetSession>();

        internal ConcurrentDictionary<uint, UdpSocket> UdpSockets { get; } =
            new ConcurrentDictionary<uint, UdpSocket>();

        internal UdpPacketDefragBoard UdpDefragBoard { get; } = new UdpPacketDefragBoard
        {
            LocalHostId = (uint)HostId.Server
        };

        internal IPAddress IPAddress { get; set; }

        internal IEventLoopGroup EventLoopGroup { get; set; }

        public event EventHandler<SessionConnectionStateChangedEventArgs> SessionConnectionStateChanged;

        public void ImportRsaKey(string xmlString)
        {
            if (string.IsNullOrWhiteSpace(xmlString))
                throw new ArgumentNullException(nameof(xmlString));

            var newRsa = new RSACryptoServiceProvider(2048);
            newRsa.FromXmlString(xmlString);

            RSA?.Dispose();
            RSA = newRsa;

            Logger.Information("RSA key imported from XML format");
        }

        public void ImportRsaKey(RSAParameters parameters)
        {
            var newRsa = new RSACryptoServiceProvider(2048);
            newRsa.ImportParameters(parameters);

            RSA?.Dispose();
            RSA = newRsa;

            Logger.Information("RSA key imported from RSAParameters");
        }

        public void ImportRsaPrivateKey(byte[] privateKey)
        {
            if (privateKey == null || privateKey.Length == 0)
                throw new ArgumentNullException(nameof(privateKey));

            var newRsa = new RSACryptoServiceProvider(2048);

            try
            {
                newRsa.ImportPkcs8PrivateKey(privateKey, out _);
            }
            catch
            {
                newRsa.ImportRSAPrivateKey(privateKey, out _);
            }

            RSA?.Dispose();
            RSA = newRsa;

            Logger.Information("RSA private key imported");
        }

        public void ImportRsaPrivateKeyFromPem(string pemString)
        {
            if (string.IsNullOrWhiteSpace(pemString))
                throw new ArgumentNullException(nameof(pemString));

            var newRsa = new RSACryptoServiceProvider(2048);
            newRsa.ImportFromPem(pemString.AsSpan());

            RSA?.Dispose();
            RSA = newRsa;

            Logger.Information("RSA private key imported from PEM");
        }

        public byte[] ExportRsaPublicKey()
        {
            var pubKey = DotNetUtilities.GetRsaPublicKey(RSA.ExportParameters(false));
            var pubKeyStruct = new RsaPublicKeyStructure(pubKey.Modulus, pubKey.Exponent);
            return pubKeyStruct.GetDerEncoded();
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
                .Channel<TcpServerSocketChannel>()
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
                .BindAsync(endPoint);

            if (udpListenerPorts != null)
            {
                foreach (uint port in udpListenerPorts)
                {
                    UdpSockets.TryAdd(port,
                        new UdpSocket(this, ((IPEndPoint)Channel.LocalAddress).Address.MapToIPv4(), port));
                    Logger.Debug("UDP listener started on port {UdpPort}", port);
                }

                lock (_udpSocketsCacheLock)
                {
                    _udpSocketsCache = UdpSockets.Values.ToArray();
                }
            }

            RetryUdpOrHolepunchIfRequired(this, null);

            Logger.Information("Listening on {Endpoint}", endPoint.ToIPv4String());

            Logger.Information(
                "Server started: DirectP2P={AllowDirectP2P}, UDP ports={UdpPortCount}, Encryption={EncryptionKeyLength}bit",
                AllowDirectP2P,
                udpListenerPorts?.Length ?? 0,
                NetSettings.EncryptedMessageKeyLength);
        }

        public override void Dispose()
        {
            Logger.Information("Shutting down {ServerType} server with {SessionCount} active sessions",
                ServerType, SessionsInternal.Count);

            _reliableUdpLoop?.Stop();
            _reliableUdpLoop = null;

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

        internal void StartReliableUdpLoop()
        {
            if (_reliableUdpLoop != null)
                return;

            _reliableUdpLoop = new ThreadLoop(
                TimeSpan.FromMilliseconds(ReliableUdpConfig.FrameMoveInterval * 1000),
                ReliableUdpFrameMove);
            _reliableUdpLoop.Start();
        }

        internal void InitiateUdpSetup(NetSession session)
        {
            if (!UdpEnabled)
                return;

            if (session.UdpEnabled)
                return;

            if (session.UdpSocket != null)
                return;

            Logger.Information("Initiating UDP setup for hostId = {HostId}", session.HostId);

            SendUdpSocketRequest(session);
        }

        internal UdpSocket GetRandomUdpSocket()
        {
            lock (_udpSocketsCacheLock)
            {
                if (_udpSocketsCache == null || _udpSocketsCache.Length != UdpSockets.Count)
                    _udpSocketsCache = UdpSockets.Values.ToArray();
                int socketIndex = Random.Shared.Next(_udpSocketsCache.Length);
                return _udpSocketsCache[socketIndex];
            }
        }

        internal void InitiateP2PConnections(NetSession session)
        {
            if (!AllowDirectP2P)
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
                if (!stateFromTarget.RemotePeer.Session.UdpEnabled)
                    continue;

                bool shouldInitialize;
                lock (stateToTarget.StateLock)
                {
                    shouldInitialize = !stateToTarget.IsInitialized;
                }

                if (!shouldInitialize)
                    continue;

                Logger.Debug("Initiating immediate P2P connection between {HostId} and {TargetHostId}",
                    session.HostId, stateToTarget.RemotePeer.Session.HostId);

                InitializeP2PConnection(session, stateToTarget, stateFromTarget);
            }
        }

        internal Task ScheduleAsync(Action<object, object> action, object context, object state, TimeSpan delay)
        {
            return EventLoopGroup.ScheduleAsync(action, context, state, delay);
        }

        private static NetSettings CreateNetSettings(NetSettings provided)
        {
            var settings = new NetSettings
            {
                EnableServerLog = false,
                FallbackMethod = FallbackMethod.None,
                MessageMaxLength = NetConfig.MessageMaxLength,
                IdleTimeout = NetConfig.NoPingTimeoutTime,
                DirectP2PStartCondition = DirectP2PStartCondition.Always,
                OverSendSuspectingThresholdInBytes = NetConfig.DefaultOverSendSuspectingThresholdInBytes,
                EnableNagleAlgorithm = true,
                EncryptedMessageKeyLength = NetCrypt.DefaultKeyLength,
                FastEncryptedMessageKeyLength = NetCrypt.DefaultFastKeyLength,
                AllowServerAsP2PGroupMember = false,
                EnableP2PEncryptedMessaging = false,
                UpnpDetectNatDevice = NetConfig.UpnpDetectNatDeviceByDefault,
                UpnpTcpAddrPortMapping = NetConfig.UpnpTcpAddrPortMappingByDefault,
                EnableLookaheadP2PSend = false,
                EnablePingTest = false,
                EmergencyLogLineCount = 0
            };

            if (provided == null)
                return settings;

            if (provided.EnableServerLog)
                settings.EnableServerLog = provided.EnableServerLog;
            if (provided.FallbackMethod != FallbackMethod.None)
                settings.FallbackMethod = provided.FallbackMethod;
            if (provided.MessageMaxLength != 0)
                settings.MessageMaxLength = provided.MessageMaxLength;
            if (provided.IdleTimeout != 0)
                settings.IdleTimeout = provided.IdleTimeout;
            if (provided.DirectP2PStartCondition != DirectP2PStartCondition.Always)
                settings.DirectP2PStartCondition = provided.DirectP2PStartCondition;
            if (provided.OverSendSuspectingThresholdInBytes != 0)
                settings.OverSendSuspectingThresholdInBytes = provided.OverSendSuspectingThresholdInBytes;
            if (!provided.EnableNagleAlgorithm)
                settings.EnableNagleAlgorithm = provided.EnableNagleAlgorithm;
            if (provided.EncryptedMessageKeyLength != 0)
                settings.EncryptedMessageKeyLength = provided.EncryptedMessageKeyLength;
            if (provided.FastEncryptedMessageKeyLength != 0)
                settings.FastEncryptedMessageKeyLength = provided.FastEncryptedMessageKeyLength;
            if (provided.AllowServerAsP2PGroupMember)
                settings.AllowServerAsP2PGroupMember = provided.AllowServerAsP2PGroupMember;
            if (provided.EnableP2PEncryptedMessaging)
                settings.EnableP2PEncryptedMessaging = provided.EnableP2PEncryptedMessaging;
            if (!provided.UpnpDetectNatDevice)
                settings.UpnpDetectNatDevice = provided.UpnpDetectNatDevice;
            if (!provided.UpnpTcpAddrPortMapping)
                settings.UpnpTcpAddrPortMapping = provided.UpnpTcpAddrPortMapping;
            if (provided.EnableLookaheadP2PSend)
                settings.EnableLookaheadP2PSend = provided.EnableLookaheadP2PSend;
            if (provided.EnablePingTest)
                settings.EnablePingTest = provided.EnablePingTest;
            if (provided.EmergencyLogLineCount != 0)
                settings.EmergencyLogLineCount = provided.EmergencyLogLineCount;

            return settings;
        }

        private void ReliableUdpFrameMove(TimeSpan delta)
        {
            double elapsedTime = delta.TotalSeconds;

            foreach (var session in SessionsInternal.Values)
                if (!session.IsDisposed)
                    session.ReliableUdpFrameMove(elapsedTime);
        }

        private static void RetryUdpOrHolepunchIfRequired(object context, object _)
        {
            if (context == null)
                return;

            var server = (NetServer)context;

            foreach (var session in server.SessionsInternal.Values)
                if (!session.IsConnected)
                    session.Dispose();

            if (!server.UdpEnabled)
                return;

            var now = DateTimeOffset.Now;
            foreach (var group in server.P2PGroupsInternal.Values)
            foreach (var member in group.P2PMembersInternal.Values)
            {
                var session = member.Session;
                if (session.UdpSocket != null)
                {
                    var timeSinceLastPing = now - session.LastUdpPing;
                    var timeSinceSetupAttempt = now - session.LastUdpSetupAttempt;

                    if (!session.UdpEnabled)
                    {
                        if (timeSinceSetupAttempt >= UdpSetupRetryInterval)
                        {
                            server.MagicNumberSessions.TryRemove(session.HolepunchMagicNumber, out var _);

                            session.Logger.Debug("Retrying UDP setup for {HostId} (last attempt {Seconds}s ago)",
                                session.HostId, (int)timeSinceSetupAttempt.TotalSeconds);

                            server.SendUdpSocketRequest(session);
                        }
                    }
                    else if (timeSinceLastPing >= UdpPingTimeout)
                    {
                        session.Logger.Debug("Fallback to TCP relay by server => {HostId}", session.HostId);
                        session.UdpEnabled = false;
                        server.UdpSessions.TryRemove(FilterTag.Create(session.HostId, (uint)HostId.Server), out var _);
                        session.RmiToClient((ushort)NexumOpCode.NotifyUdpToTcpFallbackByServer, new NetMessage());
                    }
                }

                if (!server.AllowDirectP2P)
                    continue;
                foreach (var stateToTarget in member.ConnectionStates.Values)
                {
                    stateToTarget.RemotePeer.ConnectionStates.TryGetValue(session.HostId, out var stateFromTarget);

                    if (stateFromTarget == null)
                        continue;

                    if (!stateToTarget.IsJoined || !stateFromTarget.IsJoined)
                        continue;

                    if (!stateFromTarget.RemotePeer.Session.UdpEnabled || !session.UdpEnabled)
                        continue;

                    bool isInitialized = false;
                    bool shouldRetry = false;

                    HolepunchHelper.WithOrderedLocks(
                        session.HostId,
                        stateFromTarget.RemotePeer.Session.HostId,
                        stateToTarget.StateLock,
                        stateFromTarget.StateLock,
                        () =>
                        {
                            isInitialized = stateToTarget.IsInitialized;
                            if (isInitialized)
                            {
                                var diff = now - stateToTarget.LastHolepunch;
                                int backoffSeconds = Math.Min(8 * (1 << (int)stateToTarget.RetryCount), 60);
                                if (!stateToTarget.HolepunchSuccess &&
                                    diff >= TimeSpan.FromSeconds(backoffSeconds) &&
                                    stateToTarget.RetryCount < HolepunchConfig.MaxRetryAttempts)
                                {
                                    shouldRetry = true;
                                    stateToTarget.RetryCount++;
                                    stateFromTarget.RetryCount++;

                                    stateToTarget.JitTriggered = stateFromTarget.JitTriggered = false;
                                    stateToTarget.NewConnectionSent = stateFromTarget.NewConnectionSent = false;
                                    stateToTarget.EstablishSent = stateFromTarget.EstablishSent = false;
                                    stateToTarget.PeerUdpHolepunchSuccess =
                                        stateFromTarget.PeerUdpHolepunchSuccess = false;
                                    stateToTarget.LastHolepunch = stateFromTarget.LastHolepunch = now;
                                }
                            }
                        });

                    if (isInitialized && shouldRetry)
                    {
                        session.Logger.Debug(
                            "Trying to reconnect P2P to {TargetHostId} {RetryCount}/{MaxCount}",
                            stateToTarget.RemotePeer.Session.HostId, stateToTarget.RetryCount, HolepunchConfig
                                .MaxRetryAttempts);
                        stateFromTarget.RemotePeer.Session.Logger.Debug(
                            "Trying to reconnect P2P to {TargetHostId} {RetryCount}/{MaxCount}",
                            session.HostId, stateFromTarget.RetryCount, HolepunchConfig.MaxRetryAttempts);

                        SendRenewP2PConnectionState(session, stateToTarget.RemotePeer.Session.HostId);
                        SendRenewP2PConnectionState(stateToTarget.RemotePeer.Session, session.HostId);
                    }
                    else if (!isInitialized)
                    {
                        session.Logger.Debug("Initialize P2P with {TargetHostId}",
                            stateToTarget.RemotePeer.Session.HostId);
                        stateFromTarget.RemotePeer.Session.Logger.Debug("Initialize P2P with {TargetHostId}",
                            session.HostId);

                        InitializeP2PConnection(session, stateToTarget, stateFromTarget);
                    }
                }
            }

            server.ScheduleAsync(RetryUdpOrHolepunchIfRequired, server, null,
                TimeSpan.FromMilliseconds(HolepunchConfig.RetryIntervalMs));
        }

        private void SendUdpSocketRequest(NetSession session)
        {
            session.LastUdpSetupAttempt = DateTimeOffset.Now;

            var socket = GetRandomUdpSocket();

            session.UdpSocket = socket;
            session.HolepunchMagicNumber = Guid.NewGuid();
            MagicNumberSessions.TryAdd(session.HolepunchMagicNumber, session);

            var message = new NetMessage();
            message.WriteStringEndPoint(new IPEndPoint(IPAddress.Loopback,
                ((IPEndPoint)socket.Channel.LocalAddress).Port));

            session.RmiToClient((ushort)NexumOpCode.S2C_RequestCreateUdpSocket, message);
        }

        private static void InitializeP2PConnection(NetSession session, P2PConnectionState stateToTarget,
            P2PConnectionState stateFromTarget)
        {
            bool shouldInitialize = HolepunchHelper.WithOrderedLocks(
                session.HostId,
                stateFromTarget.RemotePeer.Session.HostId,
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

            SendP2PRecycleComplete(session, stateToTarget.RemotePeer.Session.HostId);
            SendP2PRecycleComplete(stateToTarget.RemotePeer.Session, session.HostId);
        }

        private static void SendP2PRecycleComplete(NetSession session, uint targetHostId)
        {
            var message = new NetMessage();
            message.Write(targetHostId);
            message.Write(false);
            message.Write(DummyEndPoint);
            message.Write(DummyEndPoint);
            message.Write(DummyEndPoint);
            message.Write(DummyEndPoint);

            session.RmiToClient((ushort)NexumOpCode.P2PRecycleComplete, message);
        }

        private static void SendRenewP2PConnectionState(NetSession session, uint targetHostId)
        {
            var message = new NetMessage();
            message.Write(targetHostId);
            session.RmiToClient((ushort)NexumOpCode.RenewP2PConnectionState, message);
        }
    }
}
