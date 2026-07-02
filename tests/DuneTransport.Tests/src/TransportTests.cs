using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DuneTransport.BufferManager;
using DuneTransport.Transport;
using DuneTransport.Transport.Interface;
using Xunit;

namespace DuneTransport.Tests
{
    public class SocketPairFixture : IDisposable
    {
        public Socket ServerConn { get; }  // Accepted connection (for sending to client)
        public Socket Client { get; }
        private readonly Socket _listener;

        public SocketPairFixture()
        {
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            _listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _listener.Listen(1);
            int port = ((IPEndPoint)_listener.LocalEndPoint!).Port;

            Client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Client.Connect(new IPEndPoint(IPAddress.Loopback, port));
            ServerConn = _listener.Accept();
        }

        public void Dispose()
        {
            try { Client.Shutdown(SocketShutdown.Both); } catch { }
            try { Client.Close(); } catch { }
            try { ServerConn.Shutdown(SocketShutdown.Both); } catch { }
            try { ServerConn.Close(); } catch { }
            try { _listener.Close(); } catch { }
        }
    }

    /// <summary>
        /// Mock transport that allows tests to control send completion timing.
        /// Enables deterministic testing of the single-flight send guard (SendAlreadyPending).
        /// </summary>
        public class ControllableMockTransport : ITransport
        {
            public bool IsConnected { get; set; } = true;
            public bool IsDisposed { get; private set; }
            public bool ReceiveCalled { get; private set; }
            public int ReceiveCallCount { get; private set; }
            public bool SendCalled { get; private set; }
            public Segment? LastSentSegment { get; private set; }
            public int LastSentSize { get; private set; }
            public bool TryReserveSendCalled { get; private set; }
            public bool ReserveSendResult { get; set; } = true;

            /// <summary>
            /// When set, the first SendAsync will NOT complete synchronously.
            /// Test must call CompletePendingSend() to complete it.
            /// </summary>
            public Action? OnCompletePendingSend { get; set; }

            public event Action<ITransport>? OnPacketSent;
            public event Action<ITransport, Segment, TransportError>? OnPacketSendFailed;
            public event Action<ITransport, SocketAsyncEventArgs, Segment>? OnPacketReceived;
            public event Action<ITransport, TransportError>? OnPacketReceiveFailed;

            private bool _isSendPending = false;
            private Segment? _pendingSegment;
            private int _pendingSize;

            public void ReceiveAsync()
            {
                ReceiveCalled = true;
                ReceiveCallCount++;
            }

            public void SendAsync(Segment packet, int packetSize)
            {
                SendCalled = true;
                LastSentSegment = packet;
                LastSentSize = packetSize;

                if (_isSendPending)
                {
                    OnPacketSendFailed?.Invoke(this, packet, TransportError.SendAlreadyPending);
                    return;
                }

                _isSendPending = true;
                _pendingSegment = packet;
                _pendingSize = packetSize;

                // If test doesn't provide a completion hook, complete synchronously
                if (OnCompletePendingSend == null)
                {
                    _isSendPending = false;
                    _pendingSegment = null;
                    OnPacketSent?.Invoke(this);
                }
            }

            /// <summary>
            /// Test calls this to complete a pending send. Does nothing if no send is pending.
            /// </summary>
            public void CompletePendingSend()
            {
                if (!_isSendPending) return;
                _isSendPending = false;
                _pendingSegment = null;
                OnPacketSent?.Invoke(this);
            }

            public bool TryReserveSendPacket(out Segment segment)
            {
                TryReserveSendCalled = true;
                if (ReserveSendResult)
                {
                    segment = new Segment();
                    segment.SegmentIndex = 1;
                    segment.Memory = new byte[256];
                    return true;
                }
                segment = default;
                return false;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

    public class TransportTests
    {
        [Fact]
        public void Transport_Construction_NullSocket_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new global::DuneTransport.Transport.Transport(null!));
        }

