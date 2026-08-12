using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading;
using DuneTransport.BufferManager;
using DuneTransport.Transport.Interface;

namespace DuneTransport.Transport
{
    /// <summary>
    /// Asynchronous socket transport with two-phase (header/payload) receive and length-prefixed send.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wraps a connected <see cref="Socket"/> and provides an event-based API for
    /// sending and receiving framed packets. Each packet is framed with a 2-byte
    /// little-endian length prefix.
    /// </para>
    /// <para>
    /// <b>Receive flow:</b> Reads the 2-byte header first, allocates a correctly-sized
    /// payload segment, then receives the payload. Delivers the complete segment to
    /// the subscriber via <see cref="OnPacketReceived"/>.
    /// </para>
    /// <para>
    /// <b>Send flow:</b> Application reserves a segment via <see cref="TryReserveSendPacket"/>,
    /// writes payload data, calls <see cref="SendAsync"/> which prepends the length header
    /// and transmits.
    /// </para>
    /// <para>
    /// Only one send and one receive may be in flight simultaneously. Operations are
    /// guarded with <see cref="Interlocked.CompareExchange"/> flags.
    /// </para>
    /// </remarks>
    public class Transport : ITransport
    {
        /// <summary>The size of the length-prefixed header in bytes (2 bytes, little-endian).</summary>
        private const int HeaderSize = 2;

        private readonly Socket socket;

        /// <summary>Segment pool for receiving data. Segments are leased during receive operations.</summary>
        private SegmentedBuffer receiveBuffer { get; }

        /// <summary>Segment pool for sending data. Segments are leased for outgoing packets.</summary>
        private SegmentedBuffer sendBuffer { get; }

        /// <summary>Tracks whether we are currently receiving a header or payload.</summary>
        private enum ReceivePhase { Header, Payload }

        /// <summary>The segment currently being filled during an active receive operation.</summary>
        private Segment currentReceivingSegment;

        /// <summary>The segment currently being transmitted during an active send operation.</summary>
        private Segment currentSendingSegment;

        /// <summary>Current phase of the multi-part receive operation.</summary>
        private ReceivePhase phase;

        /// <summary>Bytes received in the current phase.</summary>
        private int receivedBytes;

        /// <summary>Total bytes expected for the current phase.</summary>
        private int expectedBytes;

        private readonly SocketAsyncEventArgs sendEventArgs;
        private readonly SocketAsyncEventArgs receiveEventArgs;

        /// <summary>Dispose guard: 0 = not disposed, 1 = disposed. Accessed via <see cref="Volatile.Read"/>.</summary>
        private int _disposed;

        /// <summary>Send in-flight guard: 0 = available, 1 = in flight. Accessed via <see cref="Interlocked.CompareExchange"/>.</summary>
        private int _sendInFlight;

        /// <summary>Receive in-flight guard: 0 = available, 1 = in flight. Accessed via <see cref="Interlocked.CompareExchange"/>.</summary>
        private int _receiveInFlight;

        private volatile bool _isConnected = true;

        /// <inheritdoc />
        public bool IsConnected => _isConnected;

        /// <inheritdoc />
        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        /// <inheritdoc />
        public bool ReceiveArmed => _receiveInFlight == 1;

        /// <inheritdoc />
        public bool SendArmed => _sendInFlight == 1;

        public event Action<ITransport>? OnPacketSent;
        public event Action<ITransport, Segment, TransportError>? OnPacketSendFailed;

        public event Action<ITransport, Segment>? OnPacketReceived;
        public event Action<ITransport, TransportError>? OnPacketReceiveFailed;

