using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nexum.Core;
using Nexum.Core.DotNetty.Codecs;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Server
{
    internal sealed class UdpSocket
    {
        private readonly NetServer _owner;
        private ILogger _logger = Log.ForContext(Constants.SourceContextPropertyName, nameof(UdpSocket));

        internal int Port;

        internal UdpSocket(NetServer owner)
        {
            _owner = owner;
        }

        internal IChannel Channel { get; private set; }

        internal async Task ListenAsync(IPAddress udpAddress, int listenerPort, IEventLoopGroup eventLoopGroup)
        {
            Port = listenerPort;
            _logger = Log.ForContext(Constants.SourceContextPropertyName, $"{nameof(UdpSocket)}({Port})");
            Channel = await new Bootstrap()
                .Group(eventLoopGroup)
                .ChannelFactory(() => new SocketDatagramChannel(AddressFamily.InterNetwork))
                .Handler(new ActionChannelInitializer<IChannel>(ch =>
                {
                    ch.Pipeline
                        .AddLast(new UdpFrameDecoder(NetConfig.MessageMaxLength))
                        .AddLast(new UdpFrameEncoder())
                        .AddLast(new UdpHandler(_owner, Port));
                }))
                .BindAsync(new IPEndPoint(udpAddress, Port));
            _logger.Debug("UDP socket bound on {Address}:{Port}", udpAddress, Port);
        }

        internal void Close()
        {
            _logger.Debug("Closing UDP socket on port {Port}", Port);
            Channel?.CloseAsync();
            Channel = null;
        }
    }
}
