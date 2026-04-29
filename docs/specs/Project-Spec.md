This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Development

- **Build solution:** `dotnet build DuneNetworking.slnx` (uses the preview `.slnx` format; requires the SDK pinned in `global.json`, currently 10.0.0 with `rollForward: latestMajor`).
- **Format:** `dotnet format DuneNetworking.slnx`.
- **Target framework:** The three library projects target `netstandard2.1` with `Nullable` enabled — the library is consumed as a Unity package (`package.json` → `com.dunestudio.networking`, Unity 6000.0). Do not add APIs unavailable on netstandard2.1.
- **Regression harness:** `samples/HardeningValidation/` is a standalone `net10.0` console app that exercises transport safety scenarios against a loopback socket (oversized frame rejection, zero-length frame rejection, FIN mid-header, handler-throw leak check, dispose-during-receive, double-dispose, send-on-disconnected release). It is **not** listed in `DuneNetworking.slnx` — run it directly with `dotnet run --project samples/HardeningValidation`.
- **Tests:** No xUnit/NUnit project exists; do not assume a `dotnet test` workflow. The HardeningValidation sample is the closest thing to a test suite.
- **Maturity:** Early development (v0.0.1). Missing features, polish, and safeguards are expected — do not treat absence alone as a bug. Only treat something as a bug when it is explicitly called out as one (see *Known issues* below, or told so by the user).

## Library shape and threading model

This is a library meant to be embedded in games and similar applications. The host application owns the threading strategy; the library does not spin up its own pump threads, dispatch queues, or background workers.

- **No internal loops.** The only self-driven loop is the receive re-arm in `Transport` — after each completed receive, the pipeline re-arms itself so stream fragmentation across multiple socket reads can be reassembled into a framed packet without the caller having to poll.
- **Single-flight per connection, enforced.** `Transport` guards `SendAsync` and `ReceiveAsync` with CAS flags (`_sendInFlight`, `_receiveInFlight`). Reentrance throws `InvalidOperationException` rather than corrupting state. The higher layers are still expected to serialize their own calls, but the transport fails loudly when that contract is broken.
- **Errors bubble with a reason.** Transport failures surface via `OnPacketSendFailed(ITransport, Segment, TransportError)` and `OnPacketReceiveFailed(ITransport, TransportError)`. The `TransportError` enum carries the reason: `SocketError`, `PoolExhausted`, `ProtocolError`, `InvalidSegment`, `HandlerFailed`, `ObjectDisposed`. The session layer decides which ones imply disconnect and bubbles the rest to presentation / application.

### Public API surface (what applications are expected to use)

- **Connection lifecycle** via `IClient` / `IServer` (DuneSession) which produce an `IConnection` with `DisconnectAsync`, `OnDisconnected`, `OnDisconnectRequested`.
- **Packet registration + serialization** via `IPacket` and `PacketRegistry.RegisterHandler<T>(id, handler)` in DunePresentation.
- **Peer lifecycle** via `Peer.StartReceiving()`, `Peer.Send<T>(T packet)`, `Peer.Disconnect()`.
- **Encryption abstraction** via `IPacketEncryptor` — the application supplies the symmetric implementation; the library calls it in-place on the framed payload.
- **Request/response API** (DunePresentation, planned — not yet implemented). When adding this, it belongs at the presentation layer alongside `Peer`, not inside transport or session.

Everything else (sockets, buffers, segments, framing) is intended to stay internal to the layers.

## Architecture

Three projects stacked as layers, each referencing only the one below it:

```
DunePresentation  →  DuneSession  →  DuneTransport
```

Consumers normally depend on DunePresentation and treat the lower layers as implementation detail.

### DuneTransport — byte pipeline

Responsible for raw send / receive and surfacing I/O errors up the stack.

