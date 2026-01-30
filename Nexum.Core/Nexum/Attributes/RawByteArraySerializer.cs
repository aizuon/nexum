using Nexum.Core.Serialization;

namespace Nexum.Core.Attributes
{
    public sealed class RawByteArraySerializer : INetPropertySerializer<ByteArray>
    {
        public static void Serialize(NetMessage msg, ByteArray obj)
        {
            msg.Write(obj.GetBufferSpan());
        }

        public static bool Deserialize(NetMessage msg, out ByteArray obj)
        {
            obj = null;
            if (!msg.ReadAll(out byte[] bytes))
                return false;

            obj = new ByteArray(bytes, bytes.Length, true);
            return true;
        }
    }
}
