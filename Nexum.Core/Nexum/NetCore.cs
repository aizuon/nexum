using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DotNetty.Transport.Channels;
using Serilog;

namespace Nexum.Core
{
    public abstract class NetCore : IDisposable
    {
        protected NetCore()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public ILogger Logger { get; protected set; }

        public ServerType ServerType { get; protected set; }

        internal IChannel Channel { get; set; }

        internal NetSettings NetSettings { get; set; }

        internal RSACryptoServiceProvider RSA { get; set; }

        internal IPAddress LocalIP => ((IPEndPoint)Channel.LocalAddress).Address.MapToIPv4();

        public virtual void Dispose()
        {
            RSA?.Dispose();
            RSA = null;
        }
    }
}
