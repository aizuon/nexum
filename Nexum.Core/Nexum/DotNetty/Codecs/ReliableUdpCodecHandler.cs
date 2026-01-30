using System.Threading.Tasks;
using BaseLib.Extensions;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Nexum.Core.Configuration;
using Nexum.Core.Fragmentation;
using Nexum.Core.ReliableUdp;
using Nexum.Core.Serialization;
using Nexum.Core.Udp;
using Serilog;
using SerilogConstants = Serilog.Core.Constants;

namespace Nexum.Core.DotNetty.Codecs
{
    internal sealed class ReliableUdpCodecHandler : ChannelDuplexHandler
    {
        private static readonly ILogger Logger =
            Log.ForContext(SerilogConstants.SourceContextPropertyName, nameof(ReliableUdpCodecHandler));

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            if (message is not AssembledPacket assembledPacket)
            {
                context.FireChannelRead(message);
                return;
            }

            var netMessage = new NetMessage(assembledPacket.Packet.AssembledData, true);

            if (!netMessage.Read<MessageType>(out var messageType))
            {
                netMessage.ReadOffset = 0;
                context.FireChannelRead(assembledPacket);
                return;
            }

            if (messageType != MessageType.ReliableUdp_Frame)
            {
                context.FireChannelRead(new InboundUdpMessage
                {
                    AssembledPacket = assembledPacket,
                    NetMessage = netMessage,
                    MessageType = messageType
                });
                return;
            }

            if (!ReliableUdpHelper.ParseFrame(netMessage, out var frame))
            {
                Logger.Warning("Failed to parse ReliableUdp_Frame from {Endpoint}",
                    assembledPacket.SenderEndPoint.ToIPv4String());
                return;
            }

            context.FireChannelRead(new InboundReliableUdpFrame
            {
                Frame = frame,
                SenderEndPoint = assembledPacket.SenderEndPoint,
                FilterTag = assembledPacket.FilterTag,
                SrcHostId = assembledPacket.SrcHostId
            });
        }

        public override Task WriteAsync(IChannelHandlerContext context, object message)
        {
            if (message is OutboundReliableUdpFrame reliableFrame)
            {
                var frameMessage = ReliableUdpHelper.BuildFrameMessage(reliableFrame.Frame);

                var outboundPacket = new OutboundUdpPacket
                {
                    Content = Unpooled.WrappedBuffer(frameMessage.GetBufferUnsafe(), 0, frameMessage.Length),
                    EndPoint = reliableFrame.DestEndPoint,
                    FilterTag = reliableFrame.FilterTag,
                    Mtu = reliableFrame.Mtu
                };

                return context.WriteAsync(outboundPacket);
            }

            return context.WriteAsync(message);
        }
    }

    internal sealed class InboundUdpMessage
    {
        internal AssembledPacket AssembledPacket { get; set; }

        internal NetMessage NetMessage { get; set; }

        internal MessageType MessageType { get; set; }
    }
}
