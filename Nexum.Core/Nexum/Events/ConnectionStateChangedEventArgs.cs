using System;
using Nexum.Core.Configuration;

namespace Nexum.Core.Events
{
    public sealed class ConnectionStateChangedEventArgs : EventArgs
    {
        public ConnectionStateChangedEventArgs(ConnectionState previousState, ConnectionState newState)
        {
            PreviousState = previousState;
            NewState = newState;
            Timestamp = DateTime.UtcNow;
        }

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
