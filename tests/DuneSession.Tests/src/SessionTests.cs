using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DuneSession.SocketConnectors;
using DuneSession.SocketConnectors.Interface;
using DuneTransport.BufferManager;
using DuneTransport.Transport;
using DuneTransport.Transport.Interface;
using Xunit;

namespace DuneSession.Tests
{
    public class SessionTests
    {
        // --- Connection Tests ---
        [Fact]
        public void Connection_Construction_NullSocket_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new Connection(null!));
        }

        [Fact]
        public void Connection_IsConnected_TrueAfterConstruction()
        {
            using var pair = MakeSocketPair();
            var conn = new Connection(pair.Client);
            Assert.True(conn.IsConnected);
            conn.Dispose();
        }

        [Fact]
        public void Connection_Transport_NotNull()
        {
            using var pair = MakeSocketPair();
            var conn = new Connection(pair.Client);
            Assert.NotNull(conn.Transport);
            conn.Dispose();
        }

        [Fact]
        public void Connection_DisconnectAsync_Idempotent()
        {
            using var pair = MakeSocketPair();
            var conn = new Connection(pair.Client);

            conn.DisconnectAsync();
            conn.DisconnectAsync(); // should not throw

            Assert.False(conn.IsConnected);
            conn.Dispose();
        }

        [Fact]
        public void Connection_DisconnectAsync_WhenDisposed_NoOp()
        {
            using var pair = MakeSocketPair();
            var conn = new Connection(pair.Client);
            conn.Dispose();

            conn.DisconnectAsync(); // should not throw
        }

        [Fact]
        public void Connection_Dispose_Idempotent()
        {
            using var pair = MakeSocketPair();
            var conn = new Connection(pair.Client);
            conn.Dispose();
            conn.Dispose();
        }

        [Fact]
        public async Task Connection_OnDisconnected_FiresOnRemoteClose()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.Connect(new IPEndPoint(IPAddress.Loopback, port));
            var server = listener.Accept();
            listener.Close();

            var conn = new Connection(client);
            var tcs = new TaskCompletionSource<bool>();
            conn.OnDisconnected += () => tcs.TrySetResult(true);

            // Server closes -> client receives FIN -> transport fires SocketDisconnected
            server.Shutdown(SocketShutdown.Both);
            server.Close();

            // Trigger a receive to detect the disconnect
            conn.Transport.ReceiveAsync();

            bool disconnected = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(disconnected);
            conn.Dispose();
            client.Close();
        }

        // --- ClientConnector Tests ---
        [Fact]
        public void ClientConnector_Dispose_Idempotent()
        {
            var connector = new ClientConnector();
            connector.Dispose();
            connector.Dispose();
        }

        [Fact]
        public void ClientConnector_ConnectAsync_InvalidAddress_FiresFailure()
        {
            var connector = new ClientConnector();
            SocketError? error = null;
            connector.OnConnectFailed += e => error = e;

            bool result = connector.ConnectAsync("not-valid-address", 1234);

            Assert.False(result);
            Assert.NotNull(error);
            connector.Dispose();
        }

        [Fact]
        public async Task ClientConnector_ConnectAsync_NonExistentServer_FiresFailure()
        {
            var connector = new ClientConnector();
            var tcs = new TaskCompletionSource<SocketError>();
            connector.OnConnectFailed += e => tcs.TrySetResult(e);

            connector.ConnectAsync("127.0.0.1", 59877);

            var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEqual(SocketError.Success, error);
            connector.Dispose();
        }

        [Fact]
        public void ClientConnector_ConnectAsync_WhileConnecting_ReturnsFalse()
        {
            var connector = new ClientConnector();
            // Second call while first is still connecting should return false:
            connector.ConnectAsync("127.0.0.1", 59878);
            bool second = connector.ConnectAsync("127.0.0.1", 59878);
            Assert.False(second);
            connector.Dispose();
        }

        /// <summary>
        /// Verifies that <c>ConnectAsync</c> successfully connects to a listening server
        /// and fires <c>OnConnected</c> with a valid <c>IConnection</c>.
        /// </summary>
        [Fact]
        public async Task ClientConnector_ConnectAsync_ValidServer_ConnectsSuccessfully()
        {
            // Start a real listener
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            var connector = new ClientConnector();
            var tcs = new TaskCompletionSource<IConnection>();
            connector.OnConnected += conn => tcs.TrySetResult(conn);

            // Attempt connection
            bool started = connector.ConnectAsync("127.0.0.1", port);
            Assert.True(started);

            // Accept the connection on server side
            var serverSocket = await Task.Run(() => listener.Accept());

            // Wait for OnConnected
            var connection = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(connection);
            Assert.True(connection.IsConnected);
            Assert.NotNull(connection.Transport);
            Assert.True(connector.IsConnected);

            // Cleanup
            connection.Dispose();
            serverSocket.Close();
            listener.Close();
            connector.Dispose();
        }

        // --- ServerConnector Tests ---

        [Fact]
        public void ServerConnector_Construction_ZeroBacklog_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ServerConnector(0));
        }

        [Fact]
        public void ServerConnector_Dispose_Idempotent()
        {
            var connector = new ServerConnector();
            connector.Dispose();
            connector.Dispose();
        }

        [Fact]
        public void ServerConnector_StopListening_Idempotent()
        {
            var connector = new ServerConnector();
            connector.StartListening("127.0.0.1", 0);
            connector.StopListening();
            connector.StopListening();
            connector.Dispose();
        }

        [Fact]
        public void ServerConnector_AcceptConnection_WhenNotListening_NoOp()
        {
            var connector = new ServerConnector();
            connector.AcceptConnection(); // should not throw when not listening
            connector.Dispose();
        }

        [Fact]
        public void ServerConnector_StopListening_ClosesSocket()
        {
            var connector = new ServerConnector();
            connector.StartListening("127.0.0.1", 0);
            connector.StopListening();
            Assert.False(connector.IsListening);
            connector.Dispose();
        }

        [Fact]
        public void ServerConnector_StartListening_AlreadyListening_NoOp()
        {
            var connector = new ServerConnector();
            connector.StartListening("127.0.0.1", 0);
            connector.StartListening("127.0.0.1", 0); // should not throw
            connector.Dispose();
        }

        // --- ServerConnector Integration Tests ---

        [Fact]
        public async Task ServerConnector_AcceptConnection_RealClient_FiresOnConnected()
        {
            // Start a probe listener to get a free port
            var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            probe.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            probe.Listen(1);
            int port = ((IPEndPoint)probe.LocalEndPoint!).Port;
            probe.Close();

            var connector = new ServerConnector();
            connector.StartListening("127.0.0.1", port);

            var tcs = new TaskCompletionSource<IConnection>();
            connector.OnClientConnected += conn => tcs.TrySetResult(conn);
            connector.AcceptConnection();

            // Connect a real client
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.Connect(new IPEndPoint(IPAddress.Loopback, port));

            var connection = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(connection);
            Assert.True(connection.IsConnected);

            connection.Dispose();
            client.Close();
            connector.Dispose();
        }

        [Fact]
        public async Task ServerConnector_AcceptConnection_MultipleClients_Sequential()
        {
            var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            probe.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            probe.Listen(2);
            int port = ((IPEndPoint)probe.LocalEndPoint!).Port;
            probe.Close();

            var connector = new ServerConnector();
            connector.StartListening("127.0.0.1", port);

            var accepted = new System.Collections.Generic.List<IConnection>();
            connector.OnClientConnected += conn => accepted.Add(conn);

            // First client
            var client1 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client1.Connect(new IPEndPoint(IPAddress.Loopback, port));
            connector.AcceptConnection();
            await Task.Delay(100);

            // Second client
            var client2 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client2.Connect(new IPEndPoint(IPAddress.Loopback, port));
            connector.AcceptConnection();
            await Task.Delay(100);

            Assert.Equal(2, accepted.Count);
            foreach (var conn in accepted)
                conn.Dispose();
            client1.Close();
            client2.Close();
            connector.Dispose();
        }

        [Fact]
        public async Task ServerConnector_AcceptConnection_WithData_ReceiveAndSend()
        {
            var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            probe.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            probe.Listen(1);
            int port = ((IPEndPoint)probe.LocalEndPoint!).Port;
            probe.Close();

            var connector = new ServerConnector();
            connector.StartListening("127.0.0.1", port);

            var tcs = new TaskCompletionSource<IConnection>();
            connector.OnClientConnected += conn => tcs.TrySetResult(conn);
            connector.AcceptConnection();

            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.Connect(new IPEndPoint(IPAddress.Loopback, port));

            var connection = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Set up server receive BEFORE sending data
            var recvTcs = new TaskCompletionSource<Segment>();
            connection.Transport.OnPacketReceived += (t, seg) => recvTcs.TrySetResult(seg);
            connection.Transport.ReceiveAsync();

            // Client sends data to server (with 2-byte length header)
            byte[] header = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(), 3);
            byte[] payload = new byte[] { 0xAA, 0xBB, 0xCC };
            client.Send(header);
            client.Send(payload);

            var segment = await recvTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(3, segment.Memory.Length);
            Assert.Equal(0xAA, segment.Memory.Span[0]);
            segment.Release();

            connection.Dispose();
            client.Close();
            connector.Dispose();
        }

        [Fact]
        public void ServerConnector_Dispose_WhileListening_CleansUp()
        {
            var connector = new ServerConnector();
            connector.StartListening("127.0.0.1", 0);
            Assert.True(connector.IsListening);

            connector.Dispose();

            Assert.False(connector.IsListening);
        }

        // --- ClientConnector Integration Tests ---

        [Fact]
        public async Task ClientConnector_Dispose_WithActiveConnection_CleansUp()
        {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            var connector = new ClientConnector();
            var tcs = new TaskCompletionSource<IConnection>();
            connector.OnConnected += conn => tcs.TrySetResult(conn);
            connector.ConnectAsync("127.0.0.1", port);

            var serverSocket = await Task.Run(() => listener.Accept());
            var connection = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(connection);

            // Dispose connector while connection is active
            connector.Dispose();

            Assert.False(connector.IsConnected);
            serverSocket.Close();
            listener.Close();
        }

        // --- Additional Connection Tests ---

        [Fact]
        public async Task Connection_DisconnectAsync_FiresOnDisconnected()
        {
            using var pair = MakeSocketPair();
            var conn = new Connection(pair.Client);
            var tcs = new TaskCompletionSource<bool>();
            conn.OnDisconnected += () => tcs.TrySetResult(true);

            conn.DisconnectAsync();

            // DisconnectAsync may complete synchronously on loopback
            if (!tcs.Task.IsCompleted)
                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await tcs.Task);
            conn.Dispose();
        }

        [Fact]
        public void Connection_Dispose_DisposesTransport()
        {
            using var pair = MakeSocketPair();
            var conn = new Connection(pair.Client);
            conn.Dispose();

            Assert.True(conn.Transport.IsDisposed);
            pair.Client.Close();
            pair.Server.Close();
        }

        [Fact]
        public void Connection_IsConnected_False_AfterDisconnect()
        {
            using var pair = MakeSocketPair();
            var conn = new Connection(pair.Client);
            Assert.True(conn.IsConnected);

            conn.DisconnectAsync();

            // After disconnect, IsConnected should be false
            Assert.False(conn.IsConnected);
            conn.Dispose();
        }

        // --- Concurrency Tests ---

        [Fact]
        public void Connection_DisconnectAsync_RacingWithDispose()
        {
            using var pair = MakeSocketPair();
            var conn = new Connection(pair.Client);

            try
            {
                var t1 = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < 5; i++)
                        try { conn.DisconnectAsync(); } catch { }
                });
                var t2 = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < 5; i++)
                        try { conn.Dispose(); } catch { }
                });
                System.Threading.Tasks.Task.WhenAll(t1, t2).Wait(3000);
            }
            catch (Exception e)
            {
                Assert.Fail($"Racing disconnect/dispose threw: {e.Message}");
            }

            Assert.True(conn.Transport.IsDisposed);
        }

        [Fact]
        public void ServerConnector_AcceptConnection_RacingWithStopListening()
        {
            var connector = new ServerConnector();
            connector.StartListening("127.0.0.1", 0);

            try
            {
                var t1 = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < 5; i++)
                        try { connector.AcceptConnection(); } catch { }
                });
                var t2 = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < 5; i++)
                        try { connector.StopListening(); } catch { }
                });
                System.Threading.Tasks.Task.WhenAll(t1, t2).Wait(3000);
            }
            catch (Exception e)
            {
                Assert.Fail($"Racing accept/stop threw: {e.Message}");
            }

            connector.Dispose();
        }

        // --- Helper ---
        private static SocketPairHelper MakeSocketPair()
        {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.Connect(new IPEndPoint(IPAddress.Loopback, port));
            var server = listener.Accept();
            listener.Close();

            return new SocketPairHelper(client, server);
        }

        private class SocketPairHelper : IDisposable
        {
            public Socket Client { get; }
            public Socket Server { get; }
            public SocketPairHelper(Socket client, Socket server)
            {
                Client = client;
                Server = server;
            }

            public void Dispose()
            {
                try { Client.Shutdown(SocketShutdown.Both); } catch { }
                try { Client.Close(); } catch { }
                try { Server.Shutdown(SocketShutdown.Both); } catch { }
                try { Server.Close(); } catch { }
            }
        }
    }
}