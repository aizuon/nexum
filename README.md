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

## 📦 Project Structure

```text
Nexum/
├── BaseLib/                        # Core utilities and extensions
│   ├── Extensions/
│   │   ├── BinaryReaderExtensions.cs
│   │   ├── BinaryWriterExtensions.cs
│   │   ├── ByteArrayExtensions.cs
│   │   ├── ConcurrentDictionaryExtensions.cs
│   │   ├── DateTimeExtensions.cs
│   │   ├── DictionaryExtensions.cs
│   │   ├── ExceptionExtensions.cs
│   │   ├── IPEndPointExtensions.cs
│   │   ├── StreamExtensions.cs
│   │   └── SymmetricAlgorithmExtensions.cs
│   ├── ContextEnricher.cs
│   ├── CRC32.cs
│   ├── Events.cs
│   ├── Hash.cs
│   ├── MemoryCache.cs
│   ├── NonClosingStream.cs
│   ├── Singleton.cs
│   ├── TaskLoop.cs
│   └── ThreadLoop.cs
├── Nexum.Core/                     # Shared networking core
│   ├── DotNetty/Codecs/
│   │   ├── LengthFieldBasedFrameDecoder.cs
│   │   ├── NexumFrameDecoder.cs
│   │   ├── NexumFrameEncoder.cs
│   │   ├── UdpFrameDecoder.cs
│   │   └── UdpFrameEncoder.cs
│   ├── Nexum/
│   │   ├── AssembledPacket.cs
│   │   ├── AssembledPacketError.cs
│   │   ├── ByteArray.cs
│   │   ├── CompressedFrameNumbers.cs
│   │   ├── ConnectionStateChangedEventArgs.cs
│   │   ├── Constants.cs
│   │   ├── DefraggingPacket.cs
│   │   ├── Enums.cs
│   │   ├── Extensions.cs
│   │   ├── FilterTag.cs
│   │   ├── FragHeader.cs
│   │   ├── FragmentConfig.cs
│   │   ├── HolepunchConfig.cs
│   │   ├── HolepunchHelper.cs
│   │   ├── HostId.cs
│   │   ├── MtuConfig.cs
│   │   ├── MtuDiscovery.cs
│   │   ├── NetConfig.cs
│   │   ├── NetCore.cs
│   │   ├── NetCoreHandler.cs
│   │   ├── NetCrypt.cs
│   │   ├── NetMessage.cs
│   │   ├── NetSettings.cs
│   │   ├── NetUtil.cs
│   │   ├── NetZip.cs
│   │   ├── ReliableUdpConfig.cs
│   │   ├── ReliableUdpFrame.cs
│   │   ├── ReliableUdpHelper.cs
│   │   ├── ReliableUdpHost.cs
│   │   ├── ReliableUdpReceiver.cs
│   │   ├── ReliableUdpSender.cs
│   │   ├── SessionConnectionStateChangedEventArgs.cs
│   │   ├── StreamQueue.cs
│   │   ├── SysUtil.cs
│   │   ├── UdpMessage.cs
│   │   ├── UdpPacketDefragBoard.cs
│   │   └── UdpPacketFragBoard.cs
│   ├── Simulation/
│   │   ├── NetworkProfile.cs
│   │   ├── NetworkSimulation.cs
│   │   └── SimulatedUdpChannelHandler.cs
│   └── RSAHelper.cs
├── Nexum.Client/                   # Client-side implementation
│   └── Nexum/
│       ├── NetClient.cs
│       ├── NetClientAdapter.cs
│       ├── NetClientHandler.cs
│       ├── NetUtil.cs
│       ├── P2PGroup.cs
│       ├── P2PMember.cs
│       ├── SysUtil.cs
│       └── UdpHandler.cs
├── Nexum.Server/                   # Server-side implementation
│   └── Nexum/
│       ├── ChannelAttributes.cs
│       ├── HostIdFactory.cs
│       ├── NetServer.cs
│       ├── NetServerAdapter.cs
│       ├── NetServerHandler.cs
│       ├── NetSession.cs
│       ├── P2PConnectionState.cs
│       ├── P2PGroup.cs
│       ├── P2PMember.cs
│       ├── SessionHandler.cs
│       ├── UdpHandler.cs
│       └── UdpSocket.cs
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
│   │   ├── UdpConnectionTests.cs
│   │   ├── UdpFragmentationTests.cs
│   │   └── UdpReconnectionTests.cs
│   ├── ByteArrayTests.cs
│   ├── NetCryptTests.cs
│   ├── NetMessageTests.cs
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
├── ExampleClient/                  # Example client application
│   └── Program.cs
└── ExampleServer/                  # Example server application
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

// Create a server instance
var server = new NetServer(ServerType.Relay);

// Handle incoming RMI messages
server.OnRMIReceive += (session, message, rmiId) =>
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

// Create a client instance
var client = new NetClient(ServerType.Relay);

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
client.OnRMIReceive += (message, rmiId) =>
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
    // Message settings
    MessageMaxLength = 1048576,           // Max message size (1MB)
    
    // Security settings
    EncryptedMessageKeyLength = 256,      // AES key length (bits)
    FastEncryptedMessageKeyLength = 512,  // RC4 key length (bits)
    
    // P2P settings
    DirectP2PStartCondition = DirectP2PStartCondition.Always,
};

var server = new NetServer(ServerType.Relay, settings, allowDirectP2P: true);
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
message.Write(new IPEndPoint(IPAddress.Loopback, 8080)); // IPEndPoint
message.Write(new ByteArray(data));   // byte arrays

// Read data
message.Read(out int value);
message.Read(out string text);
message.Read(out Guid guid);
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

## 📝 Server Types

Nexum supports multiple server type configurations:

```csharp
public enum ServerType : byte
{
    Auth,   // Authentication server
    Game,   // Game server
    Chat,   // Chat server
    Relay   // Relay/P2P coordination server
}
```

## 📋 TODO / Work In Progress

The following features are planned or partially implemented:

- [ ] **Code Generation for RMI** - Source generator for type-safe RMI stubs instead of manual `rmiId` handling
- [ ] **Advanced UDP Congestion Control** - Enhance `ReliableUdpHandler` with TCP-friendly rate control (TFRC) or BBR-style algorithms to prevent packet loss under load
- [ ] **Super Peer / Host Selection** - Automatically elect the best peer (lowest latency, best connectivity) as host in P2P groups for authoritative state sync
- [ ] **WiFi/Network Handover** - Seamless reconnection when the client's network changes (e.g., WiFi→mobile), preserving session state and recovering in-flight messages

## ⚙️ Configuration

### Configurable Settings

| Setting                                     | Type     | Default | Description                     |
| ------------------------------------------- | -------- | ------- | ------------------------------- |
| `NetSettings.EnableNagleAlgorithm`          | `bool`   | `true`  | TCP Nagle algorithm             |
| `NetSettings.IdleTimeout`                   | `double` | 900     | Session idle timeout in seconds |
| `NetSettings.MessageMaxLength`              | `uint`   | 1048576 | Maximum message size            |
| `NetSettings.EncryptedMessageKeyLength`     | `uint`   | 256     | AES key length in bits          |
| `NetSettings.FastEncryptedMessageKeyLength` | `uint`   | 512     | RC4 key length in bits          |

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