        /// <summary>
        /// Creates a new transport for the given socket.
        /// </summary>
        /// <param name="socket">An already-connected socket to wrap.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="socket"/> is null.</exception>
        /// <remarks>
        /// <para>
        /// Initializes separate segment pools for sending and receiving. Registers
        /// async I/O completion handlers on <see cref="SocketAsyncEventArgs"/> instances.
        /// </para>
        /// <para>
        /// The socket should be connected before calling this constructor.
        /// Call <see cref="Dispose"/> to release resources when the transport is no longer needed.
        /// </para>
        /// </remarks>
        public Transport(Socket socket)
        {
            this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
            sendBuffer = new SegmentedBuffer();
            receiveBuffer = new SegmentedBuffer();

            sendEventArgs = new SocketAsyncEventArgs();
            receiveEventArgs = new SocketAsyncEventArgs();

            sendEventArgs.Completed += OnPacketSentEventHandler;
            receiveEventArgs.Completed += OnPacketReceivedEventHandler;
        }

        /// <inheritdoc cref="ITransport.ReceiveAsync"/>
        public void ReceiveAsync()
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                OnPacketReceiveFailed?.Invoke(this, TransportError.ObjectDisposed);
                return;
            }
            if (!IsConnected)
            {
                OnPacketReceiveFailed?.Invoke(this, TransportError.SocketDisconnected);
                return;
            }
            if (Interlocked.CompareExchange(ref _receiveInFlight, 1, 0) != 0)
            {
                OnPacketReceiveFailed?.Invoke(this, TransportError.ReceiveAlreadyPending);
                return;
            }

            if (!receiveBuffer.TryReserveSegment(out Segment newSegment))
            {
                Interlocked.Exchange(ref _receiveInFlight, 0);
                OnPacketReceiveFailed?.Invoke(this, TransportError.PoolExhausted);
                return;
            }

            currentReceivingSegment = newSegment;
            phase = ReceivePhase.Header;
            expectedBytes = HeaderSize;
            receivedBytes = 0;

