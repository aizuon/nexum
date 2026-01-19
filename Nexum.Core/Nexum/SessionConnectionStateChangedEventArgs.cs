using System;

namespace Nexum.Core
{
    public sealed class SessionConnectionStateChangedEventArgs : EventArgs
    {
        public SessionConnectionStateChangedEventArgs(uint hostId, ConnectionState previousState,
            ConnectionState newState)
        {
            HostId = hostId;
            PreviousState = previousState;
            NewState = newState;
            Timestamp = DateTime.UtcNow;
        }

        public uint HostId { get; }

        public ConnectionState PreviousState { get; }

        public ConnectionState NewState { get; }

        public DateTime Timestamp { get; }

        public bool IsConnected => NewState == ConnectionState.Connected;

        public bool IsDisconnected => NewState == ConnectionState.Disconnected;

        public bool JustConnected =>
            PreviousState != ConnectionState.Connected && NewState == ConnectionState.Connected;

        public bool JustDisconnected =>
            PreviousState != ConnectionState.Disconnected && NewState == ConnectionState.Disconnected;
    }
}
