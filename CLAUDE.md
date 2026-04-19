# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Development

- **Build solution:** `dotnet build DuneNetworking.slnx` (uses the preview `.slnx` format; requires the SDK pinned in `global.json`, currently 10.0.0 with `rollForward: latestMajor`).
- **Format:** `dotnet format DuneNetworking.slnx`.
- **Target framework:** All three projects target `netstandard2.1` with `Nullable` enabled — the library is consumed as a Unity package (`package.json` → `com.dunestudio.networking`, Unity 6000.0). Do not add APIs unavailable on netstandard2.1.
- **Tests:** None. There is no test project in the solution; do not assume a `dotnet test` workflow exists.
- **Maturity:** Early development (v0.0.1). Missing features, polish, and safeguards are expected — do not treat absence alone as a bug. Only treat something as a bug when it is explicitly called out as one (see *Known issues* below, or told so by the user).

## Library shape and threading model

This is a library meant to be embedded in games and similar applications. The host application owns the threading strategy; the library does not spin up its own pump threads, dispatch queues, or background workers.

- **No internal loops.** The only self-driven loop is the receive re-arm in `Transport` — after each completed receive, the pipeline re-arms itself so stream fragmentation across multiple socket reads can be reassembled into a framed packet without the caller having to poll.
- **Send / receive are single-flighted per connection.** The upper layers are expected to ensure no two concurrent `Send` or `Receive` operations run on the same connection. The library does not serialize them internally, and the controlling layer is responsible for back-pressure.
- **Errors bubble.** Transport-level failures surface to the session layer; the session layer decides which ones imply disconnect and bubbles the rest to presentation / application.

### Public API surface (what applications are expected to use)

- **Packet registration + serialization** via `IPacket` and `PacketRegistry.RegisterHandler<T>(id, handler)` in DunePresentation.
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

- `SegmentedBuffer` is a pool that hands out fixed-size `Segment`s carved from one contiguous backing array. `Segment` is ref-counted: callers release it when done, and it returns to the pool when the count hits zero. This is the single memory allocation strategy for the whole library — there is no per-packet GC.
- `Transport` owns a `Socket` + two `SegmentedBuffer`s (send / receive) and drives I/O through `SocketAsyncEventArgs`. It is responsible for **length-prefix framing** on the wire (2-byte little-endian `ushort` payload length via `BinaryPrimitives`): bytes enter/leave this layer as whole framed packets, not raw stream fragments. Incoming packets are surfaced via `OnPacketReceived(ITransport, Segment)` with segment ownership handed to the subscriber. The receive pipeline re-arms itself after each completion — this is what reassembles fragmented stream reads into whole packets — but there is no multi-threading here: a single receive flight is in play at any time.
- `ISegmentManager` is the zero-copy serialization contract used by higher layers. It holds a `Segment` + `PacketSize` and provides default interface methods `Serialize`/`Deserialize`/`OnSend` that reserve a send slot, invoke user `OnSerialize`/`OnDeserialize` hooks, and release the segment. Any type that wants to round-trip through the transport implements this interface — `IPacket` does exactly that.

### DuneSession — connection lifecycle

Responsible for socket connect / disconnect and mapping transport errors onto connection lifecycle events. Errors it does not own get bubbled up.

- `ClientConnector` / `ServerConnector` do async connect / accept and produce an `IConnection`.
- `Connection` owns an `ITransport` and exposes connection-level events (`OnDisconnected`, `OnDisconnectRequested`). The session layer is the boundary that decides *when* a transport is alive; the transport itself only cares about bytes.
- Higher layers never touch `Socket` directly — they go through `IConnection.Transport`.

### DunePresentation — packet dispatch

Responsible for `IPacket` management, encryption, and (planned) the request/response API.

- `PresentationHeader`: fixed 2-byte little-endian `ushort` packet ID written at offset 0 of every packet's payload.
- `IPacket : ISegmentManager` — implementers provide `PacketId` + `WriteFieldsToBuffer` / `ReadFieldsFromBuffer`. Default interface methods on `IPacket` slice past the `PresentationHeader.Size` offset so user code only sees its own field region.
- `PacketRegistry` maps `ushort` → `(Func<IPacket> factory, Action<IPacket> invoker)`. `RegisterHandler<T>(id, handler)` is the only registration path.
- `Peer` wires everything together: subscribes to `ITransport.OnPacketReceived`, reads the header, looks up the entry, transfers segment ownership to a fresh packet instance, calls `Deserialize()`, then dispatches to the user handler. `Send<T>` reserves a send segment via `ISegmentManager.Serialize` and writes the header in the `afterSerialize` callback so user `WriteFieldsToBuffer` and header framing share one buffer.
- `IPacketEncryptor` is an optional symmetric hook applied to the whole framed payload (header + fields) in-place on both send and receive.
- Request/response API — planned, not yet present in the codebase. Expected to live alongside `Peer` and build on `IPacket` + `PacketRegistry`.

### Data flow on receive (the bit worth internalizing)

1. `Transport` completes a receive, reconstructs a full framed packet into a `Segment`, and raises `OnPacketReceived(this, segment)`.
2. `Peer` handler: optional decrypt → read `PresentationHeader` → `PacketRegistry.TryGetEntry`.
3. Factory produces an `IPacket`, the segment is assigned into it, ownership flips (`segmentOwned = false`), `Deserialize()` runs user `ReadFieldsFromBuffer` and then releases the segment back to the pool.
4. User handler is invoked synchronously on the receive callback thread. `transport.ReceiveAsync()` is re-armed in `finally`.

The invariant: a `Segment` has exactly one owner at any time. If you add code paths in Peer / Transport, preserve the `segmentOwned` handoff pattern — dropping it causes either double-free or pool leaks.

## Known issues to be aware of

These are the **confirmed** open bugs — existing patterns around them should be treated with skepticism. Anything else that looks like a missing guard or rough edge is almost certainly "early development," not a bug: do not file, fix, or work around it unless the user flags it or it is listed here.

- TCP framing edge cases in `Transport` around fragment reassembly.
- Potential `Segment` leak in certain `ISegmentManager` serialization failure paths.
- Dual socket ownership between `Connection` and `Transport` on disconnect.
- `Send` is not thread-safe — concurrent `Peer.Send` calls on one connection can corrupt the send buffer. (This is a bug, not the single-flight design contract above: the design assumes the application serializes sends, but the code does not fail loudly when that contract is broken.)
- `_isListening` in `ServerConnector` is not guarded across threads.