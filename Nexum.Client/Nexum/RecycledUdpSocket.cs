using DotNetty.Transport.Channels;

namespace Nexum.Client
{
    internal sealed class RecycledUdpSocket
    {
        internal RecycledUdpSocket(IChannel channel, IEventLoopGroup eventLoopGroup, int port, double recycleTime)
        {
            Channel = channel;
            EventLoopGroup = eventLoopGroup;
            Port = port;
            RecycleTime = recycleTime;
        }

        internal IChannel Channel { get; }

        internal IEventLoopGroup EventLoopGroup { get; }

        internal int Port { get; }

        internal double RecycleTime { get; set; }

        internal bool Garbaged { get; set; }
    }
}
