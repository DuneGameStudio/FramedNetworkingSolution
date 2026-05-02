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
        private readonly PacketRegistry _registry;
        private readonly Func<IPacketEncryptor>? _encryptorFactory;
        private readonly IClient _client;

        private int _disposed;

        public bool IsConnected => _client.IsConnected;

        public event Action<IPeer>? OnPeerConnected;
        public event Action<SocketError>? OnConnectFailed;

        public PeerClient(PacketRegistry registry, Func<IPacketEncryptor>? encryptorFactory = null, IClient? client = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _encryptorFactory = encryptorFactory;

            _client = client ?? new ClientConnector();
            _client.OnConnected += HandleConnected;
            _client.OnConnectFailed += HandleConnectFailed;
        }

        public bool ConnectAsync(string address, int port)
            => _client.ConnectAsync(address, port);

        private void HandleConnected(IConnection connection)
        {
            IPacketEncryptor? encryptor = _encryptorFactory?.Invoke();
            var peer = new Peer(connection, _registry, encryptor);
            peer.StartReceiving();
            OnPeerConnected?.Invoke(peer);
        }

        private void HandleConnectFailed(SocketError error)
            => OnConnectFailed?.Invoke(error);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            _client.OnConnected -= HandleConnected;
            _client.OnConnectFailed -= HandleConnectFailed;
            _client.Dispose();
        }
    }
}
