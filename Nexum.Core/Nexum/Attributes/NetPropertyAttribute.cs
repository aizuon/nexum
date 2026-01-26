#nullable enable

using System;

namespace Nexum.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class NetPropertyAttribute : Attribute
    {
        public NetPropertyAttribute(int order)
        {
            Order = order;
        }

        public NetPropertyAttribute(int order, Type serializer)
        {
            Order = order;
            Serializer = serializer;
        }

        public int Order { get; }
        public Type? Serializer { get; }
    }
}
