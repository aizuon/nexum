namespace Nexum.Core.Serialization
{
    /// <summary>
    ///     Interface for RMI packets that can be serialized.
    ///     Classes marked with [NetRmi] attribute automatically implement this interface.
    /// </summary>
    public interface INetRmi
    {
        /// <summary>
        ///     Serializes the packet into a NetMessage wrapped with RmiMessage.
        /// </summary>
        NetMessage Serialize();
    }
}
