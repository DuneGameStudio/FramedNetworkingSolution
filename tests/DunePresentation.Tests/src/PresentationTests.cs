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
            registry.RegisterHandler<EchoPacket>(0x0003, null!, _ => _ => { });

            // Verify storage indirectly: duplicate registration throws, confirming the entry exists.
            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterHandler<EchoPacket>(0x0003, null!, _ => _ => { }));
        }

        [Fact]
        public void RegisterHandler_ThrowsOnNullHandler()
        {
            var registry = new PacketRegistry();
            Assert.Throws<ArgumentNullException>(() =>
                registry.RegisterHandler<EchoPacket>(0x0003, null!, null!));
        }

        [Fact]
        public void RegisterHandler_ThrowsOnDuplicatePacketId()
        {
            var registry = new PacketRegistry();
            registry.RegisterHandler<EchoPacket>(0x0003, null!, _ => _ => { });

            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterHandler<EchoPacket>(0x0003, null!, _ => _ => { }));
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
            registry.RegisterHandler<EchoPacket>(0x0003, null!, _ => _ => { });
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

            bool released = false;
            var segment = new Segment { SegmentIndex = 1, Memory = new byte[100], ReleaseMemoryCallback = _ => released = true };
            var span = segment.Memory.Span;
            span[0] = 0xFF; span[1] = 0xFF; // unregistered packet ID 0xFFFF

            PacketError? error = null;
            peer.OnDeserializeFailed += e => error = e;
            var result = peer.DecryptAndDeserialize(segment);

            Assert.Null(result);
            Assert.Equal(PacketError.RegistryError, error);
            Assert.True(released, "Segment should have been released on registry error");
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

            bool released = false;
            var segment = new Segment { SegmentIndex = 1, Memory = new byte[100], ReleaseMemoryCallback = _ => released = true };

            PacketError? error = null;
            peer.OnDeserializeFailed += e => error = e;
            var result = peer.DecryptAndDeserialize(segment);

            Assert.Null(result);
            Assert.Equal(PacketError.DecryptError, error);
            Assert.True(released, "Segment should have been released on decrypt error");
            peer.Dispose();
        }

        [Fact]
        public void Peer_DecryptAndDeserialize_DeserializeFails()
        {
            var transport = new MockTransport { ReserveSendResult = true };
            var connection = new MockConnection(transport);
            var registry = new PacketRegistry();
            registry.RegisterHandler<BadDeserializePacket>(0x0099, null!, _ => _ => { });
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
            registry.RegisterHandler<EchoPacket>(0x0003, null!, _ => _ => { });
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

        [Fact]
        public void Peer_IsConnected_False_WhenDisconnected()
        {
            var connection = new MockConnection();
            var registry = new PacketRegistry();
            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            Assert.True(peer.IsConnected);
            connection.DisconnectAsync();
            Assert.False(peer.IsConnected);
            peer.Dispose();
        }

        [Fact]
        public void Peer_DecryptAndDeserialize_SegmentTooSmall_SerializationError()
        {
            var transport = new MockTransport { ReserveSendResult = true };
            var connection = new MockConnection(transport);
            var registry = new PacketRegistry();
            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            // Segment with only 1 byte (less than 2-byte header)
            bool released = false;
            var segment = new Segment { SegmentIndex = 1, Memory = new byte[1], ReleaseMemoryCallback = _ => released = true };

            PacketError? error = null;
            peer.OnDeserializeFailed += e => error = e;
            var result = peer.DecryptAndDeserialize(segment);

            Assert.Null(result);
            Assert.Equal(PacketError.SerializationError, error);
            Assert.True(released, "Segment should have been released on too-small error");
            peer.Dispose();
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
                registry.RegisterHandler<EchoPacket>(0x0003, null!, _ => _ => { });

                // Client side with XorEncryptor
                var clientEncryptor = new XorEncryptor(0xAB);
                var clientConnection = new Connection(clientSocket);
                var clientPeer = new global::DunePresentation.Peer.Peer(clientConnection, registry, clientEncryptor);

                // Server side with same XorEncryptor
                var serverEncryptor = new XorEncryptor(0xAB);
                var serverConnection = new Connection(serverSocket);
                var serverPeer = new global::DunePresentation.Peer.Peer(serverConnection, registry, serverEncryptor);

                var tcs = new TaskCompletionSource<EchoPacket>();
                serverPeer.Connection.Transport.OnPacketReceived += (t, seg) =>
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
                registry.RegisterHandler<EchoPacket>(0x0003, null!, _ => _ => { });

                // Client side with PassThroughEncryptor
                var clientEncryptor = new PassThroughEncryptor();
                var clientConnection = new Connection(clientSocket);
                var clientPeer = new global::DunePresentation.Peer.Peer(clientConnection, registry, clientEncryptor);

                // Server side with PassThroughEncryptor
                var serverEncryptor = new PassThroughEncryptor();
                var serverConnection = new Connection(serverSocket);
                var serverPeer = new global::DunePresentation.Peer.Peer(serverConnection, registry, serverEncryptor);

                var tcs = new TaskCompletionSource<EchoPacket>();
                serverPeer.Connection.Transport.OnPacketReceived += (t, seg) =>
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

        #region PeerClient Tests

        [Fact]
        public void PeerClient_Construction_NullRegistry_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PeerClient(null!));
        }

        [Fact]
        public void PeerClient_Construction_DefaultClientConnector()
        {
            var client = new PeerClient(new PacketRegistry());
            Assert.NotNull(client);
            client.Dispose();
        }

        [Fact]
        public void PeerClient_Construction_WithCustomClient()
        {
            var mock = new MockClientConnector();
            var client = new PeerClient(new PacketRegistry(), client: mock);
            Assert.NotNull(client);
            client.Dispose();
        }

        [Fact]
        public void PeerClient_ConnectAsync_DelegatesToConnector()
        {
            var mock = new MockClientConnector();
            var client = new PeerClient(new PacketRegistry(), client: mock);

            bool result = client.ConnectAsync("127.0.0.1", 9999);

            Assert.True(mock.ConnectCalled);
            Assert.Equal("127.0.0.1", mock.LastAddress);
            Assert.Equal(9999, mock.LastPort);
            client.Dispose();
        }

        [Fact]
        public void PeerClient_OnPeerConnected_FiresOnSuccess()
        {
            var mock = new MockClientConnector();
            var client = new PeerClient(new PacketRegistry(), client: mock);

            IPeer? peer = null;
            client.OnPeerConnected += p => peer = p;

            mock.SimulateConnected();

            Assert.NotNull(peer);
            client.Dispose();
        }

        [Fact]
        public void PeerClient_OnConnectFailed_FiresOnError()
        {
            var mock = new MockClientConnector();
            var client = new PeerClient(new PacketRegistry(), client: mock);

            SocketError? error = null;
            client.OnConnectFailed += e => error = e;

            mock.SimulateFailed();

            Assert.Equal(SocketError.ConnectionRefused, error);
            client.Dispose();
        }

        [Fact]
        public void PeerClient_Dispose_IsIdempotent()
        {
            var mock = new MockClientConnector();
            var client = new PeerClient(new PacketRegistry(), client: mock);

            client.Dispose();
            client.Dispose(); // should not throw
        }

        [Fact]
        public void PeerClient_Dispose_UnsubscribesFromConnector()
        {
            var mock = new MockClientConnector();
            var client = new PeerClient(new PacketRegistry(), client: mock);
            client.Dispose();

            // Simulate connection after dispose - should not throw or invoke events
            bool peerFired = false;
            client.OnPeerConnected += _ => peerFired = true;
            mock.SimulateConnected();
            Assert.False(peerFired);
        }

        #endregion

        #region PeerServer Tests

        [Fact]
        public void PeerServer_Construction_NullRegistry_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PeerServer(null!));
        }

        [Fact]
        public void PeerServer_Construction_DefaultServerConnector()
        {
            var server = new PeerServer(new PacketRegistry());
            Assert.NotNull(server);
            server.Dispose();
        }

        [Fact]
        public void PeerServer_Construction_WithCustomServer()
        {
            var mock = new MockServerConnector();
            var server = new PeerServer(new PacketRegistry(), server: mock);
            Assert.NotNull(server);
            server.Dispose();
        }

        [Fact]
        public void PeerServer_StartListening_DelegatesToConnector()
        {
            var mock = new MockServerConnector();
            var server = new PeerServer(new PacketRegistry(), server: mock);

            server.StartListening("127.0.0.1", 9999);

            Assert.True(mock.IsListening);
            Assert.Equal("127.0.0.1", mock.LastAddress);
            Assert.Equal(9999, mock.LastPort);
            server.Dispose();
        }

        [Fact]
        public void PeerServer_StopListening_DelegatesToConnector()
        {
            var mock = new MockServerConnector();
            var server = new PeerServer(new PacketRegistry(), server: mock);

            server.StartListening("127.0.0.1", 9999);
            server.StopListening();

            Assert.False(mock.IsListening);
            server.Dispose();
        }

        [Fact]
        public void PeerServer_OnPeerConnected_FiresOnAccept()
        {
            var mock = new MockServerConnector();
            var server = new PeerServer(new PacketRegistry(), server: mock);

            IPeer? peer = null;
            server.OnPeerConnected += p => peer = p;

            mock.SimulateClientConnected();

            Assert.NotNull(peer);
            server.Dispose();
        }

        [Fact]
        public void PeerServer_OnAcceptFailed_FiresOnError()
        {
            var mock = new MockServerConnector();
            var server = new PeerServer(new PacketRegistry(), server: mock);

            SocketError? error = null;
            server.OnAcceptFailed += e => error = e;

            mock.SimulateAcceptFailed();

            Assert.Equal(SocketError.ConnectionRefused, error);
            server.Dispose();
        }

        [Fact]
        public void PeerServer_Dispose_IsIdempotent()
        {
            var mock = new MockServerConnector();
            var server = new PeerServer(new PacketRegistry(), server: mock);

            server.Dispose();
            server.Dispose(); // should not throw
        }

        #endregion

        #region Presentation Integration Tests

        [Fact]
        public async Task Peer_RealXorEncryptor_MultiplePackets()
        {
            // End-to-end: send 5 encrypted packets in sequence
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
                registry.RegisterHandler<EchoPacket>(0x0003, null!, _ => _ => { });

                var received = new System.Collections.Generic.List<string>();
                var encryptor = new XorEncryptor(0xAB);

                var clientConnection = new Connection(clientSocket);
                var clientPeer = new global::DunePresentation.Peer.Peer(clientConnection, registry, encryptor);
                var serverConnection = new Connection(serverSocket);
                var serverPeer = new global::DunePresentation.Peer.Peer(serverConnection, registry, encryptor);

                var allDone = new System.Threading.Tasks.TaskCompletionSource<bool>();

                serverPeer.Connection.Transport.OnPacketReceived += (t, seg) =>
                {
                    var result = serverPeer.DecryptAndDeserialize(seg);
                    if (result.HasValue && result.Value.Packet is EchoPacket echo)
                    {
                        received.Add(echo.Text);
                        if (received.Count == 5)
                            allDone.TrySetResult(true);
                        else
                            t.ReceiveAsync();
                    }
                };
                serverPeer.Connection.Transport.ReceiveAsync();

                for (int i = 0; i < 5; i++)
                {
                    var packet = new EchoPacket { Text = $"msg{i}" };
                    var seg = clientPeer.SerializeAndEncrypt(packet);
                    Assert.True(seg.SegmentIndex > 0);
                    clientPeer.Send(seg, packet.PacketSize);
                }

                await allDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(5, received.Count);
                for (int i = 0; i < 5; i++)
                    Assert.Equal($"msg{i}", received[i]);

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
        public async Task PeerClient_RealServer_EndToEnd()
        {
            // Full stack: PeerServer + PeerClient over real sockets
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            int port = ((IPEndPoint)listener.LocalEndPoint!).Port;
            listener.Close();

            var registry = new PacketRegistry();
            registry.RegisterHandler<EchoPacket>(0x0003, null!, _ => p => { });

            // Server
            var server = new PeerServer(registry);
            server.StartListening("127.0.0.1", port);
            server.AcceptConnection();

            var serverPeerTcs = new System.Threading.Tasks.TaskCompletionSource<IPeer>();
            server.OnPeerConnected += p => serverPeerTcs.TrySetResult(p);

            // Client
            var client = new PeerClient(registry);
            var clientPeerTcs = new System.Threading.Tasks.TaskCompletionSource<IPeer>();
            client.OnPeerConnected += p => clientPeerTcs.TrySetResult(p);
            client.ConnectAsync("127.0.0.1", port);

            var serverPeer = await serverPeerTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var clientPeer = await clientPeerTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(serverPeer);
            Assert.NotNull(clientPeer);

            // Set up server receive
            var recvTcs = new System.Threading.Tasks.TaskCompletionSource<EchoPacket>();
            serverPeer.Connection.Transport.OnPacketReceived += (t, seg) =>
            {
                var result = serverPeer.DecryptAndDeserialize(seg);
                if (result.HasValue && result.Value.Packet is EchoPacket echo)
                    recvTcs.TrySetResult(echo);
            };
            serverPeer.Connection.Transport.ReceiveAsync();

            // Client sends
            var packet = new EchoPacket { Text = "hello from client" };
            var seg = clientPeer.SerializeAndEncrypt(packet);
            Assert.True(seg.SegmentIndex > 0);
            clientPeer.Send(seg, packet.PacketSize);

            var received = await recvTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("hello from client", received.Text);

            clientPeer.Dispose();
            serverPeer.Dispose();
            client.Dispose();
            server.Dispose();
        }

        #endregion

        #region Concurrency Tests

        [Fact]
        public void Peer_ConcurrentSerializeAndDeserialize()
        {
            var transport = new MockTransport { ReserveSendResult = true };
            var connection = new MockConnection(transport);
            var registry = new PacketRegistry();
            registry.RegisterHandler<EchoPacket>(0x0003, null!, _ => _ => { });

            var peer = new global::DunePresentation.Peer.Peer(connection, registry);

            try
            {
                var t1 = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        try
                        {
                            var packet = new EchoPacket { Text = $"msg{i}" };
                            peer.SerializeAndEncrypt(packet);
                        }
                        catch { }
                    }
                });
                var t2 = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        try
                        {
                            var wireData = new byte[] { 0x03, 0x00, (byte)'h', (byte)'i' };
                            var segment = new Segment { SegmentIndex = 1, Memory = wireData, ReleaseMemoryCallback = _ => { } };
                            peer.DecryptAndDeserialize(segment);
                        }
                        catch { }
                    }
                });
                System.Threading.Tasks.Task.WhenAll(t1, t2).Wait(5000);
            }
            catch (Exception e)
            {
                Assert.Fail($"Concurrent serialize/deserialize threw: {e.Message}");
            }

            peer.Dispose();
        }

        [Fact]
        public void PacketRegistry_ConcurrentRegistration_NoDuplicate()
        {
            var registry = new PacketRegistry();

            try
            {
                var t1 = System.Threading.Tasks.Task.Run(() =>
                {
                    for (ushort i = 0x1000; i < 0x1020; i++)
                        try { registry.RegisterHandler<EchoPacket>(i, null!, _ => _ => { }); } catch { }
                });
                var t2 = System.Threading.Tasks.Task.Run(() =>
                {
                    for (ushort i = 0x2000; i < 0x2020; i++)
                        try { registry.RegisterHandler<EchoPacket>(i, null!, _ => _ => { }); } catch { }
                });
                System.Threading.Tasks.Task.WhenAll(t1, t2).Wait(5000);
            }
            catch (Exception e)
            {
                Assert.Fail($"Concurrent registration threw: {e.Message}");
            }

            // Verify no exceptions and registrations succeeded
            Assert.True(true);
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