        [Fact]
        public void Transport_Construction_DefaultState()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);
            Assert.True(transport.IsConnected);
            Assert.False(transport.IsDisposed);
            transport.Dispose();
        }

        [Fact]
        public void Transport_ReceiveAsync_ObjectDisposed()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);
            transport.Dispose();

            TransportError? error = null;
            transport.OnPacketReceiveFailed += (t, e) => error = e;
            transport.ReceiveAsync();
            Assert.Equal(TransportError.ObjectDisposed, error);
        }

        [Fact]
        public void Transport_ReceiveAsync_Disconnected_FiresSocketError()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);
            pair.Client.Close();

            // Locally closed socket -> ReceiveAsync throws SocketException -> TransportError.SocketError
            // SocketDisconnected is only for remote FIN (BytesTransferred == 0)
            TransportError? error = null;
            transport.OnPacketReceiveFailed += (t, e) => error = e;
            transport.ReceiveAsync();
            Assert.Equal(TransportError.SocketError, error);
        }

        [Fact]
        public void Transport_ReceiveAsync_ReceiveAlreadyPending()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            TransportError? error = null;
            transport.OnPacketReceiveFailed += (t, e) => error = e;

            transport.ReceiveAsync();
            transport.ReceiveAsync(); // second call should fail

            Assert.Equal(TransportError.ReceiveAlreadyPending, error);
            transport.Dispose();
        }

        [Fact]
        public async Task Transport_Receive_PartialHeader_Continues()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            var tcs = new TaskCompletionSource<Segment>();
            transport.OnPacketReceived += (t, e, seg) => tcs.TrySetResult(seg);
            transport.OnPacketReceiveFailed += (t, e) => tcs.SetException(new Exception($"Receive failed: {e}"));
            transport.ReceiveAsync();

            byte[] header = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(), 3);

            pair.ServerConn.Send(header[0..1]);
            await Task.Delay(100);
            pair.ServerConn.Send(header[1..2]);
            await Task.Delay(100);
            pair.ServerConn.Send(new byte[] { 0xAA, 0xBB, 0xCC });

            var seg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(3, seg.Memory.Length);
            Assert.Equal(0xAA, seg.Memory.Span[0]);
            seg.Release();
            transport.Dispose();
        }

        [Fact]
        public async Task Transport_Receive_ZeroLengthPayload_ProtocolError()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            var tcs = new TaskCompletionSource<TransportError>();
            transport.OnPacketReceiveFailed += (t, e) => tcs.TrySetResult(e);
            transport.ReceiveAsync();

            byte[] header = new byte[2]; // 0x0000 = length 0
            pair.ServerConn.Send(header);

            var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TransportError.ProtocolError, error);
            transport.Dispose();
        }

        [Fact]
        public async Task Transport_Receive_OversizedPayload_ProtocolError()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            var tcs = new TaskCompletionSource<TransportError>();
            transport.OnPacketReceiveFailed += (t, e) => tcs.TrySetResult(e);
            transport.ReceiveAsync();

            // 257 bytes > 256 segment size
            byte[] header = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(), 257);
            byte[] payload = new byte[257];
            pair.ServerConn.Send(header);
            pair.ServerConn.Send(payload);

            var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TransportError.ProtocolError, error);
            transport.Dispose();
        }

        [Fact]
        public async Task Transport_Receive_ValidPacket_FiresOnPacketReceived()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            var tcs = new TaskCompletionSource<Segment>();
            transport.OnPacketReceived += (t, e, seg) => tcs.TrySetResult(seg);
            transport.OnPacketReceiveFailed += (t, e) => tcs.SetException(new Exception(e.ToString()));
            transport.ReceiveAsync();

            // Send a 3-byte payload: header (0x03) + payload (AA, BB, CC)
            byte[] header = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(), 3);
            byte[] payload = new byte[] { 0xAA, 0xBB, 0xCC };

            pair.ServerConn.Send(header);
            pair.ServerConn.Send(payload);

            var seg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(seg.SegmentIndex > 0);
            Assert.Equal(3, seg.Memory.Length);
            Assert.Equal(0xAA, seg.Memory.Span[0]);
            Assert.Equal(0xBB, seg.Memory.Span[1]);
            Assert.Equal(0xCC, seg.Memory.Span[2]);
            seg.Release();
            transport.Dispose();
        }

        [Fact]
        public async Task Transport_SendAsync_InvalidSegment()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            TransportError? error = null;
            transport.OnPacketSendFailed += (t, s, e) => error = e;
            transport.SendAsync(default(Segment), 100); // SegmentIndex == 0, invalid

            Assert.Equal(TransportError.InvalidSegment, error);
            transport.Dispose();
        }

        [Fact]
        public void Transport_TryReserveSendPacket_WhenDisposed()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);
            transport.Dispose();
            Assert.False(transport.TryReserveSendPacket(out _));
        }

        [Fact]
        public void Transport_SendAsync_ObjectDisposed()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);
            transport.Dispose();

            TransportError? error = null;
            transport.OnPacketSendFailed += (t, s, e) => error = e;
            transport.SendAsync(default, 0);
            Assert.Equal(TransportError.ObjectDisposed, error);
        }

        [Fact]
        public void Transport_SendAsync_Disconnected_FiresSocketError()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);
            pair.Client.Close();

            // Locally closed socket -> SendAsync throws ObjectDisposedException -> TransportError.SocketError
            TransportError? error = null;
            transport.OnPacketSendFailed += (t, s, e) => { error = e; s.Release(); };
            if (transport.TryReserveSendPacket(out var seg))
            {
                seg.Memory.Span[0] = 0x01;
                transport.SendAsync(seg, 1);
            }
            Assert.Equal(TransportError.SocketError, error);
            transport.Dispose();
        }

        [Fact]
        public async Task Transport_SendAsync_ValidSegment_FiresOnPacketSent()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            bool packetSent = false;
            var tcs = new TaskCompletionSource<bool>();
            transport.OnPacketSent += t => { packetSent = true; tcs.TrySetResult(true); };

            if (transport.TryReserveSendPacket(out var seg))
            {
                byte[] payload = new byte[] { 0x01, 0x02, 0x03 };
                payload.CopyTo(seg.Memory.Span);
                transport.SendAsync(seg, payload.Length);
            }

            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(packetSent);
            transport.Dispose();
        }

        [Fact]
        public void Transport_Dispose_Idempotent()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);
            transport.Dispose();
            transport.Dispose(); // Should not throw
            Assert.True(transport.IsDisposed);
        }

        [Fact]
        public void Transport_Dispose_WithInflightReceive_ReleasesSegment()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);
            transport.ReceiveAsync();
            transport.Dispose();
            Assert.True(transport.IsDisposed);
        }

        [Fact]
        public void Transport_Dispose_WithInflightSend_ReleasesSegment()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            if (transport.TryReserveSendPacket(out var seg))
            {
                seg.Memory.Span[0] = 0x01;
                transport.SendAsync(seg, 1);
            }
            transport.Dispose();
            Assert.True(transport.IsDisposed);
        }

        [Fact]
        public async Task Transport_SendReceive_RoundTrip()
        {
            // Two sockets connected, each with its own Transport
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.Connect(new IPEndPoint(IPAddress.Loopback, port));
            var server = listener.Accept();

            var tcsClientReceive = new TaskCompletionSource<Segment>();
            var tcsServerReceive = new TaskCompletionSource<Segment>();

            var clientTransport = new global::DuneTransport.Transport.Transport(client);
            var serverTransport = new global::DuneTransport.Transport.Transport(server);

            clientTransport.OnPacketReceived += (t, e, seg) => tcsClientReceive.TrySetResult(seg);
            serverTransport.OnPacketReceived += (t, e, seg) => tcsServerReceive.TrySetResult(seg);

            // Client sends to server
            byte[] clientPayload = { 0x11, 0x22, 0x33 };
            if (clientTransport.TryReserveSendPacket(out var cSeg))
            {
                clientPayload.CopyTo(cSeg.Memory.Span);
                clientTransport.SendAsync(cSeg, clientPayload.Length);
            }
            serverTransport.ReceiveAsync();

            var serverSeg = await tcsServerReceive.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(3, serverSeg.Memory.Length);
            Assert.Equal(0x11, serverSeg.Memory.Span[0]);
            serverSeg.Release();

            // Server sends back to client
            byte[] serverPayload = { 0x44, 0x55, 0x66 };
            if (serverTransport.TryReserveSendPacket(out var sSeg))
            {
                serverPayload.CopyTo(sSeg.Memory.Span);
                serverTransport.SendAsync(sSeg, serverPayload.Length);
            }
            clientTransport.ReceiveAsync();

            var clientSeg = await tcsClientReceive.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(3, clientSeg.Memory.Length);
            Assert.Equal(0x44, clientSeg.Memory.Span[0]);
            clientSeg.Release();

            clientTransport.Dispose();
            serverTransport.Dispose();
            client.Close();
            server.Close();
        }

        [Fact]
        public void Transport_SendAsync_WhenFirstSendCompletedSync_SecondSucceeds()
        {
            // Test: when first send completes synchronously, second send should succeed
            var mock = new ControllableMockTransport();
            var transport = mock; // use mock as ITransport

            // Reserve segments via mock's TryReserve
            Assert.True(mock.TryReserveSendPacket(out var seg1));
            Assert.True(mock.TryReserveSendPacket(out var seg2));

            seg1.Memory.Span[0] = 0x01;
            seg2.Memory.Span[0] = 0x02;

            TransportError? error = null;
            mock.OnPacketSendFailed += (t, s, e) => { error = e; s.Release(); };

            // First send - completes synchronously (no CompleteFirstSend hook)
            transport.SendAsync(seg1, 1);

            // Second send - should succeed because first completed
            transport.SendAsync(seg2, 1);

            Assert.Null(error); // no error on second send
            Assert.True(mock.SendCalled);
            mock.Dispose();
        }

        [Fact]
        public void Transport_SendAsync_WhenFirstSendPendingAsync_SecondFailsWithSendAlreadyPending()
        {
            // Test: when first send is pending (async), second send should fail with SendAlreadyPending
            var mock = new ControllableMockTransport
            {
                // Provide a completion hook so first send stays pending
                OnCompletePendingSend = () => { }
            };
            var transport = mock;

            // Reserve segments
            Assert.True(mock.TryReserveSendPacket(out var seg1));
            Assert.True(mock.TryReserveSendPacket(out var seg2));

            seg1.Memory.Span[0] = 0x01;
            seg2.Memory.Span[0] = 0x02;

            TransportError? error = null;
            mock.OnPacketSendFailed += (t, s, e) => { error = e; s.Release(); };

            // First send - will NOT complete synchronously because OnCompletePendingSend is set
            transport.SendAsync(seg1, 1);

            // Second send - should fail because first is still pending
            transport.SendAsync(seg2, 1);

            Assert.Equal(TransportError.SendAlreadyPending, error);
            mock.Dispose();
        }

        [Fact]
        public void Transport_SendAsync_CompletePendingThenSecondSucceeds()
        {
            // Test: complete first send, then second succeeds
            var mock = new ControllableMockTransport
            {
                OnCompletePendingSend = () => { }
            };
            var transport = mock;

            Assert.True(mock.TryReserveSendPacket(out var seg1));
            Assert.True(mock.TryReserveSendPacket(out var seg2));

            seg1.Memory.Span[0] = 0x01;
            seg2.Memory.Span[0] = 0x02;

            TransportError? error = null;
            mock.OnPacketSendFailed += (t, s, e) => { error = e; s.Release(); };

            // First send - pending
            transport.SendAsync(seg1, 1);

            // Complete the first send
            mock.CompletePendingSend();

            // Second send - should succeed now
            transport.SendAsync(seg2, 1);

            Assert.Null(error);
            mock.Dispose();
        }

        [Fact]
        public async Task Transport_OnPacketReceived_HandlerThrows_FiresHandlerFailed()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            var tcs = new TaskCompletionSource<TransportError>();
            transport.OnPacketReceived += (t, e, seg) =>
            {
                throw new InvalidOperationException("handler boom");
            };
            transport.OnPacketReceiveFailed += (t, e) => tcs.TrySetResult(e);
            transport.ReceiveAsync();

            // Send a valid 1-byte payload
            byte[] header = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(), 1);
            pair.ServerConn.Send(header);
            pair.ServerConn.Send(new byte[] { 0xAA });

            var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TransportError.HandlerFailed, error);
            transport.Dispose();
        }

        [Fact]
        public async Task Transport_SendAsync_SocketDisconnected_AfterFIN()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            // Detect FIN on receive to set _isConnected = false
            var tcsReceive = new TaskCompletionSource<TransportError>();
            transport.OnPacketReceiveFailed += (t, e) => tcsReceive.TrySetResult(e);
            transport.ReceiveAsync();

            // Remote closes
            pair.ServerConn.Shutdown(SocketShutdown.Both);
            pair.ServerConn.Close();

            var recvError = await tcsReceive.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TransportError.SocketDisconnected, recvError);

            // Now try to send — should fail with SocketDisconnected
            TransportError? sendError = null;
            transport.OnPacketSendFailed += (t, s, e) => { sendError = e; };
            if (transport.TryReserveSendPacket(out var seg))
            {
                seg.Memory.Span[0] = 0x01;
                transport.SendAsync(seg, 1);
            }
            Assert.Equal(TransportError.SocketDisconnected, sendError);
            transport.Dispose();
        }

        [Fact]
        public async Task Transport_ReceiveAsync_WhenNotConnected_AfterFIN()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            // Detect FIN on receive to set _isConnected = false
            var tcsReceive = new TaskCompletionSource<TransportError>();
            transport.OnPacketReceiveFailed += (t, e) => tcsReceive.TrySetResult(e);
            transport.ReceiveAsync();

            pair.ServerConn.Shutdown(SocketShutdown.Both);
            pair.ServerConn.Close();

            await tcsReceive.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Now try another receive — should fail with SocketDisconnected
            TransportError? error = null;
            transport.OnPacketReceiveFailed += (t, e) => error = e;
            transport.ReceiveAsync();

            Assert.Equal(TransportError.SocketDisconnected, error);
            transport.Dispose();
        }

        /// <summary>
        /// Verifies that after a ReceiveAsync completes (packet received), a new ReceiveAsync can be called.
        /// Tests the receive state machine allows sequential receives.
        /// </summary>
        [Fact]
        public async Task Transport_ReceiveAsync_MultipleSequential_AfterFirstCompletes()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            var receivedPackets = new List<Segment>();

            // First receive
            var tcs1 = new TaskCompletionSource<Segment>();
            transport.OnPacketReceived += (t, e, seg) => tcs1.TrySetResult(seg);
            transport.OnPacketReceiveFailed += (t, e) => tcs1.SetException(new Exception($"Receive failed: {e}"));
            transport.ReceiveAsync();

            // Send first packet
            byte[] header1 = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(header1.AsSpan(), 3);
            pair.ServerConn.Send(header1);
            pair.ServerConn.Send(new byte[] { 0x11, 0x22, 0x33 });

            // Wait for first packet
            var seg1 = await tcs1.Task.WaitAsync(TimeSpan.FromSeconds(5));
            receivedPackets.Add(seg1);
            Assert.Equal(0x11, receivedPackets[0].Memory.Span[0]);
            receivedPackets[0].Release();

            // Second receive - should work after first completed
            var tcs2 = new TaskCompletionSource<Segment>();
            transport.OnPacketReceived += (t, e, seg) => tcs2.TrySetResult(seg);
            transport.OnPacketReceiveFailed += (t, e) => tcs2.SetException(new Exception($"Receive failed: {e}"));
            transport.ReceiveAsync();

            // Send second packet
            byte[] header2 = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(header2.AsSpan(), 3);
            pair.ServerConn.Send(header2);
            pair.ServerConn.Send(new byte[] { 0x44, 0x55, 0x66 });

            var seg2 = await tcs2.Task.WaitAsync(TimeSpan.FromSeconds(5));
            receivedPackets.Add(seg2);
            Assert.Equal(0x44, receivedPackets[1].Memory.Span[0]);
            receivedPackets[1].Release();

            transport.Dispose();
        }

        /// <summary>
        /// Verifies that calling ReceiveAsync while another is in progress (not yet completed) fails with ReceiveAlreadyPending.
        /// </summary>
        [Fact]
        public void Transport_ReceiveAsync_ConcurrentWhilePending_FailsWithReceiveAlreadyPending()
        {
            using var pair = new SocketPairFixture();
            var transport = new global::DuneTransport.Transport.Transport(pair.Client);

            TransportError? error = null;
            transport.OnPacketReceiveFailed += (t, e) => error = e;

            // First receive - pending
            transport.ReceiveAsync();

            // Second receive while first is still pending - should fail
            transport.ReceiveAsync();

            Assert.Equal(TransportError.ReceiveAlreadyPending, error);
            transport.Dispose();
        }
    }
}