            IssueReceive();
        }

        /// <summary>
        /// Issues the next socket receive operation, handling synchronous completions inline.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Enters a loop where synchronous completions are processed immediately without
        /// returning to the caller. This avoids thread pool overhead for fast connections.
        /// </para>
        /// <para>
        /// On any error (socket exception, dispose race), the current segment is released
        /// and the in-flight flag is cleared before returning.
        /// </para>
        /// </remarks>
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
                        OnPacketReceiveFailed?.Invoke(this, TransportError.SocketError);
                        return;
                    }
                    catch (SocketException)
                    {
                        currentReceivingSegment.Release();
                        Interlocked.Exchange(ref _receiveInFlight, 0);
                        OnPacketReceiveFailed?.Invoke(this, TransportError.SocketError);
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
                // Defensive: an unexpected throw from the loop must not leak the segment.
                currentReceivingSegment.Release();
                Interlocked.Exchange(ref _receiveInFlight, 0);
                throw;
            }
        }

        private void OnPacketReceivedEventHandler(object? sender, SocketAsyncEventArgs e)
        {
            if (ProcessReceive(e))
            {
                IssueReceive();
            }
        }

        /// <summary>
        /// Processes a completed receive operation, handling the two-phase (header/payload) receive pipeline.
        /// </summary>
        /// <param name="onReceived">The completed socket async event arguments.</param>
        /// <returns><c>true</c> if more data should be received (partial phase); otherwise <c>false</c>.</returns>
        /// <remarks>
        /// <para>
        /// Two-phase receive:
        /// <list type="number">
        ///   <item><b>Header phase:</b> Reads 2 bytes (little-endian payload length). Validates length, reserves payload segment.</item>
        ///   <item><b>Payload phase:</b> Reads the declared payload length. On completion, slices segment and fires <see cref="OnPacketReceived"/>.</item>
        /// </list>
        /// </para>
        /// <para>
        /// Early exits: dispose race (silent release), socket error, graceful close (FIN).
        /// </para>
        /// </remarks>
        private bool ProcessReceive(SocketAsyncEventArgs onReceived)
        {
            // Dispose raced with this callback. Release and exit silently.
            if (Volatile.Read(ref _disposed) == 1)
            {
                Interlocked.Exchange(ref _receiveInFlight, 0);
                currentReceivingSegment.Release();
                return false;
            }

            // Socket-level error → release, bubble, stop.
            if (onReceived.SocketError != SocketError.Success)
            {
                Interlocked.Exchange(ref _receiveInFlight, 0);
                currentReceivingSegment.Release();
                OnPacketReceiveFailed?.Invoke(this, TransportError.SocketError);
                return false;
            }

            // Graceful close (FIN) — regardless of current phase.
            if (onReceived.BytesTransferred == 0)
            {
                Interlocked.Exchange(ref _receiveInFlight, 0);
                currentReceivingSegment.Release();
                _isConnected = false;
                OnPacketReceiveFailed?.Invoke(this, TransportError.SocketDisconnected);
                return false;
            }

            receivedBytes += onReceived.BytesTransferred;

            // Partial phase — keep reassembling with the same segment.
            if (receivedBytes < expectedBytes)
            {
                return true;
            }

            if (phase == ReceivePhase.Header)
            {
                ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(currentReceivingSegment.Memory.Span);

                // Done with the header segment regardless of the next branch.
                currentReceivingSegment.Release();

                // Protocol violation: zero-length payload.
                if (payloadLength == 0)
                {
                    Interlocked.Exchange(ref _receiveInFlight, 0);
                    OnPacketReceiveFailed?.Invoke(this, TransportError.ProtocolError);
                    return false;
                }

                // Protocol violation: payload larger than a segment.
                if (payloadLength > receiveBuffer.SegmentSize)
                {
                    Interlocked.Exchange(ref _receiveInFlight, 0);
                    OnPacketReceiveFailed?.Invoke(this, TransportError.ProtocolError);
                    return false;
                }

                // Reserve payload segment.
                if (!receiveBuffer.TryReserveSegment(out Segment payloadSegment))
                {
                    Interlocked.Exchange(ref _receiveInFlight, 0);
                    OnPacketReceiveFailed?.Invoke(this, TransportError.PoolExhausted);
                    return false;
                }

                currentReceivingSegment = payloadSegment;
                phase = ReceivePhase.Payload;
                expectedBytes = payloadLength;
                receivedBytes = 0;
                return true;
            }

            // Payload complete. Slice to actual payload length.
            currentReceivingSegment.Memory = currentReceivingSegment.Memory.Slice(0, expectedBytes);

            // Hand off ownership. Capture locally and clear the field BEFORE clearing
            // the in-flight flag so a racing Dispose cannot observe a cleared flag
            // while the segment is still valid in the field (use-after-release).
            var segmentToDeliver = currentReceivingSegment;
            currentReceivingSegment = default;
            Thread.MemoryBarrier();

            // Clear flag AFTER clearing segment so the subscriber can call
            // ReceiveAsync from the handler. Memory barrier ensures the segment
            // clear is visible before the flag is cleared.
            Interlocked.Exchange(ref _receiveInFlight, 0);

            try
            {
                OnPacketReceived?.Invoke(this, segmentToDeliver);
            }
            catch
            {
                // Handler bug — make sure the segment goes back to the pool.
                // Release is idempotent, so it is safe even if the
                // handler released before throwing.
                segmentToDeliver.Release();
                OnPacketReceiveFailed?.Invoke(this, TransportError.HandlerFailed);
            }

            return false;
        }

        /// <inheritdoc cref="ITransport.TryReserveSendPacket(out Segment)"/>
        public bool TryReserveSendPacket(out Segment segment)
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                segment = default;
                return false;
            }
            if (!sendBuffer.TryReserveSegment(out segment))
                return false;

            segment.Memory = segment.Memory.Slice(HeaderSize);
            return true;
        }

        /// <inheritdoc cref="ITransport.SendAsync(Segment, int)"/>
        public void SendAsync(Segment packet, int packetSize)
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                OnPacketSendFailed?.Invoke(this, packet, TransportError.ObjectDisposed);
                return;
            }

            if (!IsConnected)
            {
                OnPacketSendFailed?.Invoke(this, packet, TransportError.SocketDisconnected);
                return;
            }

            if (Interlocked.CompareExchange(ref _sendInFlight, 1, 0) != 0)
            {
                OnPacketSendFailed?.Invoke(this, packet, TransportError.SendAlreadyPending);
                return;
            }

            if (!sendBuffer.GetRegisteredMemory(packet.SegmentIndex, packetSize + HeaderSize, out Memory<byte> memory))
            {
                Interlocked.Exchange(ref _sendInFlight, 0);
                OnPacketSendFailed?.Invoke(this, packet, TransportError.InvalidSegment);
                return;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(memory.Span.Slice(0, HeaderSize), (ushort)packetSize);

            currentSendingSegment = packet;

            try
            {
                sendEventArgs.SetBuffer(memory);
                if (!socket.SendAsync(sendEventArgs))
                {
                    ProcessSend(sendEventArgs);
                }
            }
            catch (ObjectDisposedException)
            {
                var failed = currentSendingSegment;
                currentSendingSegment = default;
                Interlocked.Exchange(ref _sendInFlight, 0);
                OnPacketSendFailed?.Invoke(this, failed, TransportError.SocketError);
            }
            catch (SocketException)
            {
                var failed = currentSendingSegment;
                currentSendingSegment = default;
                Interlocked.Exchange(ref _sendInFlight, 0);
                OnPacketSendFailed?.Invoke(this, failed, TransportError.SocketError);
            }
        }

        private void OnPacketSentEventHandler(object? sender, SocketAsyncEventArgs e)
        {
            ProcessSend(e);
        }

        /// <summary>
        /// Processes a completed send operation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On socket error: fires <see cref="OnPacketSendFailed"/> with the segment (ownership transfers to subscriber).
        /// On success: releases the segment, fires <see cref="OnPacketSent"/>.
        /// On dispose race: silently releases the segment and returns.
        /// </para>
        /// </remarks>
        private void ProcessSend(SocketAsyncEventArgs onSent)
        {
            // Dispose raced with this callback. Release and exit silently.
            if (Volatile.Read(ref _disposed) == 1)
            {
                var seg = currentSendingSegment;
                currentSendingSegment = default;
                Interlocked.Exchange(ref _sendInFlight, 0);
                seg.Release();
                return;
            }

            if (onSent.SocketError != SocketError.Success)
            {
                var seg = currentSendingSegment;
                currentSendingSegment = default;
                Interlocked.Exchange(ref _sendInFlight, 0);
                if (OnPacketSendFailed != null)
                    OnPacketSendFailed(this, seg, TransportError.SocketError);
                else
                    seg.Release();
                return;
            }

            var sentSeg = currentSendingSegment;
            currentSendingSegment = default;
            Interlocked.Exchange(ref _sendInFlight, 0);
            sentSeg.Release();
            OnPacketSent?.Invoke(this);
        }

        /// <summary>
        /// Releases all resources held by this transport.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Idempotent: calling <see cref="Dispose"/> multiple times is safe.
        /// </para>
        /// <para>
        /// Releases any in-flight segments back to their pools:
        /// <list type="bullet">
        ///   <item>Receiving segment (if <see cref="_receiveInFlight"/> is set)</item>
        ///   <item>Sending segment (if <see cref="_sendInFlight"/> is set)</item>
        /// </list>
        /// </para>
        /// <para>
        /// Unregisters async I/O completion handlers and disposes <see cref="SocketAsyncEventArgs"/> instances.
        /// </para>
        /// <para>
        /// Does NOT close the underlying socket — that is the responsibility of the caller
        /// (typically <see cref="DuneSession.SocketConnectors.Connection"/>).
        /// </para>
        /// </remarks>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }
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

            try
            {
                sendEventArgs.Completed -= OnPacketSentEventHandler;
                receiveEventArgs.Completed -= OnPacketReceivedEventHandler;
                sendEventArgs.Dispose();
                receiveEventArgs.Dispose();
            }
            catch { }

            _isConnected = false;
        }
    }
}
