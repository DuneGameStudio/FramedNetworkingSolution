using System;
using System.Net.Sockets;
using DunePresentation.Encryption.Interface;
using DunePresentation.Packet;
using DunePresentation.Packet.Interfaces;
using DunePresentation.Peer.Interfaces;
using DuneSession.SocketConnectors.Interface;
using DuneTransport.BufferManager;
using DuneTransport.Transport;
using DuneTransport.Transport.Interface;

namespace DunePresentation.Tests
{
    // --- MockTransport ---
    public class MockTransport : ITransport
    {
        public bool IsConnected { get; set; } = true;
        public bool IsDisposed { get; private set; }
        public bool ReceiveArmed => false;
        public bool SendArmed => false;
        public bool ReceiveCalled { get; private set; }
        public int ReceiveCallCount { get; private set; }
        public bool SendCalled { get; private set; }
        public Segment? LastSentSegment { get; private set; }
        public int LastSentSize { get; private set; }
        public bool TryReserveSendCalled { get; private set; }
        public bool ReserveSendResult { get; set; } = true;

        public event Action<ITransport>? OnPacketSent;
        public event Action<ITransport, Segment, TransportError>? OnPacketSendFailed;
        public event Action<ITransport, Segment>? OnPacketReceived;
        public event Action<ITransport, TransportError>? OnPacketReceiveFailed;

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
        }

        public bool TryReserveSendPacket(out Segment segment)
        {
            TryReserveSendCalled = true;
            if (ReserveSendResult)
            {
                segment = new Segment(1, new byte[DefaultSegmentSize], _ => { });
                return true;
            }
            segment = default;
            return false;
        }

        private const int DefaultSegmentSize = 256;

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    // --- MockConnection ---
    public class MockConnection : IConnection
    {
        public bool IsConnected { get; private set; } = true;
        public ITransport Transport { get; }
        public bool DisconnectCalled { get; private set; }
        public bool Disposed { get; private set; }

        public event Action? OnDisconnected;

        public MockConnection() : this(new MockTransport()) { }
        public MockConnection(ITransport transport)
        {
            Transport = transport;
        }

        public void DisconnectAsync()
        {
            DisconnectCalled = true;
            IsConnected = false;
            OnDisconnected?.Invoke();
        }

        public void Dispose()
        {
            Disposed = true;
            IsConnected = false;
        }
    }

    // --- MockClientConnector ---
    public class MockClientConnector : IClientConnector
    {
        public bool IsConnected { get; private set; }
        public bool ConnectCalled { get; private set; }
        public string? LastAddress { get; private set; }
        public int LastPort { get; private set; }
        public bool Disposed { get; private set; }
        public bool RejectConnect { get; set; }

        public event Action<IConnection>? OnConnected;
        public event Action<SocketError>? OnConnectFailed;

        public bool ConnectAsync(string address, int port)
        {
            ConnectCalled = true;
            LastAddress = address;
            LastPort = port;
            if (RejectConnect)
                return false;
            return true;
        }

        public void SimulateConnected()
        {
            var conn = new MockConnection();
            IsConnected = true;
            OnConnected?.Invoke(conn);
        }

        public void SimulateFailed()
        {
            OnConnectFailed?.Invoke(SocketError.ConnectionRefused);
        }

        public void Dispose()
        {
            Disposed = true;
            IsConnected = false;
        }
    }

    // --- MockServerConnector ---
    public class MockServerConnector : IServerConnector
    {
        public bool IsListening { get; private set; }
        public bool Disposed { get; private set; }
        public bool AcceptCalled { get; private set; }
        public string? LastAddress { get; private set; }
        public int LastPort { get; private set; }

        public event Action<IConnection>? OnClientConnected;
        public event Action<SocketError>? OnAcceptFailed;

        public void StartListening(string address, int port)
        {
            LastAddress = address;
            LastPort = port;
            IsListening = true;
        }

        public void StopListening() => IsListening = false;

        public void AcceptConnection()
        {
            AcceptCalled = true;
        }

        public void SimulateClientConnected()
        {
            var conn = new MockConnection(new MockTransport { IsConnected = true });
            OnClientConnected?.Invoke(conn);
        }

        public void SimulateAcceptFailed()
        {
            OnAcceptFailed?.Invoke(SocketError.ConnectionRefused);
        }

        public void Dispose()
        {
            Disposed = true;
            IsListening = false;
        }
    }

    // --- MockPacketEncryptor ---
    public class MockEncryptor : IPacketEncryptor
    {
        public bool EncryptCalled { get; private set; }
        public bool DecryptCalled { get; private set; }
        public bool SimulateDecryptThrow { get; set; }
        public bool SimulateEncryptThrow { get; set; }

        public void Encrypt(ReadOnlySpan<byte> data, Span<byte> encrypted)
        {
            EncryptCalled = true;
            if (SimulateEncryptThrow)
                throw new InvalidOperationException("Encrypt failed");
            data.CopyTo(encrypted);
        }

        public void Decrypt(ReadOnlySpan<byte> encrypted, Span<byte> data)
        {
            DecryptCalled = true;
            if (SimulateDecryptThrow)
                throw new InvalidOperationException("Decrypt failed");
            encrypted.CopyTo(data);
        }
    }
}