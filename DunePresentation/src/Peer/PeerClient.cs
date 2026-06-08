using System;
using System.Net.Sockets;
using System.Threading;
using DunePresentation.Encryption.Interface;
using DunePresentation.Packet;
using DunePresentation.Peer.Interfaces;
using DuneSession.SocketConnectors;
using DuneSession.SocketConnectors.Interface;

namespace DunePresentation.Peer
{
    public sealed class PeerClient : IPeerClient
    {
        private readonly PacketRegistry _packetRegistry;
        private readonly Func<IPacketEncryptor>? _encryptorFactory;
        private readonly IClientConnector _clientConnector;

        private int _disposed;

        public bool IsConnected => _clientConnector.IsConnected;

        public event Action<IPeer>? OnPeerConnected;
        public event Action<SocketError>? OnConnectFailed;

        public PeerClient(PacketRegistry packetRegistry, Func<IPacketEncryptor>? encryptorFactory = null, IClientConnector? client = null)
        {
            _packetRegistry = packetRegistry ?? throw new ArgumentNullException(nameof(packetRegistry));
            _encryptorFactory = encryptorFactory;

            _clientConnector = client ?? new ClientConnector();
            _clientConnector.OnConnected += HandleConnected;
            _clientConnector.OnConnectFailed += HandleConnectFailed;
        }

        public bool ConnectAsync(string address, int port)
            => _clientConnector.ConnectAsync(address, port);

        private void HandleConnected(IConnection connection)
        {
            IPacketEncryptor? encryptor = _encryptorFactory?.Invoke();
            var peer = new Peer(connection, _packetRegistry, encryptor);
            OnPeerConnected?.Invoke(peer);
        }

        private void HandleConnectFailed(SocketError error)
            => OnConnectFailed?.Invoke(error);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            _clientConnector.OnConnected -= HandleConnected;
            _clientConnector.OnConnectFailed -= HandleConnectFailed;
            _clientConnector.Dispose();
        }
    }
}
