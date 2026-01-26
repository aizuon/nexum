using DotNetty.Transport.Channels;

namespace Nexum.Client.Udp
{
    internal sealed class RecycledUdpSocket
    {
        internal RecycledUdpSocket(IChannel channel, int port, double recycleTime)
        {
            Channel = channel;

            Port = port;
            RecycleTime = recycleTime;
        }

        internal IChannel Channel { get; }

        internal int Port { get; }

        internal double RecycleTime { get; set; }

        internal bool Garbaged { get; set; }
    }
}
