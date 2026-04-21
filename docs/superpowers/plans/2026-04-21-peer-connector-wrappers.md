# Peer Connector Wrappers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two presentation-layer wrappers (`PeerClient`, `PeerServer`) that own their corresponding DuneSession connector, emit ready-to-use `Peer` instances on connect/accept, and keep application code free of DuneSession references for the common case.

**Architecture:** Approach 2 from the design doc — owning wrappers with minimal passthrough. Stateless emission (no peer collection inside `PeerServer`). Symmetric `Func<IPacketEncryptor>?` factory on both sides so each connection gets its own encryptor. Caller-driven `AcceptConnection()` on the server — no auto-accept loop.

**Tech Stack:** C# / netstandard2.1. No new dependencies. No test framework — manual loopback smoke test per the spec.

**Spec:** `docs/superpowers/specs/2026-04-21-peer-connector-wrappers-design.md`

**Refinement from spec:** Events use `Action<IPeer>` (not `Action<Peer>`) for consistency with the rest of the project's interface-first API style. `IPeer` exposes everything callers need (`Send`, `Disconnect`, `OnDisconnected`, `IsConnected`, `StartReceiving`, `IDisposable`).

---

## File Structure

New files only — no modifications to existing source. No DuneSession or DuneTransport changes.

| Path | Responsibility |
|------|----------------|
| `DunePresentation/src/Peer/Interfaces/IPeerClient.cs` | Client-wrapper contract |
| `DunePresentation/src/Peer/Interfaces/IPeerServer.cs` | Server-wrapper contract |
| `DunePresentation/src/Peer/PeerClient.cs` | Owns `ClientConnector`; emits one `IPeer` on connect |
| `DunePresentation/src/Peer/PeerServer.cs` | Owns `ServerConnector`; emits one `IPeer` per accept |

Each class is ~40 lines. No shared base class — duplication is too small to factor, and the client/server semantics differ enough (single vs N peers, different connector events) that a base would be contrived.

---

## Task 1: Client-side wrapper (`IPeerClient` + `PeerClient`)

**Files:**
- Create: `DunePresentation/src/Peer/Interfaces/IPeerClient.cs`
- Create: `DunePresentation/src/Peer/PeerClient.cs`

- [ ] **Step 1: Create the `IPeerClient` interface**

Write `DunePresentation/src/Peer/Interfaces/IPeerClient.cs`:

```csharp
using System;
using System.Net.Sockets;

namespace DunePresentation.Peer.Interfaces
{
    public interface IPeerClient : IDisposable
    {
        bool IsConnected { get; }

        event Action<IPeer>? OnPeerConnected;
        event Action<SocketError>? OnConnectFailed;

        bool ConnectAsync(string address, int port);
    }
}
```

- [ ] **Step 2: Create the `PeerClient` class**

Write `DunePresentation/src/Peer/PeerClient.cs`:

```csharp
using System;
using System.Net.Sockets;
using DunePresentation.Encryption.Interface;
using DunePresentation.Packet;
using DunePresentation.Peer.Interfaces;
using DuneSession.SocketConnectors;
using DuneSession.SocketConnectors.Interface;

namespace DunePresentation.Peer
{
    public sealed class PeerClient : IPeerClient
    {
        private readonly PacketRegistry _registry;
        private readonly Func<IPacketEncryptor>? _encryptorFactory;
        private readonly IClient _client;

        private bool _disposed;

        public bool IsConnected => _client.IsConnected;

        public event Action<IPeer>? OnPeerConnected;
        public event Action<SocketError>? OnConnectFailed;

        public PeerClient(PacketRegistry registry, Func<IPacketEncryptor>? encryptorFactory = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _encryptorFactory = encryptorFactory;

            _client = new ClientConnector();
            _client.OnConnected += HandleConnected;
            _client.OnConnectFailed += HandleConnectFailed;
        }

        public bool ConnectAsync(string address, int port)
            => _client.ConnectAsync(address, port);

        private void HandleConnected(IConnection connection)
        {
            IPacketEncryptor? encryptor = _encryptorFactory?.Invoke();
            var peer = new Peer(connection, _registry, encryptor);
            peer.StartReceiving();
            OnPeerConnected?.Invoke(peer);
        }

        private void HandleConnectFailed(SocketError error)
            => OnConnectFailed?.Invoke(error);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _client.OnConnected -= HandleConnected;
            _client.OnConnectFailed -= HandleConnectFailed;
            _client.Dispose();
        }
    }
}
```

