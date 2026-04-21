# Peer Connector Wrappers — Design

**Date:** 2026-04-21
**Status:** Approved for implementation planning
**Scope:** DunePresentation

## Problem

Applications that use DuneNetworking currently have to reach into DuneSession to construct a `ClientConnector` or `ServerConnector`, wire up `OnConnected` / `OnClientConnected`, and manually build a `Peer` for each resulting `IConnection` (passing in `PacketRegistry` and any `IPacketEncryptor`), then call `StartReceiving()`. This is the same boilerplate in every application and forces consumers to depend on two layers (DunePresentation + DuneSession) instead of one.

Three related pain points:

1. **Boilerplate.** Every app writes the same `connector.OnConnected → new Peer(...) → peer.StartReceiving()` glue.
2. **Server-side setup.** On the server, every accepted connection repeats the same glue per peer.
3. **Layering.** DunePresentation is nominally the top layer of the library, but applications cannot currently stay within it to stand up a connection.

## Non-Goals (Scope-Creep Guards)

These are explicitly **not** part of this design. Do not add them later without a fresh brainstorm — they are the shapes that turn a thin wrapper into an application framework.

- No peer collection inside `PeerServer` (no `ConnectedPeers`, no `FindById`, no broadcast helpers). Peers are emitted to the application, which owns all tracking.
- No auto-accept loop. `AcceptConnection()` stays caller-driven — consistent with the current `ServerConnector` contract and the library-wide "no internal loops" principle (the receive re-arm in `Transport` is the single exception, and it exists to reassemble stream fragments, not to drive a pump).
- No reconnect logic.
- No handshake / auth / "peer ready" hooks beyond the existing `OnPeerConnected`.
- No per-peer `PacketRegistry` — one shared registry across every `Peer` a server produces. Packet IDs are protocol-level, not per-connection.
- No escape-hatch constructor that accepts a pre-built connector. Applications that need custom connector configuration can still use `ClientConnector` / `ServerConnector` + `new Peer(...)` directly.
- No new tests. The `HardeningValidation` sample is transport-focused; these wrappers are ~40 lines of pass-through glue. A manual loopback smoke test during implementation is sufficient. Revisit if the wrappers grow past this design.

## Design

Two new sealed classes in DunePresentation, each owning its corresponding connector internally. Failure events pass through; success events build a `Peer`, call `StartReceiving()`, and emit `OnPeerConnected(Peer)`. Wrappers do not track or own the peers they emit — applications hold and dispose them.

### `IPeerClient` / `PeerClient`

Wraps `IClient` (`ClientConnector`). Produces at most one peer over its lifetime.

```csharp
public interface IPeerClient : IDisposable
{
    bool IsConnected { get; }

    event Action<Peer>? OnPeerConnected;
    event Action<SocketError>? OnConnectFailed;

    bool ConnectAsync(string address, int port);
}

public sealed class PeerClient : IPeerClient
{
    public PeerClient(PacketRegistry registry, Func<IPacketEncryptor>? encryptorFactory = null);
    // ... interface members above
}
```

### `IPeerServer` / `PeerServer`

Wraps `IServer` (`ServerConnector`). Produces N peers over its lifetime.

```csharp
public interface IPeerServer : IDisposable
{
    bool IsListening { get; }

    event Action<Peer>? OnPeerConnected;
    event Action<SocketError>? OnAcceptFailed;

    void StartListening(string address, int port);
    void StopListening();
    void AcceptConnection();
}

public sealed class PeerServer : IPeerServer
{
    public PeerServer(PacketRegistry registry, Func<IPacketEncryptor>? encryptorFactory = null);
    // ... interface members above
}
```

### Internal behavior (identical both sides)

Constructor:
1. Instantiate the corresponding connector (`new ClientConnector()` / `new ServerConnector()`).
2. Subscribe to the connector's connection-success event and its failure event.

On connection-success callback (`IConnection connection`):
```csharp
var encryptor = encryptorFactory?.Invoke();
var peer = new Peer(connection, registry, encryptor);
peer.StartReceiving();
OnPeerConnected?.Invoke(peer);
```

The encryptor factory is called **once per peer** so each connection gets its own instance — necessary because real `IPacketEncryptor` implementations typically hold per-session state (nonce counters, handshake-derived keys). Applications with a stateless encryptor pass `() => sharedInstance`.

On failure callback: forward the `SocketError` to the wrapper's failure event. No peer is constructed.

`Dispose()`:
1. Unhook the connector's events.
2. Dispose the connector.
3. Do **not** touch emitted peers — the application owns them.

### Forwarded surface

`PeerClient` exposes `IsConnected` and `ConnectAsync` as straight delegates to its internal `ClientConnector`. `PeerServer` exposes `IsListening`, `StartListening`, `StopListening`, `AcceptConnection` the same way. These delegates are the cost of Approach 2 (owning wrapper); they are minimal and the underlying connector API is stable.

## Files Touched

New files only — no changes to DuneSession or DuneTransport:

- `DunePresentation/src/Peer/PeerClient.cs`
- `DunePresentation/src/Peer/PeerServer.cs`
- `DunePresentation/src/Peer/Interfaces/IPeerClient.cs`
- `DunePresentation/src/Peer/Interfaces/IPeerServer.cs`

## Usage After This Change

Client:
```csharp
var registry = new PacketRegistry();
registry.RegisterHandler<PingPacket>(1, ping => Console.WriteLine("pong"));

var peerClient = new PeerClient(registry);
peerClient.OnPeerConnected += peer => peer.Send(new PingPacket());
peerClient.OnConnectFailed  += err  => Console.WriteLine($"connect failed: {err}");
peerClient.ConnectAsync("127.0.0.1", 9000);
```

Server:
```csharp
var registry = new PacketRegistry();
registry.RegisterHandler<PingPacket>(1, ping => { /* ... */ });

var peerServer = new PeerServer(registry);
peerServer.OnPeerConnected += peer => { /* app stores peer, wires peer.OnDisconnected */ };
peerServer.StartListening("0.0.0.0", 9000);
peerServer.AcceptConnection();   // caller re-arms after each OnPeerConnected, as today
```

The application no longer needs a `using DuneSession.SocketConnectors;` or `using DuneSession.SocketConnectors.Interface;` for the common case.

## Decisions Recorded

- **Approach 2** (owning wrapper with minimal passthrough) chosen over composing wrapper (Approach 1, fails the layering goal) and factory method only (Approach 3, doesn't address server-side glue).
- **Stateless emission** (Approach A in earlier discussion) — wrappers do not hold peer collections.
- **Symmetric factory** for the encryptor — both sides take `Func<IPacketEncryptor>?` for consistency and to prevent the "shared encryptor across peers" footgun on the server.
