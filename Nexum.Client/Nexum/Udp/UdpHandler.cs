using System.Net;
using DotNetty.Transport.Channels;
using Nexum.Client.Core;
using Nexum.Client.P2P;
using Nexum.Core.Configuration;
using Nexum.Core.DotNetty.Codecs;
using Nexum.Core.Fragmentation;
using Nexum.Core.ReliableUdp;
using Nexum.Core.Routing;
using Nexum.Core.Serialization;
using Nexum.Core.Udp;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Client.Udp
{
    internal sealed class UdpHandler : ChannelHandlerAdapter
    {
        private readonly ILogger _logger;

        private readonly NetClient _owner;

        internal UdpHandler(NetClient owner, int port)
        {
            _owner = owner;
            _logger = Log.ForContext(Constants.SourceContextPropertyName, $"{nameof(UdpHandler)}({port})");
        }

        public override void ChannelRead(IChannelHandlerContext context, object obj)
        {
            switch (obj)
            {
                case InboundReliableUdpFrame reliableFrame:
                    HandleReliableUdpFrame(context, reliableFrame);
                    return;

                case InboundUdpMessage inboundMsg:
                    HandleInboundUdpMessage(context, inboundMsg);
                    return;

                case AssembledPacket assembledPacket:
                    HandleAssembledPacket(context, assembledPacket);
                    return;

                default:
                    _logger.Warning("Received unknown message type: {Type}", obj?.GetType().Name);
                    return;
            }
        }

        private void HandleReliableUdpFrame(IChannelHandlerContext context, InboundReliableUdpFrame reliableFrame)
        {
            var endPoint = reliableFrame.SenderEndPoint;
            ushort filterTag = reliableFrame.FilterTag;

            if (IsFromServer(endPoint, filterTag))
            {
                if (_owner.ServerReliableUdp != null)
                {
                    _owner.ServerReliableUdp.TakeReceivedFrame(reliableFrame.Frame);
                    ExtractMessagesFromServerReliableUdpStream(filterTag, endPoint);
                }
                else
                {
                    if (reliableFrame.Frame.Type == ReliableUdpFrameType.Data && reliableFrame.Frame.Data != null)
                        if (ReliableUdpHelper.UnwrapPayload(reliableFrame.Frame.Data, out var payload))
                        {
                            var innerMessage = new NetMessage(payload.ToArray(), true);
                            NetClientHandler.ReadMessage(_owner, innerMessage, filterTag, endPoint);
                        }
                }
            }
            else
            {
                var sourceMember =
                    _owner.P2PGroup?.FindMember(_owner.HostId, endPoint, filterTag);
                if (sourceMember == null)
                {
                    _logger.Verbose(
                        "ReliableUdp_Frame from unknown source, ignoring => udpEndPoint = {UdpEndPoint}, filterTag = {FilterTag}",
                        endPoint, filterTag);
                    return;
                }

                sourceMember.ProcessReceivedReliableUdpFrame(reliableFrame.Frame);
                ExtractMessagesFromP2PReliableUdpStream(sourceMember, filterTag, endPoint);
            }
        }

        private void HandleInboundUdpMessage(IChannelHandlerContext context, InboundUdpMessage inboundMsg)
        {
            var assembledPacket = inboundMsg.AssembledPacket;
            var endPoint = assembledPacket.SenderEndPoint;

            inboundMsg.NetMessage.ReadOffset = 0;
            NetClientHandler.ReadFrame(_owner, inboundMsg.NetMessage, assembledPacket.FilterTag, endPoint, true);
        }

        private void HandleAssembledPacket(IChannelHandlerContext context, AssembledPacket assembledPacket)
        {
            var endPoint = assembledPacket.SenderEndPoint;
            var assembledMessage = new NetMessage(assembledPacket.Packet.AssembledData, true);
            NetClientHandler.ReadFrame(_owner, assembledMessage, assembledPacket.FilterTag, endPoint, true);
        }

        private bool IsFromServer(IPEndPoint endPoint, ushort filterTag)
        {
            return (_owner.ServerUdpSocket != null && endPoint.Equals(_owner.ServerUdpSocket)) ||
                   FilterTag.Create((uint)HostId.Server, _owner.HostId) == filterTag;
        }

        private void ExtractMessagesFromServerReliableUdpStream(ushort filterTag, IPEndPoint udpEndPoint)
        {
            var stream = _owner.ServerReliableUdp?.ReceivedStream;
            ReliableUdpHelper.ExtractMessagesFromStream(stream,
                msg => NetClientHandler.ReadMessage(_owner, msg, filterTag, udpEndPoint));
        }

        private void ExtractMessagesFromP2PReliableUdpStream(P2PMember sourceMember, ushort filterTag,
            IPEndPoint udpEndPoint)
        {
            var stream = sourceMember.ToPeerReliableUdp?.ReceivedStream;
            ReliableUdpHelper.ExtractMessagesFromStream(stream,
                msg => NetClientHandler.ReadMessage(_owner, msg, filterTag, udpEndPoint));
        }
    }
}
