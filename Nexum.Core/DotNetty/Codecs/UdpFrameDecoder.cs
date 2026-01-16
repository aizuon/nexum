using System.Collections.Generic;
using System.Net;
using BaseLib.Extensions;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nexum.Core;
using Serilog;

namespace NexumCore.DotNetty.Codecs
{
    internal class UdpFrameDecoder : MessageToMessageDecoder<DatagramPacket>
    {
        internal static readonly ILogger Logger = Log.ForContext<UdpFrameDecoder>();
        internal readonly int MaxFrameLength;

        internal UdpFrameDecoder(int maxFrameLength)
        {
            MaxFrameLength = maxFrameLength;
        }

        protected override void Decode(IChannelHandlerContext context, DatagramPacket message, List<object> output)
        {
            var content = message.Content;
            ushort splitterFlag = content.ReadUnsignedShortLE();
            ushort filterTag = content.ReadUnsignedShortLE();
            int packetLength = content.ReadIntLE();
            uint packetId = content.ReadUnsignedIntLE();
            uint fragmentId = content.ReadUnsignedIntLE();

            if (packetLength > MaxFrameLength)
            {
                Logger.Warning("Received UDP message too long: {Length} > {MaxLength} from {Sender}", packetLength,
                    MaxFrameLength, ((IPEndPoint)message.Sender).ToIPv4String());
                throw new TooLongFrameException("Received message is too long");
            }

            var buffer = content
                .SkipBytes(2)
                .ReadStruct();
            content.Retain();

            var endPoint = (IPEndPoint)message.Sender;
            output.Add(new UdpMessage
            {
                SplitterFlag = splitterFlag,
                FilterTag = filterTag,
                PacketLength = packetLength,
                PacketId = packetId,
                FragmentId = fragmentId,
                Content = buffer,
                EndPoint = new IPEndPoint(endPoint.Address.MapToIPv4(), endPoint.Port)
            });
        }
    }
}
