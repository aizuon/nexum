using Nexum.Core.Serialization;

namespace Nexum.Core.Attributes
{
    public interface INetPropertySerializer<T>
    {
        static abstract void Serialize(NetMessage msg, T value);
        static abstract bool Deserialize(NetMessage msg, out T value);
    }
}
