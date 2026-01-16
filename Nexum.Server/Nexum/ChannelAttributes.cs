using DotNetty.Common.Utilities;

namespace Nexum.Server
{
    internal static class ChannelAttributes
    {
        internal static readonly AttributeKey<NetSession> Session =
            AttributeKey<NetSession>.ValueOf($"Nexum-{nameof(Session)}");
    }
}