- `SegmentedBuffer` is a fixed-size segment pool carved from one contiguous backing array (default 8192 bytes / 32 segments). `TryReserveSegment` rents; `ReleaseMemory` returns. `ReleaseMemory` is **idempotent** — releasing an already-free segment is a no-op, which the transport relies on for handler-throw and dispose-race cleanup paths. Introspection: `SegmentCount`, `FreeCount`, `SegmentSize`. This is the single memory allocation strategy for the whole library — there is no per-packet GC.
- `Segment` is a small struct (`SegmentIndex`, `Memory<byte>`, `ReleaseMemoryCallback`) with a `Release()` convenience. It is **single-owner, not ref-counted**: exactly one live reference at any time. Ownership transfer is explicit in the calling conventions (see below).
- `Transport` owns a `Socket` + two `SegmentedBuffer`s (send / receive) and drives I/O through `SocketAsyncEventArgs`. It is responsible for **length-prefix framing** on the wire (2-byte little-endian `ushort` payload length via `BinaryPrimitives.ReadUInt16LittleEndian`): bytes enter/leave this layer as whole framed packets, not raw stream fragments. The receive pipeline runs in two phases (`Header` → `Payload`) and re-arms itself after each completion — this is what reassembles fragmented stream reads into whole packets — but there is no multi-threading here: one receive flight and one send flight at a time, enforced by CAS flags.
- **Event ownership rules** (see XML docs on `ITransport`):
  - `OnPacketReceived(ITransport, SocketAsyncEventArgs, Segment)` — segment ownership **transfers to the subscriber**. Subscriber must call `Release()`.
  - `OnPacketReceiveFailed(ITransport, TransportError)` — any segment the transport had rented has already been released; no segment is passed.
  - `OnPacketSendFailed(ITransport, Segment, TransportError)` — the segment has **already been released** by the transport. Subscribers get it for inspection only (e.g. logging `SegmentIndex`) and must not call `Release()` — double-release would corrupt the pool.
  - `OnPacketSent(ITransport)` — segment already released; informational.
- `Dispose()` is idempotent (via `Interlocked.Exchange` on `_disposed`), sweeps any in-flight receive/send segment back to the pool, unhooks `SocketAsyncEventArgs.Completed`, and disposes the event args. **Socket lifecycle is owned by `Connection`** — `Transport` never calls `Shutdown`, `Close`, or `Dispose` on the socket.
- `ISegmentManager` is the zero-copy serialization contract used by higher layers. It holds a `Segment` + `PacketSize` and provides default interface methods `Serialize` / `Deserialize` / `OnSend` that reserve a send slot, invoke user `OnSerialize` / `OnDeserialize` hooks, and release the segment. Any type that wants to round-trip through the transport implements this interface — `IPacket` does exactly that.

### DuneSession — connection lifecycle

Responsible for socket connect / disconnect and mapping transport errors onto connection lifecycle events. Errors it does not own get bubbled up.

- `ClientConnector` / `ServerConnector` do async connect / accept and produce an `IConnection`. Both guard their entry points (`connectingState`, `isListening`) with `Interlocked.Exchange`.
- `Connection` owns an `ITransport` and exposes connection-level events (`OnDisconnected`, `OnDisconnectRequested`). `DisconnectAsync` is single-shot via `Interlocked.Exchange(ref disconnectingState, 1)`. The session layer is the boundary that decides *when* a transport is alive; the transport itself only cares about bytes. **`Connection` is the sole owner of the `Socket`** — it disposes it in `Dispose(bool)`. `Connection.Dispose()` calls `Transport.Dispose()` first (to release segments and event args), then disposes the socket.
- Higher layers never touch `Socket` directly — they go through `IConnection.Transport`.

### DunePresentation — packet dispatch

Responsible for `IPacket` management, encryption, and (planned) the request/response API.

