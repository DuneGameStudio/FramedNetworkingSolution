# DuneNetworking — API Reference

A layered networking library for games and real-time applications. Three stacked layers, each referencing only the one below it:

```
DunePresentation → DuneSession → DuneTransport
```

Consumers depend on `DunePresentation`. Lower layers are implementation detail.

***

## Contents

### DuneTransport (`DuneTransport.Transport`)

* [`Transport`](#transport)
* [`TransportError`](#transporterror)
* [`Segment`](#segment)
* [`SegmentedBuffer`](#segmentedbuffer)

### DuneSession (`DuneSession.SocketConnectors`)

* [`IConnection`](#iconnection)
* [`IClientConnector`](#iconnectclient)
* [`IServerConnector`](#iconnectserver)

### DunePresentation (`DunePresentation.Peer`)

* [`IPeer`](#ipeer)
* [`IPeerClient`](#ipeerclient)
* [`IPeerServer`](#ipeerserver)
* [`IPacket`](#ipacket)
* [`ISegmentManager`](#isegmentmanager)
* [`PacketRegistry`](#packetregistry)
* [`IPacketEncryptor`](#ipacketencryptor)

### Data Flow

* [Send path](#send-path)
* [Receive path](#receive-path)

***

## DuneTransport

Raw byte send and receive with I/O error notification. Length-prefixed framing. Pooled segment memory. No socket lifecycle management — the owning layer provides the `Socket`.

***

### `Transport`

Wraps a raw TCP socket with framing and pooling.

**Namespace:** `DuneTransport.Transport`

#### Constructor

```C#
new Transport(Socket socket)
```

| Parameter | Type     | Description                               |
| --------- | -------- | ----------------------------------------- |
| `socket`  | `Socket` | A connected TCP socket. Must not be null. |

**Throws:** `ArgumentNullException` if `socket` is null.

#### Properties

| Property        | Type   | Description                                                                                                                  |
| --------------- | ------ | ---------------------------------------------------------------------------------------------------------------------------- |
| `IsConnected`   | `bool` | `true` while the socket is alive. Set to `false` on FIN (peer closed) or `Dispose()`. Socket errors do not affect this flag. |
| `IsDisposed`    | `bool` | `true` after `Dispose()` is called.                                                                                          |

#### Methods

***

#### `TryReserveSendPacket`

Rents a segment from the send pool for serializing a packet.

```C#
bool TryReserveSendPacket(out Segment segment)
```

| Parameter | Type          | Description                                                                                                                                                                     |
| --------- | ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `segment` | `out Segment` | On success, the rented segment. `segment.Memory` is sliced from byte 2 (after the 2-byte length header). Write payload starting at offset 0 of the span. On failure, `default`. |

**Returns:** `true` if a segment was rented; `false` if disposed or pool exhausted.

**Example:**

```C#
if (transport.TryReserveSendPacket(out Segment seg))
{
    // write payload into seg.Memory.Span
    transport.SendAsync(seg, payloadSize);
}
```

***

#### `SendAsync`

Begins an asynchronous send. Prepends a 2-byte length header and initiates the socket send.

```C#
void SendAsync(Segment packet, int packetSize)
```

| Parameter    | Type      | Description                                                   |
| ------------ | --------- | ------------------------------------------------------------- |
| `packet`     | `Segment` | A segment obtained from `TryReserveSendPacket`.               |
| `packetSize` | `int`     | The number of bytes of payload (excluding the 2-byte header). |

**Behavior:**

* One send in flight at a time. If another send is pending, fires `OnPacketSendFailed` with `SendAlreadyPending`.
* On success: segment released by Transport, `OnPacketSent` fired.
* On failure: segment ownership **transfers** to the subscriber via `OnPacketSendFailed`. The subscriber must retry with `SendAsync()` or call `Release()`.

> **Retry contract:** When `OnPacketSendFailed` fires, the transport can accept a new `SendAsync()` call. The application must not send a different packet until the failed one is resolved — either retried or released. The library provides no ordering or queuing guarantees.

**Example:**

```C#
transport.OnPacketSendFailed += (t, seg, error) =>
{
    if (error == TransportError.SocketError)
        t.SendAsync(seg, segSize);  // retry
    else
        seg.Release();               // terminal error, give up
};

transport.SendAsync(segment, size);
```

***

#### `ReceiveAsync`

Begins an asynchronous receive. Arms the receive pipeline (header phase → payload phase → deliver).

```C#
void ReceiveAsync()
```

**Behavior:**

* One receive in flight at a time. If another receive is pending, fires `OnPacketReceiveFailed` with `ReceiveAlreadyPending`.
* On success: fires `OnPacketReceived` with segment ownership transferred to the subscriber.
* On failure: fires `OnPacketReceiveFailed`. No segment passed — already released.
* Does not re-arm after completion. The application must call `ReceiveAsync()` again.

**Example:**

```C#
transport.OnPacketReceived += (t, args, seg) =>
{
    // process seg.Memory.Span
    seg.Release();
    t.ReceiveAsync();  // re-arm
};

transport.ReceiveAsync();  // initial arm
```

***

#### `Dispose`

Cleans up Transport resources.

```C#
void Dispose()
```

**Behavior:**

* Releases any rented segments (send and receive).
* Unsubscribes SAEA completion handlers.
* Sets `IsConnected` to `false` and `IsDisposed` to `true`.
* Does **not** dispose the socket — the owning layer does that.
* No events fire after `Dispose()` completes.

***

#### Events

| Event                   | Signature                                                 | Description                                                                                                                                     |
| ----------------------- | --------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| `OnPacketReceived`      | `Action&lt;ITransport, SocketAsyncEventArgs, Segment&gt;` | Fired when a complete framed packet is received. Segment ownership **transfers** to the subscriber. Subscriber must call `Release()` when done. |
| `OnPacketSent`          | `Action&lt;ITransport&gt;`                                | Fired when a send completes successfully. Segment already released.                                                                             |
| `OnPacketSendFailed`    | `Action&lt;ITransport, Segment, TransportError&gt;`       | Fired when a send fails. Segment ownership **transfers** to the subscriber. Subscriber must retry or release.                                   |
| `OnPacketReceiveFailed` | `Action&lt;ITransport, TransportError&gt;`                | Fired when a receive fails. No segment passed.                                                                                                  |

**See also:** [`TransportError`](#transporterror) for error codes.

***

### `TransportError`

Reason codes surfaced with Transport failure events.

**Namespace:** `DuneTransport.Transport`

```C#
enum TransportError
```

| Value                   | Meaning                                                                             |
| ----------------------- | ----------------------------------------------------------------------------------- |
| `SocketError`           | OS-level socket failure (exception or bad `SocketError` in SAEA).                   |
| `PoolExhausted`         | Segment pool had no free segment.                                                   |
| `ProtocolError`         | Peer sent a malformed frame (zero-length or oversized payload).                     |
| `InvalidSegment`        | Caller passed a segment the send buffer cannot resolve.                             |
| `HandlerFailed`         | `OnPacketReceived` subscriber threw an exception. Segment released on their behalf. |
| `ObjectDisposed`        | Operation called on disposed Transport.                                             |
| `RegistryError`         | Packet registry failed to resolve a packet ID. (Presentation layer.)                |
| `SerializationError`    | Packet serialize/deserialize failed. (Presentation layer.)                          |
| `SocketDisconnected`    | FIN received or socket not connected.                                               |
| `ReceiveAlreadyPending` | Reentrant `ReceiveAsync()` call.                                                    |
| `SendAlreadyPending`    | Reentrant `SendAsync()` call.                                                       |

***

### `Segment`

A rented slice of the pooled buffer. Value type — copies cheaply, but `Memory<byte>` always points to the same backing array.

**Namespace:** `DuneTransport.BufferManager`

```C#
struct Segment
```

#### Properties

| Property                | Type                | Description                                |
| ----------------------- | ------------------- | ------------------------------------------ |
| `SegmentIndex`          | `int`               | 1-based index into the pool.               |
| `Memory`                | `Memory<byte>`      | The rented slice.                          |
| `ReleaseMemoryCallback` | `Action&lt;int&gt;` | Points to `SegmentedBuffer.ReleaseMemory`. |

#### Methods

| Method      | Description                                                                |
| ----------- | -------------------------------------------------------------------------- |
| `Release()` | Returns the segment to the pool. Idempotent — safe to call multiple times. |

***

### `SegmentedBuffer`

Fixed-size memory pool carved from a single contiguous array. Zero-allocation segment rental. Thread-safe.

**Namespace:** `DuneTransport.BufferManager`

```C#
new SegmentedBuffer(int arrayLength = 8192, int segmentCount = 32)
```

#### Properties

| Property       | Type  | Description                                                   |
| -------------- | ----- | ------------------------------------------------------------- |
| `SegmentSize`  | `int` | Size of each segment in bytes (`arrayLength / segmentCount`). |
| `SegmentCount` | `int` | Total number of segments.                                     |
| `FreeCount`    | `int` | Number of free segments.                                      |

#### Methods

| Method                                                                       | Description                                                                                             |
| ---------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `TryReserveSegment(out Segment)`                                             | Rents a segment. Returns `false` if pool exhausted.                                                     |
| `ReleaseMemory(int segmentNumber)`                                           | Returns a segment to the pool. Idempotent.                                                              |
| `GetRegisteredMemory(int segmentNumber, int length, out Memory&lt;byte&gt;)` | Validates bounds and returns a `Memory&lt;byte&gt;` view of the full segment (including header region). |

***

## DuneSession

Connection lifecycle management. Wraps Transport with socket disconnect and connection state.

***

### `IConnection`

Core connection handle. Owns a Transport and manages socket-level disconnect.

**Namespace:** `DuneSession.SocketConnectors.Interface`

```C#
interface IConnection : IDisposable
```

#### Properties

| Property      | Type         | Description                                     |
| ------------- | ------------ | ----------------------------------------------- |
| `IsConnected` | `bool`       | `true` while connected.                         |
| `Transport`   | `ITransport` | The underlying transport. Use this for all I/O. |

#### Methods

| Method              | Description                                                                                         |
| ------------------- | --------------------------------------------------------------------------------------------------- |
| `DisconnectAsync()` | Graceful socket disconnect. Single-shot — guarded against disposed and already-disconnected states. |
| `Dispose()`         | Unsubscribes events. Disposes transport and socket.                                                 |

#### Events

| Event            | Signature | Description                                                                                 |
| ---------------- | --------- | ------------------------------------------------------------------------------------------- |
| `OnDisconnected` | `Action`  | Fires when the connection is fully disconnected (local disconnect completes or remote FIN). |

**Example:**

```C#
connection.OnDisconnected += () => { /* cleanup */ };
connection.Transport.SendAsync(segment, size);
// ...
connection.DisconnectAsync();
```

**See also:** [`Transport`](#transport)

***

### `IClientConnector` {#iconnectclient}

Factory for client connections. Handles TCP handshake and produces an `IConnection` on success.

**Namespace:** `DuneSession.SocketConnectors.Interface`

```C#
interface IClientConnector : IDisposable
```

#### Methods

| Method                                   | Returns | Description                                                                                 |
| ---------------------------------------- | ------- | ------------------------------------------------------------------------------------------- |
| `ConnectAsync(string address, int port)` | `bool`  | Begins async TCP connect. Returns `false` if already connected or a connect is in progress. |

#### Events

| Event             | Signature                   | Description                                               |
| ----------------- | --------------------------- | --------------------------------------------------------- |
| `OnConnected`     | `Action&lt;IConnection&gt;` | Fires when connect succeeds. Produces a new `Connection`. |
| `OnConnectFailed` | `Action&lt;SocketError&gt;` | Fires when connect fails. Socket disposed.                |

**Example:**

```C#
var connector = new ClientConnector();
connector.OnConnected += conn => { /* use conn.Transport */ };
connector.OnConnectFailed += err => { /* handle */ };
connector.ConnectAsync("127.0.0.1", 5000);
```

***

### `IServerConnector` {#iconnectserver}

Factory for server connections. Binds a listening socket and produces `IConnection` per accepted client.

**Namespace:** `DuneSession.SocketConnectors.Interface`

```C#
interface IServerConnector : IDisposable
```

#### Methods

| Method                                     | Description                                                                                   |
| ------------------------------------------ | --------------------------------------------------------------------------------------------- |
| `StartListening(string address, int port)` | Binds the socket and starts listening. Single-shot. Backlog of 128.                           |
| `AcceptConnection()`                       | Begins an async accept. Call after `StartListening` and after each previous accept completes. |
| `StopListening()`                          | Closes the listening socket.                                                                  |

#### Events

| Event               | Signature                   | Description                                              |
| ------------------- | --------------------------- | -------------------------------------------------------- |
| `OnClientConnected` | `Action&lt;IConnection&gt;` | Fires when accept succeeds. Produces a new `Connection`. |
| `OnAcceptFailed`    | `Action&lt;SocketError&gt;` | Fires when accept fails.                                 |

**Example:**

```C#
var server = new ServerConnector();
server.OnClientConnected += conn => { /* handle client */ };
server.StartListening("0.0.0.0", 5000);
server.AcceptConnection();
```

***

## DunePresentation

Packet dispatch, registration, and encryption. The application's primary entry point.

***

### `IPeer`

Main handle for a single connection. Wires together Transport, packet registry, and optional encryption. The application sends and receives `IPacket` instances.

**Namespace:** `DunePresentation.Peer.Interfaces`

```C#
interface IPeer : IDisposable
```

#### Properties

| Property      | Type   | Description                                       |
| ------------- | ------ | ------------------------------------------------- |
| `IsConnected` | `bool` | `true` while the underlying connection is active. |

#### Methods

| Method                    | Description                                                                                                                            |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `Send&lt;T&gt;(T packet)` | Serializes the packet, writes the presentation header, encrypts (if configured), and initiates the send. `T` must implement `IPacket`. |
| `Receive()`               | Arms the receive pipeline. Delegates to `Transport.ReceiveAsync()`.                                                                    |
| `DisconnectAsync()`       | Graceful disconnect. Delegates to `Connection.DisconnectAsync()`.                                                                      |
| `Dispose()`               | Unsubscribes all transport events. Disposes the connection.                                                                            |

#### Events

| Event                           | Signature                                      | Description                                                                                                                                          |
| ------------------------------- | ---------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `OnDisconnected`                | `Action`                                       | Fires when the connection is fully disconnected.                                                                                                     |
| `OnPacketReceived`              | `Action&lt;IPacket, Action&lt;IPacket&gt;&gt;` | Fires when a packet is received and deserialized. The `Action&lt;IPacket&gt;` is the registered handler — the application decides when to invoke it. |
| `OnPacketSent`                  | `Action`                                       | Fires when a send completes successfully.                                                                                                            |
| `OnPacketSendFailed`            | `Action&lt;IPacket, TransportError&gt;`        | Fires when a send fails. The failed `IPacket` is passed to the application. Call `Send(packet)` to retry with a fresh segment.                       |
| `OnPacketReceiveFailed`         | `Action&lt;TransportError&gt;`                 | Fires when a receive fails.                                                                                                                          |
| `OnHandlingPacketSendFailed`    | `Action&lt;TransportError&gt;`                 | Fires when Peer's own send handling fails (serialization error or exception).                                                                        |
| `OnHandlingPacketReceiveFailed` | `Action&lt;TransportError&gt;`                 | Fires when Peer's own receive handling fails (registry error, serialization error, handler exception).                                               |

**Example — send and receive:**

```C#
peer.OnPacketReceived += (packet, invoke) =>
{
    // queue for main loop, or invoke immediately
    invoke(packet);
};

peer.OnPacketSendFailed += (packet, error) =>
{
    if (error == TransportError.SocketError)
        peer.Send(packet);  // retry
};

peer.Send(new ChatMessage { Text = "hello" });
peer.Receive();
```

**Example — retry on failure:**

```C#
peer.OnPacketSendFailed += (packet, error) =>
{
    switch (error)
    {
        case TransportError.SocketError:
        case TransportError.SendAlreadyPending:
            peer.Send(packet);  // retry with fresh segment
            break;
        case TransportError.ObjectDisposed:
        case TransportError.SocketDisconnected:
            // terminal — log and drop
            break;
    }
};
```

**See also:** [`IPacket`](#ipacket), [`TransportError`](#transporterror)

***

### `IPeerClient`

Factory for client peers. Wraps `IClientConnector` with packet registry and encryption.

**Namespace:** `DunePresentation.Peer.Interfaces`

```C#
interface IPeerClient : IDisposable
```

#### Constructor

```C#
new PeerClient(PacketRegistry registry, Func&lt;IPacketEncryptor&gt;? encryptor = null, IClientConnector? client = null)
```

| Parameter   | Type                            | Description                                                             |
| ----------- | ------------------------------- | ----------------------------------------------------------------------- |
| `registry`  | `PacketRegistry`                | Shared packet registry. Must not be null.                               |
| `encryptor` | `Func&lt;IPacketEncryptor&gt;?` | Optional factory that produces an encryptor per connection.             |
| `client`    | `IClientConnector?`             | Optional connector. A new `ClientConnector` is created if not provided. |

#### Methods

| Method                                   | Returns | Description               |
| ---------------------------------------- | ------- | ------------------------- |
| `ConnectAsync(string address, int port)` | `bool`  | Begins async TCP connect. |

#### Events

| Event             | Signature                   | Description                                     |
| ----------------- | --------------------------- | ----------------------------------------------- |
| `OnPeerConnected` | `Action&lt;IPeer&gt;`       | Fires when connect succeeds. Produces a `Peer`. |
| `OnConnectFailed` | `Action&lt;SocketError&gt;` | Fires when connect fails.                       |

**Example:**

```C#
var registry = new PacketRegistry();
registry.RegisterHandler&lt;ChatMessage&gt;(0x0001, HandleChat);

var client = new PeerClient(registry);
client.OnPeerConnected += peer =>
{
    peer.Send(new ChatMessage { Text = "hi" });
    peer.Receive();
};
client.ConnectAsync("127.0.0.1", 5000);
```

***

### `IPeerServer`

Factory for server peers. Wraps `IServerConnector` with packet registry and encryption.

**Namespace:** `DunePresentation.Peer.Interfaces`

```C#
interface IPeerServer : IDisposable
```

#### Constructor

```C#
new PeerServer(PacketRegistry registry, Func&lt;IPacketEncryptor&gt;? encryptor = null, IServerConnector? server = null)
```

| Parameter   | Type                            | Description                                                             |
| ----------- | ------------------------------- | ----------------------------------------------------------------------- |
| `registry`  | `PacketRegistry`                | Shared packet registry. Must not be null.                               |
| `encryptor` | `Func&lt;IPacketEncryptor&gt;?` | Optional factory that produces an encryptor per connection.             |
| `server`    | `IServerConnector?`             | Optional connector. A new `ServerConnector` is created if not provided. |

#### Methods

| Method                                     | Description                  |
| ------------------------------------------ | ---------------------------- |
| `StartListening(string address, int port)` | Binds and starts listening.  |
| `AcceptConnection()`                       | Begins an async accept.      |
| `StopListening()`                          | Closes the listening socket. |

#### Events

| Event             | Signature                   | Description                                      |
| ----------------- | --------------------------- | ------------------------------------------------ |
| `OnPeerConnected` | `Action&lt;IPeer&gt;`       | Fires when a client connects. Produces a `Peer`. |
| `OnAcceptFailed`  | `Action&lt;SocketError&gt;` | Fires when accept fails.                         |

***

### `IPacket`

Application packet abstraction. The application implements field-level serialization; the library handles header writing and buffer slicing.

**Namespace:** `DunePresentation.Packet.Interfaces`

```C#
interface IPacket : ISegmentManager
```

#### Properties

| Property   | Type     | Description                                                                    |
| ---------- | -------- | ------------------------------------------------------------------------------ |
| `PacketId` | `ushort` | Unique identifier for this packet type. Used by `PacketRegistry` for dispatch. |

#### Methods to Implement

| Method                                                               | Description                                                 |
| -------------------------------------------------------------------- | ----------------------------------------------------------- |
| `WriteFieldsToBuffer(Span&lt;byte&gt; buffer, out int bytesWritten)` | Write packet fields into the span. Report bytes written.    |
| `ReadFieldsFromBuffer(ReadOnlySpan&lt;byte&gt; buffer, int length)`  | Read packet fields from the span. Return `true` on success. |

**Example:**

```C#
public class ChatMessage : IPacket
{
    public ushort PacketId => 0x0001;

    public string Text { get; set; }
    public int PacketSize { get; set; }
    public Segment segment { get; set; }

    public void WriteFieldsToBuffer(Span<byte> buffer, out int bytesWritten)
    {
        var bytes = Encoding.UTF8.GetBytes(Text);
        bytesWritten = bytes.Length;
        bytes.CopyTo(buffer);
    }

    public bool ReadFieldsFromBuffer(ReadOnlySpan<byte> buffer, int length)
    {
        Text = Encoding.UTF8.GetString(buffer.Slice(0, length));
        return true;
    }
}
```

**Inherited from** **[`ISegmentManager`](#isegmentmanager):** `Serialize()`, `Deserialize()`, `segment`, `PacketSize`.

***

### `ISegmentManager`

Serialization pipeline. Handles reserve → serialize → callback so the application does not manage segments directly.

**Namespace:** `DuneTransport.BufferManager.Interface`

```C#
interface ISegmentManager
```

#### Properties

| Property     | Type      | Description                                                            |
| ------------ | --------- | ---------------------------------------------------------------------- |
| `segment`    | `Segment` | The rented segment. Set by `Serialize()`, released by `Deserialize()`. |
| `PacketSize` | `int`     | Total packet size (header + fields). Set by `OnSerialize()`.           |

#### Methods

| Method                                                                        | Returns | Description                                                                                                                       |
| ----------------------------------------------------------------------------- | ------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `Serialize(ITransport transport, Action&lt;Segment, int&gt;? afterSerialize)` | `bool`  | Reserves a segment, calls `OnSerialize()`, then runs the after-serialize callback. Returns `false` on failure (segment released). |
| `Deserialize(Action&lt;Segment, int&gt;? beforeDeserialize)`                  | `bool`  | Runs the before-deserialize callback, calls `OnDeserialize()`, then releases the segment.                                         |

***

### `PacketRegistry`

Maps packet IDs to factory + handler pairs. Used by Peer to dispatch received packets.

**Namespace:** `DunePresentation.Packet`

```C#
class PacketRegistry
```

#### Methods

| Method                                                               | Description                                                                                               |
| -------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `RegisterHandler&lt;T&gt;(ushort packetId, Action&lt;T&gt; handler)` | Registers a handler for a packet type. `T` must implement `IPacket` and have a parameterless constructor. |

**Throws:**

* `ArgumentNullException` if `handler` is null.
* `InvalidOperationException` if `packetId` is already registered.

**Example:**

```C#
var registry = new PacketRegistry();
registry.RegisterHandler&lt;ChatMessage&gt;(0x0001, msg =>
{
    Console.WriteLine($"[{msg.PacketId}] {msg.Text}");
});
```

***

### `IPacketEncryptor`

Optional symmetric encryption hook. Applied in-place to the framed payload (header + fields) on both send and receive. The application supplies the implementation.

**Namespace:** `DunePresentation.Encryption.Interface`

```C#
interface IPacketEncryptor
```

#### Methods

| Method                                                                   | Description                                                     |
| ------------------------------------------------------------------------ | --------------------------------------------------------------- |
| `Encrypt(ReadOnlySpan&lt;byte&gt; source, Span&lt;byte&gt; destination)` | Encrypts in-place. Source and destination may be the same span. |
| `Decrypt(ReadOnlySpan&lt;byte&gt; source, Span&lt;byte&gt; destination)` | Decrypts in-place. Source and destination may be the same span. |

***

## Data Flow

### Send Path

```
Application
  peer.Send&lt;T&gt;(packet)
    → packet.Serialize(transport, callback)
      → TryReserveSendPacket(out seg)   // rent segment
      → WriteFieldsToBuffer()           // app writes payload
      → PresentationHeader.Write()      // write packet ID
      → encryptor?.Encrypt()            // optional
    → Transport.SendAsync(seg, size)
      → prepend 2-byte length header
      → socket.SendAsync()
        → success: Release(), OnPacketSent
        → failure: OnPacketSendFailed(segment ownership → Peer → app)
```

### Receive Path

```
Socket data arrives
  → Transport.ReceiveAsync
    → read 2-byte header (payload length)
    → rent payload segment
    → read payload bytes
    → OnPacketReceived (segment ownership → Peer)
  → Peer.OnPacketReceivedHandler
    → decrypt?.Decrypt()
    → PresentationHeader.Read(packetId)
    → registry.TryGetEntry(packetId)
    → packet = factory()
    → packet.Deserialize() → ReadFieldsFromBuffer
    → OnPacketReceived(packet, invoke)
  → Application
    → invoke(packet)  // or queue for later
```

