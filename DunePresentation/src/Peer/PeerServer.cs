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
    public sealed class PeerServer : IPeerServer
    {
        private readonly PacketRegistry _registry;
        private readonly Func<IPacketEncryptor>? _encryptorFactory;
        private readonly IServer _server;

        private int _disposed;

        public bool IsListening => _server.IsListening;

        public event Action<IPeer>? OnPeerConnected;
        public event Action<SocketError>? OnAcceptFailed;

        public PeerServer(PacketRegistry registry, Func<IPacketEncryptor>? encryptorFactory = null, IServer? server = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _encryptorFactory = encryptorFactory;

            _server = server ?? new ServerConnector();
            _server.OnClientConnected += HandleClientConnected;
            _server.OnAcceptFailed += HandleAcceptFailed;
        }

        public void StartListening(string address, int port)
            => _server.StartListening(address, port);

        public void StopListening()
            => _server.StopListening();

        public void AcceptConnection()
            => _server.AcceptConnection();

        private void HandleClientConnected(IConnection connection)
        {
            IPacketEncryptor? encryptor = _encryptorFactory?.Invoke();
            var peer = new Peer(connection, _registry, encryptor);
            peer.StartReceiving();
            OnPeerConnected?.Invoke(peer);
        }

        private void HandleAcceptFailed(SocketError error)
            => OnAcceptFailed?.Invoke(error);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            _server.OnClientConnected -= HandleClientConnected;
            _server.OnAcceptFailed -= HandleAcceptFailed;
            _server.Dispose();
        }
    }
}