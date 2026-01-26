using System.Threading.Tasks;
using DotNetty.Transport.Channels;
using Nexum.Client.Core;
using Nexum.Core.Fragmentation;
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
            var message = obj as UdpMessage;
            var content = message.Content;

            double currentTime = _owner.GetAbsoluteTime();

            UdpPacketDefragBoard defragBoard;
            uint srcHostId;

            if ((_owner.ServerUdpSocket != null && message.EndPoint.Equals(_owner.ServerUdpSocket)) ||
                FilterTag.Create((uint)HostId.Server, _owner.HostId) == message.FilterTag)
            {
                defragBoard = _owner.UdpDefragBoard;
                srcHostId = (uint)HostId.Server;
            }
            else
            {
                var p2pMember = _owner.P2PGroup?.FindMember(_owner.HostId, message.EndPoint, message.FilterTag);

                if (p2pMember != null)
                {
                    defragBoard = p2pMember.UdpDefragBoard;
                    srcHostId = p2pMember.HostId;
                }
                else
                {
                    defragBoard = _owner.UdpDefragBoard;
                    srcHostId = (uint)HostId.None;
                }
            }

            var result = defragBoard.PushFragment(
                message,
                srcHostId,
                currentTime,
                out var assembledPacket,
                out string error);

            if (result == AssembledPacketError.Assembling)
            {
                content.Release();
                return;
            }

            if (result == AssembledPacketError.Error)
            {
                _logger.Warning("UDP defragmentation error: {Error}", error);
                content.Release();
                return;
            }

            var assembledMessage = new NetMessage(assembledPacket.Packet.AssembledData, true);

            NetClientHandler.ReadFrame(_owner, assembledMessage, message.FilterTag, message.EndPoint, true);

            content.Release();
        }

        public override Task WriteAsync(IChannelHandlerContext context, object message)
        {
            var udpMessage = message as UdpMessage;
            return base.WriteAsync(context, udpMessage);
        }
    }
}
