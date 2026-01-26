using Nexum.Core.Serialization;

namespace Nexum.Core.Attributes
{
    public sealed class ScalarSerializer : INetPropertySerializer<long>
    {
        public static void Serialize(NetMessage msg, long obj)
        {
            msg.WriteScalar(obj);
        }

        public static bool Deserialize(NetMessage msg, out long obj)
        {
            obj = 0L;
            return msg.ReadScalar(ref obj);
        }
    }
}
