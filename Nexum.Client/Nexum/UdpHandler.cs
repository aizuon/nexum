using System.Threading.Tasks;
using DotNetty.Transport.Channels;
using Nexum.Core;
using Serilog;

namespace Nexum.Client
{
    internal class UdpHandler : ChannelHandlerAdapter
    {
        private static readonly ILogger Logger = Log.ForContext<UdpHandler>();
        private readonly NetClient _owner;

        public UdpHandler(NetClient owner)
        {
            _owner = owner;
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
                P2PMember p2pMember = null;
                if (_owner.P2PGroup != null)
                    foreach (var member in _owner.P2PGroup.P2PMembers.Values)
                    {
                        if (member.PeerLocalToRemoteSocket != null &&
                            member.PeerLocalToRemoteSocket.Equals(message.EndPoint))
                        {
                            p2pMember = member;
                            break;
                        }

                        if (member.PeerRemoteToLocalSocket != null &&
                            member.PeerRemoteToLocalSocket.Equals(message.EndPoint))
                        {
                            p2pMember = member;
                            break;
                        }

                        if (FilterTag.Create(member.HostId, _owner.HostId) == message.FilterTag)
                        {
                            p2pMember = member;
                            break;
                        }
                    }

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
                Logger.Warning("UDP defragmentation error: {Error}", error);
                content.Release();
                return;
            }

            var netMessage = new NetMessage(assembledPacket.Packet.AssembledData,
                assembledPacket.Packet.AssembledData.Length);

            NetClientHandler.ReadFrame(_owner, netMessage, message.EndPoint, true);

            content.Release();
        }

        public override Task WriteAsync(IChannelHandlerContext context, object message)
        {
            var udpMessage = message as UdpMessage;
            return base.WriteAsync(context, udpMessage);
        }
    }
}
