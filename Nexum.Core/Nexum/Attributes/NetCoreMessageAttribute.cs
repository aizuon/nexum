using System;
using Nexum.Core.Configuration;

namespace Nexum.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class NetCoreMessageAttribute(MessageType messageType) : Attribute
    {
        public MessageType MessageType { get; } = messageType;
    }
}
