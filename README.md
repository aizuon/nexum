# Nexum

A high-performance networking library for .NET 10 designed for real-time multiplayer games. The name "Nexum" comes from Latin, meaning "bond" or "connection" — reflecting the library's purpose of creating reliable network connections. Features TCP and UDP communication with NAT hole punching, reliable UDP, server-orchestrated P2P groups with direct and relayed messaging, AES/RC4 encryption, and zero-allocation optimizations.

> **Note:** This library was inspired by [ProudNet](https://www.proudnet.com/en/product/proudnet.php), a commercial networking solution.

[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## ✨ Features

### Server-Client Communication

- **TCP (Default)** - Reliable, ordered message delivery using DotNetty
- **UDP via NAT Hole Punching** - Optional low-latency UDP channel established through automatic NAT traversal
  - **Unreliable UDP** - Fire-and-forget for real-time data (position updates, etc.)
  - **Reliable UDP** - Guaranteed delivery over UDP with automatic retransmission and ordering
- **MTU Discovery** - Binary search algorithm discovers optimal MTU by piggybacking probes on ping/pong packets
- **Auto Fragmentation** - Large UDP packets automatically fragmented and reassembled with adaptive MTU
- **Message Compression** - Zlib compression for bandwidth optimization
- **Server Time Sync** - Client can synchronize with server time

### Peer-to-Peer (P2P)

- **P2P Groups** - Server orchestrates client-to-client UDP connections
- **P2P NAT Hole Punching** - Direct peer-to-peer connections through NAT
- **Direct P2P** - Send messages directly between peers over UDP
- **P2P MTU Discovery** - Each peer pair discovers optimal MTU independently
- **Relayed P2P** - Fallback relay through server when direct connection fails
  - **TCP Relay** - Relayed messages through server TCP
  - **UDP Relay** - Relayed messages through server UDP (supports both reliable and unreliable)

### Security

- **RSA Key Exchange** - Secure 2048-bit RSA key exchange during connection
- **AES Encryption** - Secure message encryption (configurable key length, default 256-bit)
- **RC4 Fast Encryption** - High-performance encryption for real-time data (configurable key length, default 512-bit)
- **Per-Session Keys** - Unique encryption keys generated for each client session

### Performance

- **Asynchronous I/O** - Built on DotNetty for high-performance async networking
- **UDP Socket Pool** - Multiple UDP listener sockets with random assignment for load distribution
- **Efficient Serialization** - Custom binary serialization with `NetMessage`
- **Zero-Allocation Patterns** - `GC.AllocateUninitializedArray`, `ArrayPool<T>`, `stackalloc` for small buffers, `Span<T>`, and `BinaryPrimitives`
- **Optimized Thread Pools** - DotNetty `MultithreadEventLoopGroup` auto-scales to CPU core count
- **Adaptive Fragmentation** - MTU inferred from incoming fragments for efficient reassembly

### Packet Serialization (Source Generator)

- **Compile-Time Code Generation** - Roslyn source generator automatically creates serialization and deserialization methods
- **Attribute-Based** - Simple attributes define packet structure and property order
- **Type-Safe** - Strong typing for packet properties with automatic serialization order
- **Custom Serializers** - Support for custom serializers for complex or non-standard types
- **Zero Boilerplate** - No manual serialization code needed for marked packets

## 📦 Project Structure

```text
Nexum/
├── BaseLib/                        # Core utilities and extensions
│   ├── Caching/
│   │   └── MemoryCache.cs
│   ├── Extensions/
│   │   ├── ByteArrayExtensions.cs
│   │   ├── ConcurrentDictionaryExtensions.cs
│   │   ├── DateTimeExtensions.cs
│   │   ├── DictionaryExtensions.cs
│   │   ├── ExceptionExtensions.cs
│   │   ├── IPEndPointExtensions.cs
│   │   ├── SemaphoreSlimExtensions.cs
│   │   ├── StreamExtensions.cs
│   │   └── SymmetricAlgorithmExtensions.cs
│   ├── Hashing/
│   │   ├── CRC32.cs
│   │   └── Hash.cs
│   ├── IO/
│   │   └── NonClosingStream.cs
│   ├── Logging/
│   │   └── ContextEnricher.cs
│   ├── Patterns/
│   │   └── Singleton.cs
│   ├── Threading/
│   │   ├── TaskLoop.cs
│   │   └── ThreadLoop.cs
│   └── Events.cs
├── Nexum.SourceGen/                # Roslyn source generator for packet serialization
│   └── NetSerializableGenerator.cs
├── Nexum.Core/                     # Shared networking core
│   ├── Logging/
│   │   ├── BurstDuplicateLogFilter.cs
│   │   └── BurstDuplicateLogger.cs
│   └── Nexum/
│       ├── Attributes/             # Source generator attributes
│       │   ├── INetPropertySerializer.cs
│       │   ├── NetCoreMessageAttribute.cs
│       │   ├── NetPropertyAttribute.cs
│       │   ├── NetSerializableAttribute.cs
│       │   ├── ScalarSerializer.cs
│       │   ├── StringEndPointSerializer.cs
│       │   └── UnicodeStringSerializer.cs
│       ├── Configuration/          # Settings and configuration
│       │   ├── Constants.cs
│       │   ├── Enums.cs
│       │   ├── FragmentConfig.cs
│       │   ├── HolepunchConfig.cs
│       │   ├── MtuConfig.cs
│       │   ├── NetConfig.cs
│       │   ├── NetSettings.cs
│       │   └── ReliableUdpConfig.cs
│       ├── Crypto/                 # Encryption and compression
│       │   ├── NetCrypt.cs
│       │   ├── NetZip.cs
│       │   └── RSAHelper.cs
│       ├── DotNetty/Codecs/        # DotNetty codec implementations
│       │   ├── LengthFieldBasedFrameDecoder.cs
│       │   ├── NexumFrameDecoder.cs
│       │   ├── NexumFrameEncoder.cs
│       │   ├── UdpFrameDecoder.cs
│       │   └── UdpFrameEncoder.cs
│       ├── Events/                 # Event arguments
│       │   ├── ConnectionStateChangedEventArgs.cs
│       │   └── SessionConnectionStateChangedEventArgs.cs
│       ├── Fragmentation/          # UDP packet fragmentation
│       │   ├── AssembledPacket.cs
│       │   ├── AssembledPacketError.cs
│       │   ├── DefraggingPacket.cs
│       │   ├── FragHeader.cs
│       │   ├── UdpPacketDefragBoard.cs
│       │   └── UdpPacketFragBoard.cs
│       ├── Holepunching/           # NAT hole punching
│       │   └── HolepunchHelper.cs
│       ├── Message/                # Core message packets
│       ├── Mtu/                    # MTU discovery
│       │   └── MtuDiscovery.cs
│       ├── ReliableUdp/            # Reliable UDP implementation
│       │   ├── CompressedFrameNumbers.cs
│       │   ├── ReliableUdpFrame.cs
│       │   ├── ReliableUdpHelper.cs
│       │   ├── ReliableUdpHost.cs
│       │   ├── ReliableUdpReceiver.cs
│       │   ├── ReliableUdpSender.cs
│       │   └── StreamQueue.cs
│       ├── Rmi/                    # RMI packets (S2C, C2S, C2C)
│       ├── Routing/                # Host identification
│       │   ├── FilterTag.cs
│       │   └── HostId.cs
│       ├── Serialization/          # Binary serialization
│       │   ├── ByteArray.cs
│       │   └── NetMessage.cs
│       ├── Simulation/             # Network simulation for testing
│       │   ├── NetworkProfile.cs
│       │   ├── NetworkSimulation.cs
│       │   └── SimulatedUdpChannelHandler.cs
│       ├── Udp/                    # UDP message types
│       │   └── UdpMessage.cs
│       ├── Utilities/              # Helper utilities
│       │   ├── EventLoopScheduler.cs
│       │   ├── Extensions.cs
│       │   ├── NetUtil.cs
│       │   └── SysUtil.cs
│       ├── ModuleInit.cs
│       ├── NetCore.cs
│       └── NetCoreHandler.cs
├── Nexum.Client/                   # Client-side implementation
│   └── Nexum/
│       ├── Core/                   # Client core
│       │   ├── NetClient.cs
│       │   ├── NetClientAdapter.cs
│       │   └── NetClientHandler.cs
│       ├── P2P/                    # P2P client components
│       │   ├── P2PGroup.cs
│       │   └── P2PMember.cs
│       ├── Udp/                    # UDP handling
│       │   ├── RecycledUdpSocket.cs
│       │   └── UdpHandler.cs
│       └── Utilities/              # Client-specific utilities
│           ├── NetUtil.cs
│           └── SysUtil.cs
├── Nexum.Server/                   # Server-side implementation
│   └── Nexum/
│       ├── Core/                   # Server core
│       │   ├── ChannelAttributes.cs
│       │   ├── HostIdFactory.cs
│       │   ├── NetServer.cs
│       │   ├── NetServerAdapter.cs
│       │   └── NetServerHandler.cs
│       ├── P2P/                    # P2P server components
│       │   ├── P2PConnectionState.cs
│       │   ├── P2PGroup.cs
│       │   └── P2PMember.cs
│       ├── Sessions/               # Session management
│       │   ├── NetSession.cs
│       │   └── SessionHandler.cs
│       └── Udp/                    # UDP handling
│           ├── UdpHandler.cs
│           └── UdpSocket.cs
├── Nexum.Tests/                    # Unit and integration tests
│   ├── Integration/
│   │   ├── ConnectionStateTests.cs
│   │   ├── ConnectionTests.cs
│   │   ├── EdgeCaseTests.cs
│   │   ├── IntegrationTestBase.cs
│   │   ├── IntegrationTestCollection.cs
│   │   ├── KeyExchangeTests.cs
│   │   ├── MtuDiscoveryTests.cs
│   │   ├── P2PConnectionTests.cs
│   │   ├── ReliableUdpTests.cs
│   │   ├── StressTests.cs
│   │   ├── UdpConnectionTests.cs
│   │   ├── UdpFragmentationTests.cs
│   │   └── UdpReconnectionTests.cs
│   ├── ByteArrayTests.cs
│   ├── CRC32Tests.cs
│   ├── NetCryptTests.cs
│   ├── NetMessageTests.cs
│   ├── NetPacketSourceGenTests.cs
│   └── NetZipTests.cs
├── Nexum.Tests.E2E/                # End-to-end AWS tests
│   ├── Orchestration/
│   │   ├── Ec2Orchestrator.cs
│   │   ├── IamProvisioner.cs
│   │   ├── S3Deployer.cs
│   │   └── SsmCommandRunner.cs
│   ├── AwsConfig.cs
│   └── CoreFeaturesE2ETest.cs
├── Nexum.E2E.Client/               # E2E test client application
│   └── Program.cs
├── Nexum.E2E.Server/               # E2E test server application
│   └── Program.cs
├── Nexum.E2E.Common/               # Shared E2E constants
│   └── E2EConstants.cs
├── Example.Client/                 # Example client application
│   └── Program.cs
└── Example.Server/                 # Example server application
    └── Program.cs
```

## 🚀 Quick Start

### Prerequisites

- .NET 10.0 SDK or later
- Visual Studio 2022+ or VS Code with C# extension

### Installation

Clone the repository and build the solution:

```bash
git clone https://github.com/aizuon/nexum.git
cd nexum
dotnet build Nexum.sln
```

### Server Example

```csharp
using System.Net;
using Nexum.Core;
using Nexum.Server;

const string serverName = "Relay";
var serverGuid = new Guid("a43a97d1-9ec7-495e-ad5f-8fe45fde1151");

// Create a server instance
var server = new NetServer(serverName, serverGuid);

// Handle incoming RMI messages
server.OnRmiReceive += (session, message, rmiId) =>
{
    switch (rmiId)
    {
        case 1: // Custom message handler
            // Read message data
            message.Read(out int value);
            
            // Send response back to client
            var response = new NetMessage();
            response.Write(value * 2);
            session.RmiToClient(2, response);
            break;
    }
};

// Start listening with TCP and UDP ports
await server.ListenAsync(
    new IPEndPoint(IPAddress.Any, 28000),      // TCP endpoint
    new uint[] { 29000, 29001, 29002, 29003 }  // UDP ports
);
```

### Client Example

```csharp
using System.Net;
using Nexum.Core;
using Nexum.Client;

const string serverName = "Relay";
var serverGuid = new Guid("a43a97d1-9ec7-495e-ad5f-8fe45fde1151");

// Create a client instance
var client = new NetClient(serverName, serverGuid);

// Handle connection completion
client.OnConnectionComplete += () =>
{
    Console.WriteLine($"Connected with HostId: {client.HostId}");
    
    // Send a message to the server
    var message = new NetMessage();
    message.Write(42);
    client.RmiToServer(1, message);
};

// Handle incoming RMI messages
client.OnRmiReceive += (message, rmiId) =>
{
    message.Read(out int result);
    Console.WriteLine($"Received response: {result}");
};

// Connect to server
await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 28000));
```

## 🔧 Configuration

### Server Settings

Configure the server behavior using `NetSettings`:

```csharp
var settings = new NetSettings
{
    // Transport settings
    EnableNagleAlgorithm = true,          // TCP Nagle algorithm
    IdleTimeout = 900,                    // Session idle timeout (seconds)

    // Message settings
    MessageMaxLength = 1048576,           // Max message size (1MB)
    
    // Security settings
    EncryptedMessageKeyLength = 256,      // AES key length (bits)
    FastEncryptedMessageKeyLength = 512,  // RC4 key length (bits)
    
    // P2P settings
    EnableP2PEncryptedMessaging = false,  // Encryption for P2P messages
    DirectP2PStartCondition = DirectP2PStartCondition.Always, // When to initiate direct P2P holepunching
};

var server = new NetServer("Relay", new Guid("a43a97d1-9ec7-495e-ad5f-8fe45fde1151"), settings);
```

## 📡 P2P Communication

### Creating P2P Groups

```csharp
// Server-side: Create a P2P group and add clients
var group = server.CreateP2PGroup();

// Add clients to the group
group.Join(session1);
group.Join(session2);

// Remove clients from the group
group.Leave(session1);
```

### P2P Messaging (Client-side)

```csharp
// After joining a P2P group, access peers
var peer = client.P2PGroup.P2PMembers[targetHostId];

// Send message to peer (via relay if direct connection not established)
var message = new NetMessage();
message.Write("Hello, peer!");
peer.RmiToPeer(7001, message, reliable: true, relay: true);

// Send directly (requires established direct connection)
peer.RmiToPeer(7001, message, reliable: false, relay: false);
```

## 📨 Message Serialization

`NetMessage` provides comprehensive serialization support:

```csharp
var message = new NetMessage();

// Write primitive types
message.Write(42);                    // int
message.Write(3.14f);                 // float
message.Write(true);                  // bool
message.Write("Hello");               // string (Latin1)
message.Write("Hello", unicode: true);// string (Unicode)

// Write complex types
message.Write(Guid.NewGuid());        // Guid
message.Write(new Version(1, 2, 3, 4)); // Version
message.Write(new IPEndPoint(IPAddress.Loopback, 8080)); // IPEndPoint
message.Write(new ByteArray(data));   // ByteArray
message.Write(MyEnum.Value);          // Enums

// Read data
message.Read(out int value);
message.Read(out string text);
message.Read(out Guid guid);
message.Read(out MyEnum enumValue);
```

### Data Transfer Objects (DTOs)

Use `[NetSerializable]` to define DTOs with automatic serialization. The source generator creates `Serialize()` and `Deserialize()` methods at compile time:

```csharp
using Nexum.Core.Attributes;

[NetSerializable]
public partial class PositionDto
{
    [NetProperty(0)]
    public float X { get; set; }

    [NetProperty(1)]
    public float Y { get; set; }

    [NetProperty(2)]
    public float Z { get; set; }
}

// Serialize
var dto = new PositionDto { X = 10.5f, Y = 0f, Z = -5.2f };
var message = dto.Serialize();

// Deserialize
if (PositionDto.Deserialize(message, out var received))
{
    Console.WriteLine($"Position at ({received.X}, {received.Y}, {received.Z})");
}
```

### RMI Packets with Source Generator

Use `[NetRmi]` to define RMI packets with automatic ID assignment. The generated `Serialize()` method wraps the packet in an `RmiMessage` with the specified RMI ID:

```csharp
// Define an enum for your RMI IDs (must use ushort as underlying type)
public enum GameRmiId : ushort
{
    PlayerMove = 1001,
    PlayerAttack = 1002,
    ChatMessage = 1003
}

[NetRmi(GameRmiId.PlayerMove)]
public partial class PlayerMoveRmi
{
    [NetProperty(0)]
    public uint PlayerId { get; set; }

    [NetProperty(1)]
    public PositionDto Position { get; set; }
}

// Server-side: Send RMI to client
var rmi = new PlayerMoveRmi 
{ 
    PlayerId = 1, 
    Position = new PositionDto { X = 10.5f, Y = 20.3f, Z = 0f }
};
session.RmiToClient(rmi);                           // TCP
session.RmiToClientUdpIfAvailable(rmi);             // UDP if available

// Client-side: Handle incoming RMI
client.OnRmiReceive += (message, rmiId) =>
{
    if (rmiId == (ushort)GameRmiId.PlayerMove &&
        PlayerMoveRmi.Deserialize(message, out var move))
    {
        Console.WriteLine($"Player {move.PlayerId} moved to ({move.Position.X}, {move.Position.Y}, {move.Position.Z})");
    }
};
```

You can also use raw `ushort` values for RMI IDs:

```csharp
[NetRmi(1001)]
public partial class PlayerMoveRmi { /* ... */ }
```

### Custom Serializers

Custom serializers can be specified for complex types:

```csharp
[NetSerializable]
public partial class ServerInfo
{
    [NetProperty(0, typeof(StringEndPointSerializer))]
    public IPEndPoint Endpoint { get; set; }

    [NetProperty(1, typeof(UnicodeStringSerializer))]
    public string ServerName { get; set; }
}
```

Implement custom serializers by implementing `INetPropertySerializer<T>`:

```csharp
public sealed class UnixTimestampSerializer : INetPropertySerializer<DateTime>
{
    public static void Serialize(NetMessage msg, DateTime obj)
    {
        long unixTime = new DateTimeOffset(obj).ToUnixTimeMilliseconds();
        msg.Write(unixTime);
    }

    public static bool Deserialize(NetMessage msg, out DateTime obj)
    {
        if (!msg.Read(out long unixTime))
        {
            obj = default;
            return false;
        }
        obj = DateTimeOffset.FromUnixTimeMilliseconds(unixTime).UtcDateTime;
        return true;
    }
}
```

## 🔐 Security Architecture

### Connection Handshake

1. Client connects via TCP
2. Server sends RSA public key (2048-bit)
3. Client generates AES and RC4 session keys
4. Client encrypts keys with server's RSA public key
5. Encrypted keys sent to server
6. Server decrypts and stores session keys
7. All subsequent communication uses session keys

### Encryption Modes

| Mode     | Algorithm | Use Case                              |
| -------- | --------- | ------------------------------------- |
| `Secure` | AES       | Sensitive data, authentication        |
| `Fast`   | RC4       | Real-time game data, position updates |
| `None`   | -         | Non-sensitive data                    |

## 🧪 Testing

Run the test suite:

```bash
# Run unit tests
dotnet test Nexum.Tests/Nexum.Tests.csproj --filter "FullyQualifiedName!~Integration"

# Run integration tests
dotnet test Nexum.Tests/Nexum.Tests.csproj --filter "FullyQualifiedName~Integration"

# Run all tests with coverage
dotnet test Nexum.Tests/Nexum.Tests.csproj --collect:"XPlat Code Coverage"
```

## 📊 Architecture Diagram

```text
┌─────────────────────────────────────────────────────────────────┐
│                         NetServer                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │ TCP Channel │  │ UDP Sockets │  │     P2P Groups          │  │
│  │  (DotNetty) │  │   (Pool)    │  │  ┌─────┐  ┌─────┐       │  │
│  └──────┬──────┘  └──────┬──────┘  │  │Grp 1│  │Grp 2│  ...  │  │
│         │                │         │  └──┬──┘  └──┬──┘       │  │
│         └────────┬───────┘         └─────┼────────┼──────────┘  │
│                  │                       │        │             │
│         ┌────────▼────────┐              │        │             │
│         │   NetSession    │◄─────────────┘        │             │
│         │  ┌───────────┐  │                       │             │
│         │  │ NetCrypt  │  │                       │             │
│         │  └───────────┘  │                       │             │
│         └────────┬────────┘                       │             │
└──────────────────┼────────────────────────────────┼─────────────┘
                   │                                │
         ┌─────────▼─────────┐             ┌────────▼────────┐
         │    NetClient      │             │    NetClient    │
         │  ┌─────────────┐  │◄───────────►│ (P2P Direct or  │
         │  │  P2PGroup   │  │    Direct   │  Relay via      │
         │  │  P2PMember  │  │    P2P      │  Server)        │
         │  └─────────────┘  │             └─────────────────┘
         └───────────────────┘
```

## 🔧 Dependencies

| Package                   | Version | Purpose                    |
| ------------------------- | ------- | -------------------------- |
| DotNetty.Transport        | 0.7.6   | Async networking framework |
| DotNetty.Codecs           | 0.7.6   | Frame encoding/decoding    |
| BouncyCastle.Cryptography | 2.6.2   | AES/RC4 encryption         |
| Serilog                   | 4.3.0   | Structured logging         |

## 🆔 Server Identity (ServerName + ServerGuid)

Clients and servers identify the target server using:

- `ServerName` (string): used for logging/context
- `ServerGuid` (Guid): used to validate the handshake target

The client sends `ServerGuid` during the connection handshake, and the server validates it before accepting the connection.

## 📋 TODO / Work In Progress

The following features are planned or partially implemented:

- [ ] **Advanced UDP Congestion Control** - Enhance `ReliableUdpHandler` with TCP-friendly rate control (TFRC) or BBR-style algorithms to prevent packet loss under load
- [ ] **Super Peer / Host Selection** - Automatically elect the best peer (lowest latency, best connectivity) as host in P2P groups for authoritative state sync
- [ ] **WiFi/Network Handover** - Seamless reconnection when the client's network changes (e.g., WiFi→mobile), preserving session state and recovering in-flight messages

## ⚙️ Configuration

### Configurable Settings

| Setting                                     | Type                      | Default  | Description                              |
| ------------------------------------------- | ------------------------- | -------- | ---------------------------------------- |
| `NetSettings.EnableNagleAlgorithm`          | `bool`                    | `true`   | TCP Nagle algorithm                      |
| `NetSettings.IdleTimeout`                   | `double`                  | 900      | Session idle timeout in seconds          |
| `NetSettings.MessageMaxLength`              | `uint`                    | 1048576  | Maximum message size                     |
| `NetSettings.EncryptedMessageKeyLength`     | `uint`                    | 256      | AES key length in bits                   |
| `NetSettings.FastEncryptedMessageKeyLength` | `uint`                    | 512      | RC4 key length in bits                   |
| `NetSettings.EnableP2PEncryptedMessaging`   | `bool`                    | `false`  | Encryption for P2P messages              |
| `NetSettings.DirectP2PStartCondition`       | `DirectP2PStartCondition` | `Always` | When to initiate direct P2P holepunching |

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- [DotNetty](https://github.com/Azure/DotNetty) - High-performance networking framework
- [BouncyCastle](https://www.bouncycastle.org/) - Cryptography library
- [Serilog](https://serilog.net/) - Structured logging
