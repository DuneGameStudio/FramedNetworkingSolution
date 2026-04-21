# Transport Safety Hardening — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate every category-(c) unhandled scenario in `DuneTransport.Transport` (segment leaks, concurrent-call corruption, oversized-frame hang, dispose-during-callback UAF, asymmetric error events, external `IsConnected` toggle). No new features.

**Architecture:** Atomic flag guards (`_sendInFlight`, `_receiveInFlight`, `_disposed`) enforce the documented single-flight contract at runtime. Every state-machine exit path accounts for its rented `Segment`. `SegmentedBuffer.ReleaseMemory` becomes idempotent to allow safe release on ambiguous-ownership paths (handler throw, dispose race). Public API throws `ObjectDisposedException`/`InvalidOperationException` instead of silently corrupting state. Failure events carry a `TransportError` enum reason; `ITransport` arg symmetrized across send/receive failure paths.

**Tech Stack:** C# / netstandard2.1 / .NET SDK 10.0 / `SocketAsyncEventArgs` / `System.Threading.Interlocked`.

**Source spec:** `/home/abdulrahman/.claude/plans/superpowers-brainstorming-let-s-make-tr-happy-dijkstra.md`

**Testing note:** The solution has no test project (CLAUDE.md). This plan uses `dotnet build` + `dotnet format --verify-no-changes` as the per-task gate, plus a final Task 8 "validation checklist" that exercises each scenario in a throwaway sandbox console. The sandbox is created under `DuneTransport/samples/HardeningValidation/` and is not added to the solution — it is built ad-hoc with `dotnet run --project` and is removed (or kept, user's call) after Task 8.

**Branch:** Work on the current branch (`dev`) unless the operator sets up a worktree.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `DuneTransport/src/Transport/TransportError.cs` | **Create** | New enum: failure-event reason codes. |
| `DuneTransport/src/BufferManager/SegmentedBuffer.cs` | Modify | `ReleaseMemory(int)` becomes idempotent (no-op on already-free index). |
| `DuneTransport/src/Transport/Interface/ITransport.cs` | Modify | Drop `IsConnected` setter. Add `IsDisposed`. Update event signatures. |
| `DuneTransport/src/Transport/Transport.cs` | Rewrite core | Add flags, rewrite `ReceiveAsync`/`SendAsync`/`ProcessReceive`/`ProcessSend`/`Dispose`, change event invocation sites. |
| `DuneSession/src/SocketConnectors/Connection.cs` | Modify | Update event-handler signatures to match new `ITransport` events. |
| `DunePresentation/src/Peer/Peer.cs` | Modify | Update event-handler signatures to match new `ITransport` events. |
| `DuneTransport/samples/HardeningValidation/Program.cs` | Create (temp) | Sandbox scenario runner for Task 8. Not added to `.slnx`. |

---

## Task 1: Add `TransportError` enum

**Files:**
- Create: `DuneTransport/src/Transport/TransportError.cs`

- [ ] **Step 1: Create enum file**

```csharp
namespace DuneTransport.Transport
{
    /// <summary>
    /// Reason code surfaced with Transport failure events.
    /// </summary>
    public enum TransportError
    {
        /// <summary>Underlying socket I/O failed (OS-level SocketException, ObjectDisposedException on the socket, etc.).</summary>
        SocketError,

        /// <summary>Segment pool had no free segment when one was needed.</summary>
        PoolExhausted,

        /// <summary>Peer sent a malformed frame (header says payload length is 0 or greater than the receive segment size).</summary>
        ProtocolError,

        /// <summary>Caller passed a Segment the send buffer cannot resolve (bad SegmentIndex or oversized length).</summary>
        InvalidSegment,

        /// <summary>User subscriber of OnPacketReceived threw an exception. The segment has been released on the caller's behalf.</summary>
        HandlerFailed,

        /// <summary>The Transport was disposed while the operation was pending. Reserved for boundary cases.</summary>
        ObjectDisposed,
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build DuneNetworking.slnx`
Expected: succeeds with 0 warnings, 0 errors.

- [ ] **Step 3: Format**

Run: `dotnet format DuneNetworking.slnx --verify-no-changes`
Expected: exit 0.

- [ ] **Step 4: Commit**

```bash
git add DuneTransport/src/Transport/TransportError.cs
git commit -m "feat(transport): add TransportError enum for failure-event reasons"
```

---

## Task 2: Make `SegmentedBuffer.ReleaseMemory` idempotent

**Files:**
- Modify: `DuneTransport/src/BufferManager/SegmentedBuffer.cs` — the `ReleaseMemory(int index)` method (today: ~lines 44–52; throws `InvalidOperationException` when `isAllocated[index]` is already false).

**Rationale:** Transport needs to release segments on paths where ownership is ambiguous (handler-throw, dispose-during-in-flight). Rather than tracking ownership in both the Transport and the pool, make the pool's release tolerant of a double-call. The cost is losing "double-release" as a bug detector, but Transport's other guards (`_disposed`, `_inFlight` flags) still catch the underlying bug classes.

- [ ] **Step 1: Locate the method**

Open `DuneTransport/src/BufferManager/SegmentedBuffer.cs`. Find the `ReleaseMemory(int index)` method. Current body (per audit):

```csharp
public void ReleaseMemory(int index)
{
    if (!isAllocated[index])
    {
        throw new InvalidOperationException("Segment is not allocated or index is out of range");
    }
    isAllocated[index] = false;
    freeSegments.Enqueue(index);
}
```

- [ ] **Step 2: Replace method body with idempotent version**

Target code:

```csharp
public void ReleaseMemory(int index)
{
    if (index < 0 || index >= isAllocated.Length)
    {
        throw new ArgumentOutOfRangeException(nameof(index));
    }
    if (!isAllocated[index])
    {
        // Idempotent: already free. Transport relies on this for
        // handler-throw and dispose-race cleanup paths.
        return;
    }
    isAllocated[index] = false;
    freeSegments.Enqueue(index);
}
```

Use `Edit` with `old_string` being the full original method body (including signature), `new_string` being the above.

- [ ] **Step 3: Build**

Run: `dotnet build DuneNetworking.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Format**

Run: `dotnet format DuneNetworking.slnx --verify-no-changes`
Expected: exit 0.

- [ ] **Step 5: Commit**

```bash
git add DuneTransport/src/BufferManager/SegmentedBuffer.cs
git commit -m "refactor(buffer): make SegmentedBuffer.ReleaseMemory idempotent

Double-release of a segment now returns silently instead of throwing.
Transport needs this to release segments on ambiguous-ownership paths
(handler throw, dispose-during-in-flight) without tracking ownership
twice. Out-of-range index still throws."
```

---

## Task 3: Add atomic flags + disposed guard to `Transport.cs` (no interface change yet)

**Files:**
- Modify: `DuneTransport/src/Transport/Transport.cs` — add three `int` fields (`_sendInFlight`, `_receiveInFlight`, `_disposed`); remove the old `disposedValue` bool; gate every public entry point.

**Rationale:** This task adds the enforcement plumbing without changing any public signature or semantics of events. After this commit the Transport throws on concurrent calls and on post-dispose calls, but the interface is unchanged. Consumers still compile.

- [ ] **Step 1: Add `using System.Threading;` to the top of `Transport.cs`**

Use `Edit` to add `using System.Threading;` alongside existing `using` directives near line 1.

- [ ] **Step 2: Replace the `disposedValue` field declaration with three interlocked flags**

Find (per audit, ~line 230 inside the partial dispose region):

```csharp
private bool disposedValue;
```

Replace with:

```csharp
private int _disposed;        // 0/1 via Interlocked.Exchange
private int _sendInFlight;    // 0/1 via Interlocked.CompareExchange
private int _receiveInFlight; // 0/1 via Interlocked.CompareExchange
```

Move the field block up to sit with the other private fields near the top of the class (around line 19–24) for readability. Remove any leftover `disposedValue` references.

- [ ] **Step 3: Add a `IsDisposed` public property**

Near the `IsConnected` declaration (~line 29) add:

```csharp
public bool IsDisposed => Volatile.Read(ref _disposed) == 1;
```

(The `ITransport` interface will gain `IsDisposed { get; }` in Task 7; for now it is just a public property on the concrete class. Leave the `IsConnected` setter in place — it is removed in Task 7.)

- [ ] **Step 4: Add a private throw-if-disposed helper**

Add as a private method on `Transport`:

```csharp
private void ThrowIfDisposed()
{
    if (Volatile.Read(ref _disposed) == 1)
    {
        throw new ObjectDisposedException(nameof(Transport));
    }
}
```

- [ ] **Step 5: Gate the three public entry points**

At the top of `ReceiveAsync()` (~line 52):

```csharp
public void ReceiveAsync()
{
    ThrowIfDisposed();
    if (!IsConnected)
    {
        throw new InvalidOperationException("Transport is not connected.");
    }
    if (Interlocked.CompareExchange(ref _receiveInFlight, 1, 0) != 0)
    {
        throw new InvalidOperationException("ReceiveAsync called while a previous receive is in flight.");
    }

    // ... rest of existing body: reserve segment, init phase, IssueReceive ...
}
```

At the top of `SendAsync(Segment packet, int packetSize)` (~line 161):

```csharp
public void SendAsync(Segment packet, int packetSize)
{
    ThrowIfDisposed();
    if (!IsConnected)
    {
        packet.Release();
        throw new InvalidOperationException("Transport is not connected.");
    }
    if (Interlocked.CompareExchange(ref _sendInFlight, 1, 0) != 0)
    {
        throw new InvalidOperationException("SendAsync called while a previous send is in flight.");
    }

    // ... rest of existing body: GetRegisteredMemory, write header, SetBuffer, socket.SendAsync ...
}
```

At the top of `TryReserveSendPacket(out Segment segment)` (~line 148):

```csharp
public bool TryReserveSendPacket(out Segment segment)
{
    ThrowIfDisposed();

    // ... rest of existing body ...
}
```

**Important:** leave all other behavior unchanged for this task. Downstream tasks 4–6 rewrite the bodies. This task only installs the gates.

- [ ] **Step 6: Clear flags in completion-path code**

In `ProcessReceive()` (~line 94), before every `return` that ends the receive (error, FIN, success-handoff), clear the flag:

```csharp
Interlocked.Exchange(ref _receiveInFlight, 0);
```

Same in `ProcessSend()` (~line 206) for `_sendInFlight`.

For now (this task), place the clears at *existing* terminal points, even if those points are still leaky — Tasks 4–6 rewrite those bodies. The goal here is: the happy path round-trips the flag correctly so the gates work for sequential calls.

- [ ] **Step 7: Build**

Run: `dotnet build DuneNetworking.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 8: Format**

Run: `dotnet format DuneNetworking.slnx --verify-no-changes`
Expected: exit 0.

- [ ] **Step 9: Commit**

```bash
git add DuneTransport/src/Transport/Transport.cs
git commit -m "feat(transport): add interlocked flags + disposed guard

Three atomic fields (_disposed, _sendInFlight, _receiveInFlight) and a
ThrowIfDisposed helper. Public entry points (ReceiveAsync, SendAsync,
TryReserveSendPacket) now throw ObjectDisposedException after Dispose
and InvalidOperationException on concurrent-call or not-connected
violations. Behavior of the receive/send state machines is otherwise
unchanged — leaks are fixed in subsequent commits."
```

---

## Task 4: Rewrite receive state machine — zero-leak exit paths

**Files:**
- Modify: `DuneTransport/src/Transport/Transport.cs` — rewrite `ProcessReceive()` (~lines 94–146) and adjust `IssueReceive()` (~lines 70–88). Keep the existing `OnPacketReceiveFailed` / `OnDisconnectRequested` signatures untouched for this commit; Task 7 changes the signatures.

**Rationale:** Every exit path from `ProcessReceive` must either (a) release the rented segment and clear the flag, or (b) hand ownership to the subscriber. No other outcome is valid. This task implements that rule end-to-end.

**Constant to add** near the top of the class (or verify it matches `HeaderSize` already declared ~line 11):

```csharp
private const int MaxPayloadSize = ushort.MaxValue; // wire cap; actual cap below is also bound by receive segment size
```

(Optional — if `HeaderSize` is already present, you can skip this. The hard cap uses `receiveBuffer.segmentSize`, which is a runtime value.)

- [ ] **Step 1: Replace `ProcessReceive` body**

Target code (replaces lines ~94–146; adjust variable names to match current codebase):

```csharp
private bool ProcessReceive(SocketAsyncEventArgs onReceived)
{
    // Dispose raced with this callback. Release and exit quietly.
    if (Volatile.Read(ref _disposed) == 1)
    {
        currentReceivingSegment.Release();
        Interlocked.Exchange(ref _receiveInFlight, 0);
        return false;
    }

    // Socket-level error → release, bubble, stop.
    if (onReceived.SocketError != SocketError.Success)
    {
        currentReceivingSegment.Release();
        IsConnected = false;
        Interlocked.Exchange(ref _receiveInFlight, 0);
        OnPacketReceiveFailed?.Invoke(this);
        return false;
    }

    // Graceful close (FIN) — regardless of current phase.
    if (onReceived.BytesTransferred == 0)
    {
        currentReceivingSegment.Release();
        IsConnected = false;
        Interlocked.Exchange(ref _receiveInFlight, 0);
        OnDisconnectRequested?.Invoke();
        return false;
    }

    receivedBytes += onReceived.BytesTransferred;

    // Partial phase — re-arm with the same segment.
    if (receivedBytes < expectedBytes)
    {
        return true;
    }

    if (phase == ReceivePhase.Header)
    {
        ushort payloadLength = BitConverter.ToUInt16(
            currentReceivingSegment.Memory.Span.Slice(0, HeaderSize));

        // Release header segment — we are done with it regardless of branch below.
        currentReceivingSegment.Release();

        // Protocol violation: zero-length payload.
        if (payloadLength == 0)
        {
            Interlocked.Exchange(ref _receiveInFlight, 0);
            OnPacketReceiveFailed?.Invoke(this);
            OnDisconnectRequested?.Invoke();
            return false;
        }

        // Protocol violation: oversized payload for our pool.
        if (payloadLength > receiveBuffer.segmentSize)
        {
            Interlocked.Exchange(ref _receiveInFlight, 0);
            OnPacketReceiveFailed?.Invoke(this);
            OnDisconnectRequested?.Invoke();
            return false;
        }

        // Reserve payload segment.
        if (!receiveBuffer.TryReserveSegment(out currentReceivingSegment))
        {
            Interlocked.Exchange(ref _receiveInFlight, 0);
            OnPacketReceiveFailed?.Invoke(this);
            return false;
        }

        phase = ReceivePhase.Payload;
        receivedBytes = 0;
        expectedBytes = payloadLength;
        return true;
    }

    // Payload complete.
    currentReceivingSegment.Memory = currentReceivingSegment.Memory.Slice(0, expectedBytes);

    // Clear flag BEFORE invoke so a well-behaved subscriber can re-arm in the handler.
    Interlocked.Exchange(ref _receiveInFlight, 0);

    var segmentToDeliver = currentReceivingSegment;
    currentReceivingSegment = default;

    try
    {
        OnPacketReceived?.Invoke(this, onReceived, segmentToDeliver);
    }
    catch
    {
        // Handler bug: make sure the segment goes back to the pool.
        segmentToDeliver.Release();
        OnPacketReceiveFailed?.Invoke(this);
    }

    return false;
}
```

**Notes on the edit:**
- The method returns `bool` today (per audit: "ProcessReceive bool return naming unclear"). Keep the return type and meaning: `true` = re-arm loop should continue issuing another `ReceiveAsync`; `false` = stop.
- The existing `IssueReceive()` call site is the loop that consumes this bool — leave it alone.
- `segmentToDeliver` + `currentReceivingSegment = default` prevents a later `Dispose` branch from double-releasing the segment the subscriber now owns.

- [ ] **Step 2: Tighten `IssueReceive()` for error-release symmetry**

Find the existing `IssueReceive()` method (~line 70). Ensure the try/catch around `socket.ReceiveAsync` also releases `currentReceivingSegment` and clears the flag on both `ObjectDisposedException` and `SocketException` branches. Target shape:

```csharp
private void IssueReceive()
{
    receiveEventArgs.SetBuffer(
        currentReceivingSegment.Memory.Slice(receivedBytes, expectedBytes - receivedBytes));

    try
    {
        while (true)
        {
            bool pending;
            try
            {
                pending = socket.ReceiveAsync(receiveEventArgs);
            }
            catch (ObjectDisposedException)
            {
                currentReceivingSegment.Release();
                Interlocked.Exchange(ref _receiveInFlight, 0);
                OnPacketReceiveFailed?.Invoke(this);
                return;
            }
            catch (SocketException)
            {
                currentReceivingSegment.Release();
                Interlocked.Exchange(ref _receiveInFlight, 0);
                IsConnected = false;
                OnPacketReceiveFailed?.Invoke(this);
                return;
            }

            if (pending) return;

            if (!ProcessReceive(receiveEventArgs)) return;

            receiveEventArgs.SetBuffer(
                currentReceivingSegment.Memory.Slice(receivedBytes, expectedBytes - receivedBytes));
        }
    }
    catch
    {
        // Defensive: any unexpected throw from the loop must not leak the segment.
        currentReceivingSegment.Release();
        Interlocked.Exchange(ref _receiveInFlight, 0);
        throw;
    }
}
```

Match the existing method's loop structure — the audit says sync completions are processed in a `while(true)` loop. Keep that; only change error-release behavior.

- [ ] **Step 3: Update `ReceiveAsync` happy-path tail**

After the gates installed in Task 3 Step 5, the rest of `ReceiveAsync` should be:

```csharp
if (!receiveBuffer.TryReserveSegment(out currentReceivingSegment))
{
    Interlocked.Exchange(ref _receiveInFlight, 0);
    OnPacketReceiveFailed?.Invoke(this);
    return;
}

phase = ReceivePhase.Header;
receivedBytes = 0;
expectedBytes = HeaderSize;

IssueReceive();
```

If the existing body already resembles this, just confirm the flag clear on the `TryReserveSegment` failure branch.

- [ ] **Step 4: Build**

Run: `dotnet build DuneNetworking.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Format**

Run: `dotnet format DuneNetworking.slnx --verify-no-changes`
Expected: exit 0.

- [ ] **Step 6: Commit**

```bash
git add DuneTransport/src/Transport/Transport.cs
git commit -m "fix(transport): zero-leak exit paths in receive state machine

Every ProcessReceive/IssueReceive return path now either releases the
rented segment or hands ownership to the subscriber. Oversized and
zero-length payloads are rejected as protocol violations with an
explicit OnDisconnectRequested. The receive-in-flight flag is cleared
before OnPacketReceived is invoked so subscribers can re-arm from
inside the handler. Handler exceptions no longer leak the segment."
```

---

## Task 5: Rewrite send path — zero-leak exit paths

**Files:**
- Modify: `DuneTransport/src/Transport/Transport.cs` — rewrite `SendAsync` body and `ProcessSend` (~lines 161–215).

- [ ] **Step 1: Replace `SendAsync` body (keep gates from Task 3)**

Target code for the body after the gates from Task 3:

```csharp
public void SendAsync(Segment packet, int packetSize)
{
    ThrowIfDisposed();

    if (!IsConnected)
    {
        packet.Release();
        throw new InvalidOperationException("Transport is not connected.");
    }

    if (Interlocked.CompareExchange(ref _sendInFlight, 1, 0) != 0)
    {
        throw new InvalidOperationException("SendAsync called while a previous send is in flight.");
    }

    if (!sendBuffer.GetRegisteredMemory(packet, packetSize + HeaderSize, out Memory<byte> memory))
    {
        Interlocked.Exchange(ref _sendInFlight, 0);
        packet.Release();
        OnPacketSendFailed?.Invoke(this, packet);
        return;
    }

    if (!BitConverter.TryWriteBytes(memory.Span.Slice(0, HeaderSize), (ushort)packetSize))
    {
        Interlocked.Exchange(ref _sendInFlight, 0);
        packet.Release();
        OnPacketSendFailed?.Invoke(this, packet);
        return;
    }

    currentSendingSegment = packet;
    sendEventArgs.SetBuffer(memory);

    try
    {
        bool pending = socket.SendAsync(sendEventArgs);
        if (!pending)
        {
            ProcessSend(sendEventArgs);
        }
    }
    catch (ObjectDisposedException)
    {
        var failed = currentSendingSegment;
        currentSendingSegment = default;
        failed.Release();
        Interlocked.Exchange(ref _sendInFlight, 0);
        OnPacketSendFailed?.Invoke(this, failed);
    }
    catch (SocketException)
    {
        var failed = currentSendingSegment;
        currentSendingSegment = default;
        failed.Release();
        IsConnected = false;
        Interlocked.Exchange(ref _sendInFlight, 0);
        OnPacketSendFailed?.Invoke(this, failed);
    }
}
```

**Note:** the event invocation uses the current (Task 5) signature `OnPacketSendFailed?.Invoke(this, packet)` — keep it matching what the interface has at this moment. Task 7 switches it to `(this, packet, TransportError)`.

For the `this` argument here: the current event is `EventHandler<Segment>` which takes `(object? sender, Segment e)`. Adjust the call to match: `OnPacketSendFailed?.Invoke(this, packet);` works if the sender is `this`. If the existing code uses a different sender convention, match it exactly.

- [ ] **Step 2: Replace `ProcessSend` body**

Target code (replaces ~lines 206–215):

```csharp
private void ProcessSend(SocketAsyncEventArgs onSent)
{
    if (Volatile.Read(ref _disposed) == 1)
    {
        var seg = currentSendingSegment;
        currentSendingSegment = default;
        seg.Release();
        Interlocked.Exchange(ref _sendInFlight, 0);
        return;
    }

    if (onSent.SocketError != SocketError.Success)
    {
        var seg = currentSendingSegment;
        currentSendingSegment = default;
        seg.Release();
        IsConnected = false;
        Interlocked.Exchange(ref _sendInFlight, 0);
        OnPacketSendFailed?.Invoke(this, seg);
        return;
    }

    var sentSeg = currentSendingSegment;
    currentSendingSegment = default;
    sentSeg.Release();
    Interlocked.Exchange(ref _sendInFlight, 0);
    OnPacketSent?.Invoke(this, onSent);
}
```

**Note:** `OnPacketSent` today is `EventHandler<SocketAsyncEventArgs>` — the invocation signature matches the current interface. Task 7 changes it to `Action<ITransport>` and we'll drop `onSent` from the invoke. For now, keep the existing signature.

- [ ] **Step 3: Build**

Run: `dotnet build DuneNetworking.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Format**

Run: `dotnet format DuneNetworking.slnx --verify-no-changes`
Expected: exit 0.

- [ ] **Step 5: Commit**

```bash
git add DuneTransport/src/Transport/Transport.cs
git commit -m "fix(transport): zero-leak exit paths in send state machine

SendAsync now releases the caller's segment on every failure path:
disconnected-before-send, GetRegisteredMemory failure, header-write
failure, ObjectDisposedException, and SocketException. The send-in-
flight flag is cleared before firing success/failure events so the
subscriber can re-send from the handler. currentSendingSegment is
reset to default after each completion to prevent a stale release on
the next send."
```

---

## Task 6: Rewrite Dispose — stamp-and-guard

**Files:**
- Modify: `DuneTransport/src/Transport/Transport.cs` — replace the `Dispose(bool)` / `Dispose()` pair (~lines 219–248).

- [ ] **Step 1: Replace the dispose region**

Target code:

```csharp
public void Dispose()
{
    if (Interlocked.Exchange(ref _disposed, 1) == 1)
    {
        return;
    }

    // Best-effort signal to the peer and to the OS so in-flight I/O
    // completes with an error. Callbacks that land after this point
    // see _disposed == 1 and take the release-and-exit branch.
    try { socket.Shutdown(SocketShutdown.Both); } catch { }
    try { socket.Close(); } catch { }

    // Sweep any segment still rented at dispose time. ReleaseMemory is
    // idempotent (see SegmentedBuffer), so a racing callback that
    // releases first is safe.
    if (Interlocked.Exchange(ref _receiveInFlight, 0) == 1)
    {
        var seg = currentReceivingSegment;
        currentReceivingSegment = default;
        seg.Release();
    }
    if (Interlocked.Exchange(ref _sendInFlight, 0) == 1)
    {
        var seg = currentSendingSegment;
        currentSendingSegment = default;
        seg.Release();
    }

    sendEventArgs.Completed -= OnPacketSentEventHandler;
    receiveEventArgs.Completed -= OnPacketReceivedEventHandler;
    sendEventArgs.Dispose();
    receiveEventArgs.Dispose();

    try { socket.Dispose(); } catch { }

    IsConnected = false;
}
```

- [ ] **Step 2: Remove any now-unreferenced protected `Dispose(bool)` pattern**

If the old code used the full IDisposable pattern (`protected virtual void Dispose(bool disposing)` + finalizer), simplify to just `public void Dispose()` since Transport holds only managed resources (SAEAs + Socket, all IDisposable). No finalizer needed. Confirm no override consumers exist.

- [ ] **Step 3: Build**

Run: `dotnet build DuneNetworking.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Format**

Run: `dotnet format DuneNetworking.slnx --verify-no-changes`
Expected: exit 0.

- [ ] **Step 5: Commit**

```bash
git add DuneTransport/src/Transport/Transport.cs
git commit -m "fix(transport): stamp-and-guard Dispose

Dispose now (1) sets _disposed atomically first, (2) closes the socket
so any in-flight I/O completes with an error, (3) releases any segment
still rented by receive or send flight, (4) unsubscribes and disposes
the SAEAs. Callbacks that race Dispose observe _disposed == 1 and
release-and-exit silently. Double Dispose is a no-op. No async drain."
```

---

## Task 7: Interface rewrite — `TransportError` reason, symmetric signatures, read-only `IsConnected`, `IsDisposed`

**Files:**
- Modify: `DuneTransport/src/Transport/Interface/ITransport.cs`
- Modify: `DuneTransport/src/Transport/Transport.cs` (update event declarations + invocation sites to match new interface)
- Modify: `DuneSession/src/SocketConnectors/Connection.cs` (handler signatures)
- Modify: `DunePresentation/src/Peer/Peer.cs` (handler signatures)

**Rationale:** This is the one commit where compilation is gated on coordinated edits across projects. The interface change ripples into Connection (session layer) and Peer (presentation layer) which subscribe to Transport events. Edit all four files, then build.

- [ ] **Step 1: Replace `ITransport.cs`**

Target content (overwrites the whole file):

```csharp
using System;
using System.Net.Sockets;
using DuneTransport.BufferManager;

namespace DuneTransport.Transport.Interface
{
    public interface ITransport : IDisposable
    {
        bool IsConnected { get; }
        bool IsDisposed { get; }

        SegmentedBuffer receiveBuffer { get; }
        SegmentedBuffer sendBuffer { get; }

        event Action<ITransport>? OnPacketSent;
        event Action<ITransport, Segment, TransportError>? OnPacketSendFailed;
        event Action<ITransport, SocketAsyncEventArgs, Segment>? OnPacketReceived;
        event Action<ITransport, TransportError>? OnPacketReceiveFailed;
        event Action? OnDisconnectRequested;

        void ReceiveAsync();
        void SendAsync(Segment packet, int packetSize);
        bool TryReserveSendPacket(out Segment segment);
    }
}
```

- [ ] **Step 2: Update event declarations in `Transport.cs`**

The old block (~lines 31–35) should now read:

```csharp
public event Action<ITransport>? OnPacketSent;
public event Action<ITransport, Segment, TransportError>? OnPacketSendFailed;
public event Action<ITransport, SocketAsyncEventArgs, Segment>? OnPacketReceived;
public event Action<ITransport, TransportError>? OnPacketReceiveFailed;
public event Action? OnDisconnectRequested;
```

Make `IsConnected`'s setter private (keep the backing field private set). Concrete signature:

```csharp
public bool IsConnected { get; private set; }
```

- [ ] **Step 3: Update every event invocation site in `Transport.cs`**

Replacements (search for each):

| Old | New |
|-----|-----|
| `OnPacketSent?.Invoke(this, onSent);` | `OnPacketSent?.Invoke(this);` |
| `OnPacketSendFailed?.Invoke(this, packet);` *(or `?.Invoke(this, seg);`)* | `OnPacketSendFailed?.Invoke(this, packet, reason);` with `reason` = one of `TransportError.SocketError`, `TransportError.InvalidSegment` depending on branch |
| `OnPacketReceiveFailed?.Invoke(this);` | `OnPacketReceiveFailed?.Invoke(this, reason);` where `reason` ∈ { `SocketError`, `PoolExhausted`, `ProtocolError`, `HandlerFailed` } |

Reasons per branch (use the matrix below — do not guess):

| Branch in Transport.cs | Reason |
|------------------------|--------|
| ReceiveAsync: `TryReserveSegment` fails | `PoolExhausted` |
| IssueReceive catch `ObjectDisposedException` | `SocketError` |
| IssueReceive catch `SocketException` | `SocketError` |
| ProcessReceive: `SocketError != Success` | `SocketError` |
| ProcessReceive: `payloadLength == 0` | `ProtocolError` |
| ProcessReceive: `payloadLength > segmentSize` | `ProtocolError` |
| ProcessReceive: payload `TryReserveSegment` fails | `PoolExhausted` |
| ProcessReceive: handler throw | `HandlerFailed` |
| SendAsync: `GetRegisteredMemory` fails | `InvalidSegment` |
| SendAsync: header `TryWriteBytes` fails | `InvalidSegment` |
| SendAsync catch `ObjectDisposedException` | `SocketError` |
| SendAsync catch `SocketException` | `SocketError` |
| ProcessSend: `SocketError != Success` | `SocketError` |

Also: remove the `using` for any import no longer needed on the event signatures.

- [ ] **Step 4: Fix `DuneSession/src/SocketConnectors/Connection.cs`**

Find any subscription to `ITransport` events. The likely spots:

```csharp
transport.OnPacketReceiveFailed += SomeHandler;
transport.OnPacketSendFailed += SomeHandler;
transport.OnPacketSent += SomeHandler;
```

Update handler signatures:
- `OnPacketReceiveFailed`: method signature becomes `void Handler(ITransport t, TransportError reason)`.
- `OnPacketSendFailed`: `void Handler(ITransport t, Segment seg, TransportError reason)`.
- `OnPacketSent`: `void Handler(ITransport t)`.

If Connection previously set `transport.IsConnected = ...`, remove those assignments — the setter is gone. Connection should not have been setting this; if it did, the corresponding logic (e.g., disconnect flow) is now driven by `OnDisconnectRequested` / socket errors.

- [ ] **Step 5: Fix `DunePresentation/src/Peer/Peer.cs`**

Peer subscribes to `OnPacketReceived` (signature unchanged) and may subscribe to `OnPacketReceiveFailed` (signature changed — add `TransportError reason` arg). Update handler signatures accordingly. No behavior change.

- [ ] **Step 6: Build**

Run: `dotnet build DuneNetworking.slnx`
Expected: 0 warnings, 0 errors. **If the build fails**, the error will point at the exact unresolved delegate signature. Fix the signature, re-run. Do not paper over type mismatches with `object?` casts.

- [ ] **Step 7: Format**

Run: `dotnet format DuneNetworking.slnx --verify-no-changes`
Expected: exit 0.

- [ ] **Step 8: Commit**

```bash
git add \
  DuneTransport/src/Transport/Interface/ITransport.cs \
  DuneTransport/src/Transport/Transport.cs \
  DuneSession/src/SocketConnectors/Connection.cs \
  DunePresentation/src/Peer/Peer.cs
git commit -m "feat(transport): TransportError reason on failure events, read-only IsConnected, IsDisposed

ITransport:
- IsConnected is now get-only (Transport flips it internally on socket
  error / dispose / graceful close)
- IsDisposed added
- OnPacketSent: Action<ITransport> (dropped raw SocketAsyncEventArgs)
- OnPacketSendFailed: Action<ITransport, Segment, TransportError>
- OnPacketReceiveFailed: Action<ITransport, TransportError>
- OnPacketReceived: unchanged

OnPacketSendFailed ownership contract: Transport has already released
the segment; the subscriber receives the Segment struct for inspection
only and must not call Release.

Consumers (Connection, Peer) updated to the new handler signatures."
```

---

## Task 8: End-to-end validation via throwaway sandbox

**Files:**
- Create (temp): `DuneTransport/samples/HardeningValidation/HardeningValidation.csproj`
- Create (temp): `DuneTransport/samples/HardeningValidation/Program.cs`

**Goal:** exercise every fixed scenario and verify pool-free count returns to baseline.

- [ ] **Step 1: Create the sandbox project file**

`DuneTransport/samples/HardeningValidation/HardeningValidation.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <RootNamespace>HardeningValidation</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\DuneTransport.csproj" />
  </ItemGroup>
</Project>
```

*(Not added to `DuneNetworking.slnx`. Run ad-hoc via `dotnet run --project`.)*

- [ ] **Step 2: Create the sandbox program**

`DuneTransport/samples/HardeningValidation/Program.cs`:

```csharp
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DuneTransport.BufferManager;
using DuneTransport.Transport;

namespace HardeningValidation
{
    internal static class Program
    {
        private static int _pass, _fail;

        private static void Main()
        {
            RunLoopback("concurrent-receive-throws",    TestConcurrentReceiveThrows);
            RunLoopback("concurrent-send-throws",       TestConcurrentSendThrows);
            RunLoopback("send-on-disconnected-releases",TestSendOnDisconnectedReleases);
            RunLoopback("oversized-frame-rejects",      TestOversizedFrameRejects);
            RunLoopback("zero-length-frame-rejects",    TestZeroLengthFrameRejects);
            RunLoopback("fin-mid-header-cleans-up",     TestFinMidHeaderCleansUp);
            RunLoopback("handler-throw-no-leak",        TestHandlerThrowNoLeak);
            RunLoopback("dispose-during-receive",       TestDisposeDuringReceive);
            RunLoopback("double-dispose-noop",          TestDoubleDisposeNoop);
            Console.WriteLine($"\n{_pass} passed, {_fail} failed.");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }

        private static void RunLoopback(string name, Action<DuneTransport.Transport.Transport, Socket> body)
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            var port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            using var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.Connect(IPAddress.Loopback, port);
            using var serverSocket = listener.Accept();

            var t = new DuneTransport.Transport.Transport(clientSocket) { /* set IsConnected if exposed via ctor; otherwise the Session layer normally does. For sandbox, see comment. */ };
            // NOTE: if IsConnected is set-by-Transport-only, the sandbox needs to use the same flow the Session layer uses.
            // Wire up whatever startup the real stack does; a helper extension on Transport for tests is acceptable here.

            try
            {
                body(t, serverSocket);
                Report(name, true, null);
            }
            catch (Exception e)
            {
                Report(name, false, e.ToString());
            }
            finally
            {
                try { t.Dispose(); } catch { }
            }
        }

        private static void Report(string name, bool ok, string? err)
        {
            if (ok) { _pass++; Console.WriteLine($"PASS  {name}"); }
            else    { _fail++; Console.WriteLine($"FAIL  {name}\n      {err}"); }
        }

        // -------- Tests (each must leave the Transport's pools with zero allocated segments.) --------

        private static void TestConcurrentReceiveThrows(DuneTransport.Transport.Transport t, Socket peer)
        {
            t.ReceiveAsync();
            try { t.ReceiveAsync(); throw new Exception("expected InvalidOperationException"); }
            catch (InvalidOperationException) { }
            AssertBaseline(t);
        }

        private static void TestConcurrentSendThrows(DuneTransport.Transport.Transport t, Socket peer)
        {
            if (!t.TryReserveSendPacket(out var a)) throw new Exception("reserve a");
            if (!t.TryReserveSendPacket(out var b)) throw new Exception("reserve b");
            t.SendAsync(a, 0);
            try { t.SendAsync(b, 0); throw new Exception("expected InvalidOperationException"); }
            catch (InvalidOperationException) { b.Release(); }
            Thread.Sleep(50);
            AssertBaseline(t);
        }

        private static void TestSendOnDisconnectedReleases(DuneTransport.Transport.Transport t, Socket peer)
        {
            peer.Close();
            Thread.Sleep(50);
            t.TryReserveSendPacket(out var p);
            try { t.SendAsync(p, 0); } catch (InvalidOperationException) { }
            AssertBaseline(t);
        }

        private static void TestOversizedFrameRejects(DuneTransport.Transport.Transport t, Socket peer)
        {
            var rejected = false;
            t.OnPacketReceiveFailed += (_, reason) => { if (reason == TransportError.ProtocolError) rejected = true; };
            t.ReceiveAsync();
            var overSize = (ushort)(t.receiveBuffer.segmentSize + 1);
            peer.Send(BitConverter.GetBytes(overSize));
            Thread.Sleep(100);
            if (!rejected) throw new Exception("oversized frame not rejected");
            AssertBaseline(t);
        }

        private static void TestZeroLengthFrameRejects(DuneTransport.Transport.Transport t, Socket peer)
        {
            var rejected = false;
            t.OnPacketReceiveFailed += (_, reason) => { if (reason == TransportError.ProtocolError) rejected = true; };
            t.ReceiveAsync();
            peer.Send(BitConverter.GetBytes((ushort)0));
            Thread.Sleep(100);
            if (!rejected) throw new Exception("zero frame not rejected");
            AssertBaseline(t);
        }

        private static void TestFinMidHeaderCleansUp(DuneTransport.Transport.Transport t, Socket peer)
        {
            var disc = false;
            t.OnDisconnectRequested += () => disc = true;
            t.ReceiveAsync();
            peer.Send(new byte[] { 0x01 }); // 1 byte of a 2-byte header
            peer.Shutdown(SocketShutdown.Send);
            Thread.Sleep(100);
            if (!disc) throw new Exception("FIN not observed");
            AssertBaseline(t);
        }

        private static void TestHandlerThrowNoLeak(DuneTransport.Transport.Transport t, Socket peer)
        {
            var failed = false;
            t.OnPacketReceived += (_, __, seg) => throw new Exception("handler bug");
            t.OnPacketReceiveFailed += (_, reason) => { if (reason == TransportError.HandlerFailed) failed = true; };
            t.ReceiveAsync();
            peer.Send(BitConverter.GetBytes((ushort)1));
            peer.Send(new byte[] { 0x42 });
            Thread.Sleep(100);
            if (!failed) throw new Exception("HandlerFailed not raised");
            AssertBaseline(t);
        }

        private static void TestDisposeDuringReceive(DuneTransport.Transport.Transport t, Socket peer)
        {
            t.ReceiveAsync();
            t.Dispose();
            try { t.ReceiveAsync(); throw new Exception("expected ObjectDisposedException"); }
            catch (ObjectDisposedException) { }
            AssertBaseline(t);
        }

        private static void TestDoubleDisposeNoop(DuneTransport.Transport.Transport t, Socket peer)
        {
            t.Dispose();
            t.Dispose();
            AssertBaseline(t);
        }

        private static void AssertBaseline(DuneTransport.Transport.Transport t)
        {
            var rcv = t.receiveBuffer.SegmentCount - t.receiveBuffer.FreeCount;
            var snd = t.sendBuffer.SegmentCount    - t.sendBuffer.FreeCount;
            if (rcv != 0 || snd != 0)
                throw new Exception($"pool leaked: receive={rcv}, send={snd}");
        }
    }
}
```

**Notes for the engineer:**
- `SegmentedBuffer.SegmentCount` / `FreeCount` may not exist today. If so, add two read-only properties to `SegmentedBuffer` as part of this task (small, backward-compatible addition; `SegmentCount => isAllocated.Length`, `FreeCount => freeSegments.Count`). Commit the getter addition separately if that keeps the diff clean.
- The sandbox assumes Transport's ctor sets `IsConnected = true` after the socket is connected, *or* that there is an internal path the sandbox can use. If Transport's real bring-up path lives in the Session layer, add a minimal helper (e.g., a public `MarkConnected()` on Transport only visible inside the sandbox via `InternalsVisibleTo`), or use the full Session layer `ClientConnector` / `ServerConnector` to bring the transports up. Choose whichever is less invasive.

- [ ] **Step 3: Run the sandbox**

```bash
dotnet run --project DuneTransport/samples/HardeningValidation
```

Expected output:

```
PASS  concurrent-receive-throws
PASS  concurrent-send-throws
PASS  send-on-disconnected-releases
PASS  oversized-frame-rejects
PASS  zero-length-frame-rejects
PASS  fin-mid-header-cleans-up
PASS  handler-throw-no-leak
PASS  dispose-during-receive
PASS  double-dispose-noop

