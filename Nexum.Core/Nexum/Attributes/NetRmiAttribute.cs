using System;

namespace Nexum.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class NetRmiAttribute : Attribute
    {
        public NetRmiAttribute(ushort rmiId)
        {
            RmiId = rmiId;
        }

        public NetRmiAttribute(object rmiId)
        {
            RmiId = rmiId;
        }

        public object RmiId { get; }
    }
}
