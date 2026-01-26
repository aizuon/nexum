using Nexum.Core.Serialization;

namespace Nexum.Core.Attributes
{
    public sealed class UnicodeStringSerializer : INetPropertySerializer<string>
    {
        public static void Serialize(NetMessage msg, string obj)
        {
            msg.Write(obj, true);
        }

        public static bool Deserialize(NetMessage msg, out string obj)
        {
            return msg.Read(out obj, out _);
        }
    }
}
