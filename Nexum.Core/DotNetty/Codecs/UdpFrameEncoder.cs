using System;
using System.Collections.Generic;
using BaseLib.Extensions;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace Nexum.Core.DotNetty.Codecs
{
    internal sealed class UdpFrameEncoder : MessageToMessageEncoder<UdpMessage>
    {
        protected override void Encode(IChannelHandlerContext context, UdpMessage message, List<object> output)
        {
            var buffer = context.Allocator.Buffer();
            try
            {
                buffer.WriteShortLE(message.SplitterFlag)
                    .WriteShortLE(message.FilterTag)
                    .WriteIntLE(message.PacketLength)
                    .WriteIntLE((int)message.PacketId)
                    .WriteIntLE((int)message.FragmentId)
                    .WriteShortLE(Constants.TcpSplitter)
                    .WriteStruct(message.Content);

                output.Add(new DatagramPacket(buffer, message.EndPoint));
            }
            catch (Exception ex)
            {
                buffer.Release();
                ex.Rethrow();
            }
            finally
            {
                message.Content.Release();
            }
        }
    }
}
