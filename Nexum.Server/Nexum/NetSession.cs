using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nexum.Core;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Server
{
    public class NetSession : IDisposable
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        internal readonly ConcurrentQueue<PendingPeerHolepunchRequest> PendingPeerHolepunchRequests =
            new ConcurrentQueue<PendingPeerHolepunchRequest>();

        internal volatile bool IsDisposed;

        internal NetSession(ServerType serverType, uint hostId, IChannel channel)
        {
            HostId = hostId;
            Channel = (ISocketChannel)channel;
            var remoteEndPoint = (IPEndPoint)Channel.RemoteAddress;
            RemoteEndPoint = new IPEndPoint(remoteEndPoint.Address.MapToIPv4(), remoteEndPoint.Port);

            var localEndPoint = (IPEndPoint)Channel.LocalAddress;
            LocalEndPoint = new IPEndPoint(localEndPoint.Address.MapToIPv4(), localEndPoint.Port);

            UdpDefragBoard = new UdpPacketDefragBoard { LocalHostId = (uint)Core.HostId.Server };

            UdpFragBoard.DefragBoard = UdpDefragBoard;

            Logger = Log.ForContext("HostId", HostId).ForContext("EndPoint", RemoteEndPoint.Address.ToString())
                .ForContext(Constants.SourceContextPropertyName, serverType + "Session");
        }

        public uint HostId { get; }

        public IPEndPoint LocalEndPoint { get; }

        public IPEndPoint RemoteEndPoint { get; }

        public P2PGroup P2PGroup { get; internal set; }

        public IPEndPoint UdpEndPoint { get; private set; }

        public IPEndPoint UdpLocalEndPoint { get; private set; }

        public bool IsConnected => Channel != null && Channel.Open && Channel.Active;

        public ILogger Logger { get; }

        internal object RecvLock { get; } = new object();

        internal object SendLock { get; } = new object();

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

        internal double ClientUdpLastPing { get; set; }

        internal double ClientUdpRecentPing { get; set; }

        internal bool UdpEnabled { get; set; }

        internal bool UdpSessionInitialized { get; set; }

        internal UdpSocket UdpSocket { get; set; }

        internal IPEndPoint UdpEndPointInternal
        {
            set => UdpEndPoint = value;
        }

        internal IPEndPoint UdpLocalEndPointInternal
        {
            set => UdpLocalEndPoint = value;
        }

        public void Dispose()
        {
            IsDisposed = true;
            Logger.Information("Session disposed for hostId = {HostId}", HostId);
            P2PGroup?.Leave(this);

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

        public double GetAbsoluteTime()
        {
            return _stopwatch.Elapsed.TotalSeconds;
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
            Logger.Information("Client reliable UDP initialized with firstFrameNumber = {FirstFrameNumber}",
                firstFrameNumber);
        }

        private void SendReliableUdpFrameToClient(ReliableUdpFrame frame)
        {
            var msg = ReliableUdpHelper.BuildFrameMessage(frame);
            ToClientUdp(msg);
        }

        internal void ResetToClientReliableUdp()
        {
            if (ToClientReliableUdp != null)
            {
                ToClientReliableUdp.OnFailed -= OnToClientReliableUdpFailed;
                ToClientReliableUdp = null;
            }
        }

        private void OnToClientReliableUdpFailed()
        {
            Logger.Warning(
                "ToClientReliableUdp failed after max retries, falling back to TCP - retry will be triggered by server");

            ToClientReliableUdp.OnFailed -= OnToClientReliableUdpFailed;
            ToClientReliableUdp = null;

            UdpEnabled = false;

            RmiToClient((ushort)NexumOpCode.NotifyUdpToTcpFallbackByServer, new NetMessage());
        }

        internal void ReliableUdpFrameMove(double elapsedTime)
        {
            double currentTime = GetAbsoluteTime();
            ToClientReliableUdp?.FrameMove(elapsedTime);
            UdpDefragBoard.PruneStalePackets(currentTime);
        }

        public void RmiToClient(ushort rmiId, NetMessage message, EncryptMode ecMode = EncryptMode.Secure)
        {
            var data = new NetMessage();
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;

            data.WriteEnum(MessageType.RMI);
            data.Write(rmiId);
            data.Write(message);
            NexumToClient(data);
        }

        public void NexumToClient(NetMessage data)
        {
            lock (SendLock)
            {
                if (IsDisposed)
                    return;

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
                ToClient(message);
            }
        }

        private void ToClient(NetMessage message)
        {
            var buffer = Unpooled.WrappedBuffer(message.GetBufferSpan().ToArray());
            Channel.WriteAndFlushAsync(buffer);
        }

        public void RmiToClientUdpIfAvailable(ushort rmiId, NetMessage message, EncryptMode ecMode = EncryptMode.Fast,
            bool reliable = false)
        {
            var data = new NetMessage();
            if (ecMode != EncryptMode.None)
                message.EncryptMode = ecMode;

            data.WriteEnum(MessageType.RMI);
            data.Write(rmiId);
            data.Write(message);

            NexumToClientUdpIfAvailable(data, reliable: reliable);
        }

        public void NexumToClientUdpIfAvailable(NetMessage data, bool force = false, bool reliable = false)
        {
            lock (SendLock)
            {
                if (IsDisposed)
                    return;

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
        }

        private void ToClientUdp(NetMessage message)
        {
            var channel = UdpSocket?.Channel;
            if (channel == null || !channel.Active || UdpEndPoint == null)
            {
                Logger.Verbose("UDP Channel not ready for hostId = {HostId}, packet dropped", HostId);
                return;
            }

            byte[] data = message.GetBufferSpan().ToArray();
            foreach (var udpMessage in
                     UdpFragBoard.FragmentPacket(data, data.Length, (uint)Core.HostId.Server, HostId))
            {
                udpMessage.EndPoint = UdpEndPoint;
                channel.WriteAndFlushAsync(udpMessage);
            }
        }
    }
}