- [ ] **Step 3: Build to verify**

Run from repo root:

```bash
dotnet build DuneNetworking.slnx
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

If you see CS0246 on `Peer`, confirm the namespace on the interface file is `DunePresentation.Peer.Interfaces` and the class file is `DunePresentation.Peer` — the namespaces are nested under `Peer` so the class `Peer` is reachable.

- [ ] **Step 4: Commit**

```bash
git add DunePresentation/src/Peer/Interfaces/IPeerClient.cs DunePresentation/src/Peer/PeerClient.cs
git commit -m "feat(presentation): add PeerClient wrapper over ClientConnector"
```

---

## Task 2: Server-side wrapper (`IPeerServer` + `PeerServer`)

**Files:**
- Create: `DunePresentation/src/Peer/Interfaces/IPeerServer.cs`
- Create: `DunePresentation/src/Peer/PeerServer.cs`

- [ ] **Step 1: Create the `IPeerServer` interface**

Write `DunePresentation/src/Peer/Interfaces/IPeerServer.cs`:

```csharp
using System;
using System.Net.Sockets;

namespace DunePresentation.Peer.Interfaces
{
    public interface IPeerServer : IDisposable
    {
        bool IsListening { get; }

        event Action<IPeer>? OnPeerConnected;
        event Action<SocketError>? OnAcceptFailed;

        void StartListening(string address, int port);
        void StopListening();
        void AcceptConnection();
    }
}
```

- [ ] **Step 2: Create the `PeerServer` class**

Write `DunePresentation/src/Peer/PeerServer.cs`:

```csharp
using System;
using System.Net.Sockets;
using DunePresentation.Encryption.Interface;
using DunePresentation.Packet;
using DunePresentation.Peer.Interfaces;
using DuneSession.SocketConnectors;
using DuneSession.SocketConnectors.Interface;

namespace DunePresentation.Peer
{
    public sealed class PeerServer : IPeerServer
    {
        private readonly PacketRegistry _registry;
        private readonly Func<IPacketEncryptor>? _encryptorFactory;
        private readonly IServer _server;

        private bool _disposed;

        public bool IsListening => _server.IsListening;

        public event Action<IPeer>? OnPeerConnected;
        public event Action<SocketError>? OnAcceptFailed;

        public PeerServer(PacketRegistry registry, Func<IPacketEncryptor>? encryptorFactory = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _encryptorFactory = encryptorFactory;

            _server = new ServerConnector();
            _server.OnClientConnected += HandleClientConnected;
            _server.OnAcceptFailed += HandleAcceptFailed;
        }

        public void StartListening(string address, int port)
            => _server.StartListening(address, port);

        public void StopListening()
            => _server.StopListening();

        public void AcceptConnection()
            => _server.AcceptConnection();

        private void HandleClientConnected(IConnection connection)
        {
            IPacketEncryptor? encryptor = _encryptorFactory?.Invoke();
            var peer = new Peer(connection, _registry, encryptor);
            peer.StartReceiving();
            OnPeerConnected?.Invoke(peer);
        }

