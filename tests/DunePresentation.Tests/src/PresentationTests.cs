using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DunePresentation.Encryption.Interface;
using DunePresentation.Packet;
using DunePresentation.Packet.Interfaces;
using DunePresentation.Peer;
using DunePresentation.Peer.Interfaces;
using DuneSession.SocketConnectors;
using DuneSession.SocketConnectors.Interface;
using DuneTransport.BufferManager;
using DuneTransport.Transport;
using DuneTransport.Transport.Interface;
using DemoPackets;
using Xunit;

namespace DunePresentation.Tests
{
    public class PresentationTests
    {
        #region PacketRegistry Tests

        [Fact]
        public void RegisterHandler_StoresHandlerCorrectly()
        {
            var registry = new PacketRegistry();
            registry.RegisterHandler<EchoPacket>(0x0003, _ => { });

            // Verify storage indirectly: duplicate registration throws, confirming the entry exists.
            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterHandler<EchoPacket>(0x0003, _ => { }));
        }

        [Fact]
        public void RegisterHandler_ThrowsOnNullHandler()
        {
            var registry = new PacketRegistry();
            Assert.Throws<ArgumentNullException>(() =>
                registry.RegisterHandler<EchoPacket>(0x0003, null!));
        }

        [Fact]
        public void RegisterHandler_ThrowsOnDuplicatePacketId()
        {
            var registry = new PacketRegistry();
            registry.RegisterHandler<EchoPacket>(0x0003, _ => { });

            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterHandler<EchoPacket>(0x0003, _ => { }));
        }

        #endregion

        #region Peer Tests

        [Fact]
        public void Peer_Construction_ThrowsOnNullConnection()
        {
            var registry = new PacketRegistry();
            Assert.Throws<ArgumentNullException>(() =>
                new global::DunePresentation.Peer.Peer(null!, registry));
        }

        [Fact]
        public void Peer_Construction_ThrowsOnNullRegistry()
        {
            var connection = new MockConnection();
            Assert.Throws<ArgumentNullException>(() =>
                new global::DunePresentation.Peer.Peer(connection, null!));
        }

        [Fact]
        public void Peer_Construction_AcceptsNullEncryptor()
        {
            var connection = new MockConnection();
            var registry = new PacketRegistry();
            var peer = new global::DunePresentation.Peer.Peer(connection, registry, encryptor: null);
            Assert.NotNull(peer);
            peer.Dispose();
        }

        [Fact]
        public void SerializeAndEncrypt_ReturnsSegment_WhenSuccessful()
        {
            var transport = new MockTransport { ReserveSendResult = true };
            var connection = new MockConnection(transport);
            var registry = new PacketRegistry();
            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            var packet = new EchoPacket { Text = "hello" };
            var segment = peer.SerializeAndEncrypt(packet);

            Assert.True(segment.SegmentIndex > 0);
            peer.Dispose();
        }

        [Fact]
        public void SerializeAndEncrypt_ReturnsDefault_WhenPoolExhausted()
        {
            var transport = new MockTransport { ReserveSendResult = false };
            var connection = new MockConnection(transport);
            var registry = new PacketRegistry();
            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            PacketError? error = null;
            peer.OnSerializeFailed += e => error = e;

            var packet = new EchoPacket { Text = "hello" };
            var segment = peer.SerializeAndEncrypt(packet);

            Assert.Equal(0, segment.SegmentIndex);
            Assert.Equal(PacketError.PoolExhausted, error);
            peer.Dispose();
        }

        [Fact]
        public void Send_DelegatesToTransport()
        {
            var transport = new MockTransport();
            var connection = new MockConnection(transport);
            var registry = new PacketRegistry();
            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            var segment = new Segment { SegmentIndex = 1, Memory = new byte[64] };
            peer.Send(segment, 64);

            Assert.True(transport.SendCalled);
            Assert.Equal(1, transport.LastSentSegment!.Value.SegmentIndex);
            Assert.Equal(64, transport.LastSentSize);
            peer.Dispose();
        }

        [Fact]
        public void DisconnectAsync_DelegatesToConnection()
        {
            var connection = new MockConnection();
            var registry = new PacketRegistry();
            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            peer.DisconnectAsync();

            Assert.True(connection.DisconnectCalled);
            peer.Dispose();
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var connection = new MockConnection();
            var registry = new PacketRegistry();
            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            peer.Dispose();
            peer.Dispose(); // should not throw
        }

        #endregion

        
        #region DecryptAndDeserialize Tests

        [Fact]
        public void Peer_DecryptAndDeserialize_Success()
        {
            var transport = new MockTransport { ReserveSendResult = true };
            var connection = new MockConnection(transport);
            var registry = new PacketRegistry();
            registry.RegisterHandler<EchoPacket>(0x0003, _ => { });
            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            // EchoPacket wire format: 2 bytes packet ID (LE), then raw UTF-8 text bytes
            // DecryptAndDeserialize sets packet.PacketSize = span.Length
            // So OnDeserialize reads span.Length - PresentationHeader.Size as the payload
            var wireData = new byte[] { 0x03, 0x00, (byte)'h', (byte)'i' }; // packet ID + "hi"
            var segment = new Segment { SegmentIndex = 1, Memory = wireData, ReleaseMemoryCallback = _ => { } };

            var result = peer.DecryptAndDeserialize(segment);
            Assert.NotNull(result);
            Assert.IsType<EchoPacket>(result!.Value.Packet);
            Assert.Equal("hi", ((EchoPacket)result.Value.Packet).Text);
            peer.Dispose();
        }

        [Fact]
        public void Peer_DecryptAndDeserialize_RegistryError()
        {
            var transport = new MockTransport { ReserveSendResult = true };
            var connection = new MockConnection(transport);
            var registry = new PacketRegistry();
            // No handlers registered
            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            var segment = new Segment { SegmentIndex = 1, Memory = new byte[100], ReleaseMemoryCallback = _ => { } };
            var span = segment.Memory.Span;
            span[0] = 0xFF; span[1] = 0xFF; // unregistered packet ID 0xFFFF

            PacketError? error = null;
            peer.OnDeserializeFailed += e => error = e;
            var result = peer.DecryptAndDeserialize(segment);

            Assert.Null(result);
            Assert.Equal(PacketError.RegistryError, error);
            peer.Dispose();
        }

        [Fact]
        public void Peer_DecryptAndDeserialize_DecryptThrows()
        {
            var transport = new MockTransport { ReserveSendResult = true };
            var connection = new MockConnection(transport);
            var registry = new PacketRegistry();
            var encryptor = new MockEncryptor { SimulateDecryptThrow = true };
            var peer = new global::DunePresentation.Peer.Peer(connection, registry, encryptor);

            var segment = new Segment { SegmentIndex = 1, Memory = new byte[100], ReleaseMemoryCallback = _ => { } };

            PacketError? error = null;
            peer.OnDeserializeFailed += e => error = e;
            var result = peer.DecryptAndDeserialize(segment);

            Assert.Null(result);
            Assert.Equal(PacketError.DecryptError, error);
            peer.Dispose();
        }

        [Fact]
        public void Peer_DecryptAndDeserialize_DeserializeFails()
        {
            var transport = new MockTransport { ReserveSendResult = true };
            var connection = new MockConnection(transport);
            var registry = new PacketRegistry();
            registry.RegisterHandler<BadDeserializePacket>(0x0099, _ => { });
            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            var segment = new Segment { SegmentIndex = 1, Memory = new byte[100], ReleaseMemoryCallback = _ => { } };
            var span = segment.Memory.Span;
            span[0] = 0x99; span[1] = 0x00; // packet ID 0x0099

            PacketError? error = null;
            peer.OnDeserializeFailed += e => error = e;
            var result = peer.DecryptAndDeserialize(segment);

            Assert.Null(result);
            Assert.Equal(PacketError.DeserializeError, error);
            peer.Dispose();
        }

        [Fact]
        public void Peer_EncryptorIntegration_SerializeAndDecrypt()
        {
            var transport = new MockTransport { ReserveSendResult = true };
            var connection = new MockConnection(transport);
            var registry = new PacketRegistry();
            registry.RegisterHandler<EchoPacket>(0x0003, _ => { });
            var encryptor = new XorEncryptor(0xAB);
            var peer = new global::DunePresentation.Peer.Peer(connection, registry, encryptor);

            var packet = new EchoPacket { Text = "hello" };
            var segment = peer.SerializeAndEncrypt(packet);

            Assert.True(segment.SegmentIndex > 0);
            Assert.Equal(7, packet.PacketSize); // 2 byte header + 5 bytes "hello"

            // Slice the segment memory to the actual packet size so DecryptAndDeserialize
            // reads only the relevant bytes (span.Length becomes PacketSize after decrypt).
            segment.Memory = segment.Memory.Slice(0, packet.PacketSize);

            var result = peer.DecryptAndDeserialize(segment);
            Assert.NotNull(result);
            Assert.Equal("hello", ((EchoPacket)result!.Value.Packet).Text);
            peer.Dispose();
        }

        [Fact]
        public void Peer_Dispose_CleansUpConnection()
        {
            var connection = new MockConnection();
            var registry = new PacketRegistry();
            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            peer.Dispose();
            Assert.True(connection.Disposed);
        }

        #region Integration Tests with Real Encryptors

        [Fact]
        public async Task Peer_RealXorEncryptor_EndToEndEncryptDecrypt()
        {
            // Integration test: real Peer + real XorEncryptor + real Transport (via socket pair)
            // Tests the full encryption/decryption pipeline end-to-end

            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.Connect(new IPEndPoint(IPAddress.Loopback, port));
            var serverSocket = listener.Accept();
            listener.Close();

            try
            {
                var registry = new PacketRegistry();
                registry.RegisterHandler<EchoPacket>(0x0003, _ => { });

                // Client side with XorEncryptor
                var clientEncryptor = new XorEncryptor(0xAB);
                var clientConnection = new Connection(clientSocket);
                var clientPeer = new global::DunePresentation.Peer.Peer(clientConnection, registry, clientEncryptor);

                // Server side with same XorEncryptor
                var serverEncryptor = new XorEncryptor(0xAB);
                var serverConnection = new Connection(serverSocket);
                var serverPeer = new global::DunePresentation.Peer.Peer(serverConnection, registry, serverEncryptor);

                var tcs = new TaskCompletionSource<EchoPacket>();
                serverPeer.Connection.Transport.OnPacketReceived += (t, args, seg) =>
                {
                    var result = serverPeer.DecryptAndDeserialize(seg);
                    if (result.HasValue && result.Value.Packet is EchoPacket echo)
                        tcs.TrySetResult(echo);
                    if (serverPeer.IsConnected)
                        serverPeer.Connection.Transport.ReceiveAsync();
                };
                serverPeer.Connection.Transport.ReceiveAsync();

                // Client sends encrypted packet
                var sendPacket = new EchoPacket { Text = "encrypted hello" };
                var sendSegment = clientPeer.SerializeAndEncrypt(sendPacket);
                Assert.True(sendSegment.SegmentIndex > 0);
                clientPeer.Send(sendSegment, sendPacket.PacketSize);

                var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal("encrypted hello", received.Text);

                clientPeer.Dispose();
                serverPeer.Dispose();
            }
            finally
            {
                clientSocket.Close();
                serverSocket.Close();
            }
        }

        [Fact]
        public async Task Peer_RealPassThroughEncryptor_EndToEndEncryptDecrypt()
        {
            // Integration test: real Peer + real PassThroughEncryptor + real Transport

            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.Connect(new IPEndPoint(IPAddress.Loopback, port));
            var serverSocket = listener.Accept();
            listener.Close();

            try
            {
                var registry = new PacketRegistry();
                registry.RegisterHandler<EchoPacket>(0x0003, _ => { });

                // Client side with PassThroughEncryptor
                var clientEncryptor = new PassThroughEncryptor();
                var clientConnection = new Connection(clientSocket);
                var clientPeer = new global::DunePresentation.Peer.Peer(clientConnection, registry, clientEncryptor);

                // Server side with PassThroughEncryptor
                var serverEncryptor = new PassThroughEncryptor();
                var serverConnection = new Connection(serverSocket);
                var serverPeer = new global::DunePresentation.Peer.Peer(serverConnection, registry, serverEncryptor);

                var tcs = new TaskCompletionSource<EchoPacket>();
                serverPeer.Connection.Transport.OnPacketReceived += (t, args, seg) =>
                {
                    var result = serverPeer.DecryptAndDeserialize(seg);
                    if (result.HasValue && result.Value.Packet is EchoPacket echo)
                        tcs.TrySetResult(echo);
                    if (serverPeer.IsConnected)
                        serverPeer.Connection.Transport.ReceiveAsync();
                };
                serverPeer.Connection.Transport.ReceiveAsync();

                // Client sends packet (no actual encryption)
                var sendPacket = new EchoPacket { Text = "plain text" };
                var sendSegment = clientPeer.SerializeAndEncrypt(sendPacket);
                Assert.True(sendSegment.SegmentIndex > 0);
                clientPeer.Send(sendSegment, sendPacket.PacketSize);

                var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal("plain text", received.Text);

                clientPeer.Dispose();
                serverPeer.Dispose();
            }
            finally
            {
                clientSocket.Close();
                serverSocket.Close();
            }
        }

        #endregion

        /// <summary>Packet whose ReadFieldsFromBuffer always returns false.</summary>
        private class BadDeserializePacket : IPacket
        {
            public ushort PacketId => 0x0099;
            public int PacketSize { get; set; }
            public Segment segment { get; set; }

            public void WriteFieldsToBuffer(Span<byte> buffer, out int bytesWritten)
            {
                bytesWritten = 0;
            }

            public bool ReadFieldsFromBuffer(ReadOnlySpan<byte> buffer, int length)
            {
                return false; // Always fails
            }
        }

        #endregion
    }
}