- `PresentationHeader`: fixed 2-byte little-endian `ushort` packet ID written at offset 0 of every packet's payload (via `BinaryPrimitives`).
- `IPacket : ISegmentManager` — implementers provide `PacketId` + `WriteFieldsToBuffer` / `ReadFieldsFromBuffer`. Default interface methods on `IPacket` slice past the `PresentationHeader.Size` offset so user code only sees its own field region.
- `PacketRegistry` maps `ushort` → `(Func<IPacket> factory, Action<IPacket> invoker)`. `RegisterHandler<T>(id, handler)` is the only registration path; duplicate IDs throw.
- `Peer` wires everything together: subscribes to `ITransport.OnPacketReceived` and `OnPacketReceiveFailed`, reads the header, looks up the entry, transfers segment ownership to a fresh packet instance, calls `Deserialize()`, then dispatches to the user handler. `Send<T>` reserves a send segment via `ISegmentManager.Serialize` and writes the header in the `afterSerialize` callback so user `WriteFieldsToBuffer` and header framing share one buffer. On receive failure, `Peer` calls `DisconnectAsync()`.
- `IPacketEncryptor` is an optional symmetric hook applied to the whole framed payload (header + fields) in-place on both send and receive.
- Request/response API — planned, not yet present in the codebase. Expected to live alongside `Peer` and build on `IPacket` + `PacketRegistry`.

### Data flow on receive (the bit worth internalizing)

1. `Transport` completes a receive, reassembles header + payload into a `Segment`, and raises `OnPacketReceived(this, args, segment)`. The `_receiveInFlight` flag is cleared **before** the invocation so the subscriber may call `ReceiveAsync()` from the handler; the delivered segment is captured locally and the field cleared so a racing `Dispose` cannot double-release it.
2. `Peer` handler: optional decrypt → read `PresentationHeader` → `PacketRegistry.TryGetEntry`.
3. Factory produces an `IPacket`, the segment is assigned into it, ownership flips (`segmentOwned = false`), `Deserialize()` runs user `ReadFieldsFromBuffer` and then releases the segment back to the pool.
4. User handler is invoked synchronously on the receive callback thread. `transport.ReceiveAsync()` is re-armed in `finally`. If the subscriber throws, `Transport` releases the segment (idempotent) and raises `OnPacketReceiveFailed(HandlerFailed)`.

The invariant: a `Segment` has exactly one owner at any time. If you add code paths in Peer / Transport, preserve the `segmentOwned` handoff pattern — dropping it causes either double-free or pool leaks (release is idempotent, but leaks are not detected).

## Known issues to be aware of

These are the **confirmed** open bugs — existing patterns around them should be treated with skepticism. Anything else that looks like a missing guard or rough edge is almost certainly "early development," not a bug: do not file, fix, or work around it unless the user flags it or it is listed here.

- Potential `Segment` leak in certain `ISegmentManager` serialization failure paths (specifically: `Serialize` releases on `OnSerialize == false`, but other failure modes in user-supplied `afterSerialize` callbacks are not covered).
- `ServerConnector.StartListening` uses `(int)SocketOptionName.MaxConnections` as the listen backlog, which resolves to 5 — far too low for game networking. Should be a configurable value (e.g. 100+).
- `Peer.OnPacketReceivedHandler` catches all exceptions from user handlers, logs via `Debug.WriteLine` (stripped in Release), and re-arms receive. This means:
  - Handler exceptions are silently swallowed in Release builds.
  - `Transport.OnPacketReceiveFailed(HandlerFailed)` never fires for Peer-level exceptions (Peer intercepts them before Transport sees the throw).
  - The spec's documented data flow says "If the subscriber throws, Transport releases the segment (idempotent) and raises OnPacketReceiveFailed(HandlerFailed)" — but the actual flow contradicts this.
- Undersized packets (`span.Length < PresentationHeader.Size`) and unknown packet IDs are silently discarded by `Peer.OnPacketReceivedHandler` without raising `OnPacketReceiveFailed` or calling `DisconnectAsync()`. These are protocol violations that should be surfaced.
