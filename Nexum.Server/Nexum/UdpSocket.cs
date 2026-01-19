using System;
using System.Net;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Nexum.Core;
using NexumCore.DotNetty.Codecs;
using Serilog;
using Constants = Serilog.Core.Constants;

namespace Nexum.Server
{
    internal sealed class UdpSocket
    {
        private static readonly ILogger Logger = Log.ForContext(Constants.SourceContextPropertyName, nameof(UdpSocket));

        public readonly uint Port;
        internal IEventLoopGroup WorkerGroup;

        public UdpSocket(NetServer owner, IPAddress udpAddress, uint listenerPort)
        {
            Port = listenerPort;
            Channel = Listen(owner, udpAddress, Port);
            Logger.Information("UDP socket bound on {Address}:{Port}", udpAddress, listenerPort);
        }

        public static Action<IChannelPipeline> UdpPipelineConfigurator { get; set; }

        public IChannel Channel { get; private set; }

        private IChannel Listen(NetServer owner, IPAddress udpAddress, uint listenerPort)
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
                        .AddLast(new UdpHandler(owner));
                }))
                .BindAsync(new IPEndPoint(udpAddress, (int)listenerPort))
                .GetAwaiter()
                .GetResult();
        }

        public void Close()
        {
            Logger.Information("Closing UDP socket on port {Port}", Port);
            Channel?.CloseAsync();
            Channel = null;
            WorkerGroup?.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            WorkerGroup = null;
        }
    }
}
