using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using DuneTransport.BufferManager;
using DuneTransport.Transport.Interface;

namespace DuneTransport.Transport
{
    public class Transport : ITransport
    {
        private const int HeaderSize = 2;

        // Borrowed reference — Connection owns the socket lifecycle.
        // Transport must never close, shutdown, or dispose this socket.
        private readonly Socket socket;

        public SegmentedBuffer receiveBuffer { get; }
        public SegmentedBuffer sendBuffer { get; }

        private enum ReceivePhase { Header, Payload }

        private Segment currentReceivingSegment;
        private Segment currentSendingSegment;

        private ReceivePhase phase;
        private int receivedBytes;
        private int expectedBytes;

        private readonly SocketAsyncEventArgs sendEventArgs;
        private readonly SocketAsyncEventArgs receiveEventArgs;

        private int _disposed;        // 0/1 via Interlocked.Exchange
        private int _sendInFlight;    // 0/1 via Interlocked.CompareExchange
        private int _receiveInFlight; // 0/1 via Interlocked.CompareExchange

        public bool IsConnected { get; private set; } = true;
        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public event Action<ITransport>? OnPacketSent;
        public event Action<ITransport, Segment, TransportError>? OnPacketSendFailed;

        public event Action<ITransport, SocketAsyncEventArgs, Segment>? OnPacketReceived;
        public event Action<ITransport, TransportError>? OnPacketReceiveFailed;

        public event Action? OnDisconnectRequested;

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

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                throw new ObjectDisposedException(nameof(Transport));
            }
        }

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
                        Debug.WriteLine("IssueReceive | ObjectDisposedException", "error");
                        currentReceivingSegment.Release();
                        Interlocked.Exchange(ref _receiveInFlight, 0);
                        OnPacketReceiveFailed?.Invoke(this, TransportError.SocketError);
                        return;
                    }
                    catch (SocketException ex)
                    {
                        Debug.WriteLine($"IssueReceive | SocketException: {ex.Message}", "error");
                        currentReceivingSegment.Release();
                        IsConnected = false;
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

        private bool ProcessReceive(SocketAsyncEventArgs onReceived)
        {
            // Dispose raced with this callback. Release and exit silently.
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
                OnPacketReceiveFailed?.Invoke(this, TransportError.SocketError);
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
                    Debug.WriteLine("ProcessReceive | Zero-length payload rejected.", "error");
                    Interlocked.Exchange(ref _receiveInFlight, 0);
                    OnPacketReceiveFailed?.Invoke(this, TransportError.ProtocolError);
                    OnDisconnectRequested?.Invoke();
                    return false;
                }

                // Protocol violation: payload larger than a segment.
                if (payloadLength > receiveBuffer.SegmentSize)
                {
                    Debug.WriteLine($"ProcessReceive | Oversized payload ({payloadLength} > {receiveBuffer.SegmentSize}) rejected.", "error");
                    Interlocked.Exchange(ref _receiveInFlight, 0);
                    OnPacketReceiveFailed?.Invoke(this, TransportError.ProtocolError);
                    OnDisconnectRequested?.Invoke();
                    return false;
                }

                // Reserve payload segment.
                if (!receiveBuffer.TryReserveSegment(out Segment payloadSegment))
                {
                    Debug.WriteLine("ProcessReceive | Failed to reserve payload segment.", "error");
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

            // Clear flag BEFORE invoke so the subscriber can call ReceiveAsync from the handler.
            Interlocked.Exchange(ref _receiveInFlight, 0);

            // Hand off ownership. Capture locally and clear the field so a
            // racing Dispose doesn't double-release what the subscriber now owns.
            var segmentToDeliver = currentReceivingSegment;
            currentReceivingSegment = default;

            try
            {
                OnPacketReceived?.Invoke(this, onReceived, segmentToDeliver);
            }
            catch
            {
                // Handler bug — make sure the segment goes back to the pool.
                // Release is idempotent (Task 2), so it is safe even if the
                // handler released before throwing.
                segmentToDeliver.Release();
                OnPacketReceiveFailed?.Invoke(this, TransportError.HandlerFailed);
            }

            return false;
        }

        public bool TryReserveSendPacket(out Segment segment)
        {
            ThrowIfDisposed();
            if (!sendBuffer.TryReserveSegment(out segment))
                return false;

            segment.Memory = segment.Memory.Slice(HeaderSize);
            return true;
        }

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

            if (!sendBuffer.GetRegisteredMemory(packet.SegmentIndex, packetSize + HeaderSize, out Memory<byte> memory))
            {
                Interlocked.Exchange(ref _sendInFlight, 0);
                packet.Release();
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
                Debug.WriteLine("SendAsync | ObjectDisposedException", "error");
                var failed = currentSendingSegment;
                currentSendingSegment = default;
                failed.Release();
                Interlocked.Exchange(ref _sendInFlight, 0);
                OnPacketSendFailed?.Invoke(this, failed, TransportError.SocketError);
            }
            catch (SocketException ex)
            {
                Debug.WriteLine($"SendAsync | SocketException: {ex.Message}", "error");
                var failed = currentSendingSegment;
                currentSendingSegment = default;
                failed.Release();
                IsConnected = false;
                Interlocked.Exchange(ref _sendInFlight, 0);
                OnPacketSendFailed?.Invoke(this, failed, TransportError.SocketError);
            }
        }

        private void OnPacketSentEventHandler(object? sender, SocketAsyncEventArgs e)
        {
            ProcessSend(e);
        }

        private void ProcessSend(SocketAsyncEventArgs onSent)
        {
            // Dispose raced with this callback. Release and exit silently.
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
                OnPacketSendFailed?.Invoke(this, seg, TransportError.SocketError);
                return;
            }

            var sentSeg = currentSendingSegment;
            currentSendingSegment = default;
            sentSeg.Release();
            Interlocked.Exchange(ref _sendInFlight, 0);
            OnPacketSent?.Invoke(this);
        }

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

            IsConnected = false;
        }
    }
}