using System;
using System.Net;
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
        private readonly ILogger _logger;

        public readonly int Port;
        internal IEventLoopGroup WorkerGroup;

        public UdpSocket(NetServer owner, IPAddress udpAddress, int listenerPort)
        {
            Port = listenerPort;
            _logger = Log.ForContext(Constants.SourceContextPropertyName, $"{nameof(UdpSocket)}({Port})");
            Channel = Listen(owner, udpAddress, Port);
            _logger.Debug("UDP socket bound on {Address}:{Port}", udpAddress, Port);
        }

        public static Action<IChannelPipeline> UdpPipelineConfigurator { get; set; }

        public IChannel Channel { get; private set; }

        private IChannel Listen(NetServer owner, IPAddress udpAddress, int listenerPort)
        {
            WorkerGroup = new MultithreadEventLoopGroup();
            return new Bootstrap()
                .Group(WorkerGroup)
                .Channel<SocketDatagramChannel>()
                .Handler(new ActionChannelInitializer<IChannel>(ch =>
                {
                    UdpPipelineConfigurator?.Invoke(ch.Pipeline);

                    ch.Pipeline
                        .AddLast(new UdpFrameDecoder(NetConfig.MessageMaxLength))
                        .AddLast(new UdpFrameEncoder())
                        .AddLast(new UdpHandler(owner, listenerPort));
                }))
                .BindAsync(new IPEndPoint(udpAddress, listenerPort))
                .GetAwaiter()
                .GetResult();
        }

        public void Close()
        {
            _logger.Debug("Closing UDP socket on port {Port}", Port);
            Channel?.CloseAsync();
            Channel = null;
            WorkerGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            WorkerGroup = null;
        }
    }
}
