using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DotNetty.Transport.Channels;
using Serilog;

namespace Nexum.Core
{
    public abstract class NetCore : IDisposable
    {
        static NetCore()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public ILogger Logger { get; protected set; }

        public string ServerName { get; protected set; }

        public Guid ServerGuid { get; protected set; }

        internal IChannel Channel { get; set; }

        internal NetSettings NetSettings { get; set; }

        internal RSACryptoServiceProvider RSA { get; set; }

        internal IPAddress LocalIP => ((IPEndPoint)Channel.LocalAddress).Address.MapToIPv4();

        protected IEventLoopGroup EventLoopGroup { get; set; }

        public virtual void Dispose()
        {
            RSA?.Dispose();
            RSA = null;
        }

        protected Task ScheduleAsync(Action<object, object> action, object context, object state, TimeSpan delay)
        {
            return EventLoopGroup.ScheduleAsync(action, context, state, delay);
        }
    }
}