        private void HandleAcceptFailed(SocketError error)
            => OnAcceptFailed?.Invoke(error);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _server.OnClientConnected -= HandleClientConnected;
            _server.OnAcceptFailed -= HandleAcceptFailed;
            _server.Dispose();
        }
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build DuneNetworking.slnx
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Commit**

```bash
git add DunePresentation/src/Peer/Interfaces/IPeerServer.cs DunePresentation/src/Peer/PeerServer.cs
git commit -m "feat(presentation): add PeerServer wrapper over ServerConnector"
```

---

## Task 3: Loopback smoke test (manual, not committed)

**Files:**
- Create (temporary): `samples/PeerConnectSmoke/PeerConnectSmoke.csproj`
- Create (temporary): `samples/PeerConnectSmoke/Program.cs`

The spec defers formal tests. This task verifies end-to-end that both wrappers produce working peers against a loopback socket. The files are deleted after the run and not committed.

- [ ] **Step 1: Create the smoke-test project file**

Write `samples/PeerConnectSmoke/PeerConnectSmoke.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <RootNamespace>PeerConnectSmoke</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\DuneTransport\DuneTransport.csproj" />
    <ProjectReference Include="..\..\DuneSession\DuneSession.csproj" />
    <ProjectReference Include="..\..\DunePresentation\DunePresentation.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the smoke-test program**

Write `samples/PeerConnectSmoke/Program.cs`:

```csharp
using System;
using System.Threading;
using DunePresentation.Packet;
using DunePresentation.Peer;
using DunePresentation.Peer.Interfaces;

var registry = new PacketRegistry();

using var server = new PeerServer(registry);
int serverFired = 0;
server.OnPeerConnected += _ => Interlocked.Exchange(ref serverFired, 1);
server.OnAcceptFailed += err => Console.WriteLine($"server accept failed: {err}");

server.StartListening("127.0.0.1", 19000);
server.AcceptConnection();

using var client = new PeerClient(registry);
int clientFired = 0;
IPeer? clientPeer = null;
client.OnPeerConnected += peer => { clientPeer = peer; Interlocked.Exchange(ref clientFired, 1); };
client.OnConnectFailed += err => Console.WriteLine($"client connect failed: {err}");

client.ConnectAsync("127.0.0.1", 19000);

// Give the loopback handshake time to complete.
Thread.Sleep(500);

Console.WriteLine($"server.OnPeerConnected fired: {serverFired == 1}");
Console.WriteLine($"client.OnPeerConnected fired: {clientFired == 1}");
Console.WriteLine($"client peer IsConnected: {clientPeer?.IsConnected}");

Environment.ExitCode = (serverFired == 1 && clientFired == 1 && clientPeer?.IsConnected == true) ? 0 : 1;
```

Port `19000` is arbitrary; change if occupied.

- [ ] **Step 3: Run the smoke test**

```bash
dotnet run --project samples/PeerConnectSmoke
```

Expected output:

```
server.OnPeerConnected fired: True
client.OnPeerConnected fired: True
client peer IsConnected: True
```

Exit code: `0`.

If `server.OnPeerConnected fired: False`, the most likely cause is that `ServerConnector.AcceptConnection()` wasn't called — re-check `Task 2 PeerServer.AcceptConnection` is a straight delegate.

If `client.OnPeerConnected fired: False` with no visible connect failure, increase the `Thread.Sleep` to 1500ms — the OS may be slow to complete the loopback handshake under load.

- [ ] **Step 4: Delete the smoke-test files**

```bash
rm -rf samples/PeerConnectSmoke
```

- [ ] **Step 5: Verify no uncommitted changes remain from this task**

```bash
git status -s
```

Expected: the smoke-test project does not appear. If `samples/PeerConnectSmoke/` still shows, re-run the delete above.

No commit for this task — the smoke test is a one-shot manual verification per the spec ("no new tests").

---

## Self-Review Summary

- **Spec coverage:** Problem statement (boilerplate, server setup, layering) → Tasks 1 & 2. Non-goals (no collection, no auto-accept, no reconnect, no handshake hooks, no per-peer registry, no escape-hatch ctor, no formal tests) → none implemented, non-goals preserved. Design sections (`IPeerClient`/`PeerClient`, `IPeerServer`/`PeerServer`, internal behavior, forwarded surface, files touched, usage) → all in Tasks 1, 2, and the smoke test.
- **Placeholder scan:** No TBDs, no "add error handling" hand-waving, no "similar to Task N" — each task has its full code inline.
- **Type consistency:** Registry parameter is `PacketRegistry` everywhere. Encryptor parameter is `Func<IPacketEncryptor>?` everywhere. Event payload is `IPeer` on both interfaces and both classes. Handlers are named `HandleConnected`/`HandleConnectFailed` on the client and `HandleClientConnected`/`HandleAcceptFailed` on the server, matching the connector event names they subscribe to.
- **Refinement noted:** `Action<IPeer>` vs spec's `Action<Peer>` — called out at the top of this plan.
