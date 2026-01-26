using System.Net;
using Nexum.Core.Serialization;

namespace Nexum.Core.Attributes
{
    public sealed class StringEndPointSerializer : INetPropertySerializer<IPEndPoint>
    {
        public static void Serialize(NetMessage msg, IPEndPoint obj)
        {
            msg.WriteStringEndPoint(obj);
        }

        public static bool Deserialize(NetMessage msg, out IPEndPoint obj)
        {
            return msg.ReadStringEndPoint(out obj);
        }
    }
}