9 passed, 0 failed.
```

Exit code: 0.

**If any fail:** the failure message identifies which guarantee is broken; go back to the task that introduced that path (4, 5, or 6).

- [ ] **Step 4: Commit**

Two options:
- **Keep the sandbox** (recommended) — useful for manual regression checks. Commit:

  ```bash
  git add DuneTransport/samples/HardeningValidation/
  git commit -m "test(transport): add HardeningValidation sandbox for manual scenario coverage"
  ```

- **Discard the sandbox** — if the project prefers to stay lean until there's a real test project:

  ```bash
  rm -rf DuneTransport/samples/HardeningValidation/
  ```

  No commit needed (files were never staged).

- [ ] **Step 5: Final build + format check**

```bash
dotnet build DuneNetworking.slnx
dotnet format DuneNetworking.slnx --verify-no-changes
```

Both: exit 0.

---

## Verification summary

End-of-plan gate: every one of the 14 audit scenarios has a fix landed in a specific task, and Task 8 exercises each one end-to-end.

| Audit # | Scenario | Fixed in |
|---------|----------|----------|
| 1, 2 | Oversized / 0xFFFF payload | Task 4 |
| 3 | Concurrent `ReceiveAsync` | Task 3 (gate) + Task 4 (release on throw-path) |
| 4 | Concurrent `SendAsync` | Task 3 (gate) + Task 5 |
| 5 | `SendAsync` on `!IsConnected` | Task 3 (gate) + Task 5 (pre-release) |
| 6 | `GetRegisteredMemory` fail | Task 5 |
| 7 | Partial header + socket error | Task 4 |
| 8 | FIN mid-phase | Task 4 |
| 9 | Handler throws | Task 4 |
| 10, 12 | Dispose during callback / API after Dispose | Task 3 (gate) + Task 6 |
| 11 | In-flight segment at Dispose | Task 6 |
| 13 | Double-release race | Task 2 |
| 14 | External `IsConnected` toggle | Task 7 |

## Out of scope

- `ShutdownAsync` with drain.
- `CancellationToken` on send/receive.
- Keepalive / dead-peer detection.
- Structured diagnostics / pool-stats events.
- Backpressure events.
- Real test project (`dotnet test`-driven).
- Fixes outside Transport (e.g., `_isListening` race in `ServerConnector`).
