# DuneNetworking Library Specification

A layered networking library for games and real-time applications. Provides TCP connection management, length-prefixed packet framing, and packet dispatch on top of raw sockets.

## Purpose

Provide a minimal, composable networking foundation that handles the boring parts (TCP connect/accept, byte framing, packet routing) while leaving application-level decisions (disconnect logic, packet handling, threading) to the host application.

The library targets netstandard2.1 and is structured as three stacked layers, each referencing only the one below it:

```
DunePresentation → DuneSession → DuneTransport
```

Consumers depend on `DunePresentation` and treat the lower layers as implementation detail.

## Design Principles

1. **The application owns the threading model.** The library does not create background threads, dispatch queues, or receive pumps. The host application decides how and when to call into the library.

2. **The library never auto-disconnects.** Transport and peer layers surface errors with reason codes, but the application decides whether and when to disconnect. This gives game servers control over kick vs. timeout semantics.

3. **No internal loops.** The only self-driving behavior is the receive re-arm in Transport — after each completed receive, the pipeline re-arms itself to handle stream fragmentation. Higher layers do not poll or wait.

4. **Single-flight per connection.** Each connection accepts one send and one receive operation at a time. Reentrant calls are rejected. Higher layers serialize their own calls.

5. **Zero-copy serialization.** Packets serialize and deserialize directly into and from pooled byte buffers without intermediate allocations. The library uses a fixed-size segment pool — there is no per-packet garbage collection.

## Layer: Transport

Raw byte send and receive with I/O error notification.

- Manages a fixed-size memory pool carved from a single contiguous array. Memory is rented and returned explicitly. Releasing already-free memory is a no-op (idempotent release).
- Wraps each packet with a length prefix on the wire. The receive pipeline reassembles fragmented stream reads into complete packets.
- Surfaces I/O errors via events carrying a reason code. Never touches the socket lifecycle — the owning layer manages sockets.

### Event Ownership

- **Packet received:** segment ownership transfers to the subscriber. The subscriber must release it.
- **Packet receive failed:** no segment is passed; any rented segment has already been released.
- **Packet send failed:** the segment has already been released by the transport. Inspect only; do not release.

## Layer: Session

Connection lifecycle management. Maps transport errors onto connection-level events.

- Provides client and server connection factories that produce a connection handle. Entry points are guarded against reentrant calls.
- The connection handle owns an underlying transport and exposes connection-level events (disconnected, disconnect requested).
- Disconnect is single-shot — once initiated, it cannot be repeated.
- Higher layers interact with connections through their transport interface, never touching sockets directly.

## Layer: Presentation

Packet dispatch, registration, and encryption.

### Packet Interface

Implementers provide a packet ID and field-level serialization methods. The library handles header writing and slicing so user code only serializes its own fields.

### Packet Registry

Maps packet IDs to factory and handler pairs. Registration is by type — the library infers the packet ID. Duplicate IDs throw. Unknown packet IDs at runtime raise an error event.

### Peer

Wires transport, registry, and encryption together. On receive: decrypts (if configured), reads the packet ID, looks up the handler, deserializes the packet, and raises a received event with an invoker callback. The application decides when to invoke the handler.

On send: reserves a buffer slot, writes the packet header in a post-serialize callback, and sends. On errors, raises the appropriate event — it never auto-disconnects.

`Receive()` is a single-shot dispatch. The application must call it repeatedly to process incoming packets.

### Encryption

An optional symmetric encryption hook applied in-place to the framed payload (header + fields) on both send and receive. The application supplies the encryption implementation.

### Connection Helpers

PeerServer and PeerClient are factory helpers that wire up connectors, packet registry, and optional encryption, then produce ready-to-use peer instances. PeerServer accepts connections and creates a peer per client. PeerClient connects to a remote and creates a peer on success.

## Concurrency Model

- **No internal threads.** All operations are synchronous or callback-driven.
- **Events fire on the calling thread.** No thread hopping.
- **Application owns receive loop.** `Receive()` is called repeatedly by the application at its chosen cadence.
- **Application owns disconnect decisions.** Errors surface as events; the application calls disconnect.

## What Is Not Included

- **Application-level protocols:** heartbeat, authentication, matchmaking
- **Message queues or buffering:** the library delivers packets as they arrive
- **Reliability or ordering guarantees:** TCP provides ordering; the library does not add retry or acknowledgment layers
- **Multi-connection management:** each peer/connection is independent; the application manages collections
