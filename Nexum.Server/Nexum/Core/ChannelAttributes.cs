using DotNetty.Common.Utilities;
using Nexum.Server.Sessions;

namespace Nexum.Server.Core
{
    internal static class ChannelAttributes
    {
        internal static readonly AttributeKey<NetSession> Session =
            AttributeKey<NetSession>.ValueOf($"Nexum-{nameof(Session)}");
    }
}
