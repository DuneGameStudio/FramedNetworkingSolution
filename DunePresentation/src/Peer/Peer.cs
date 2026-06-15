using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using DunePresentation.Encryption.Interface;
using DunePresentation.Packet;
using DunePresentation.Packet.Interfaces;
using DunePresentation.Peer.Interfaces;
using DuneSession.SocketConnectors.Interface;
using DuneTransport.BufferManager;
using DuneTransport.Transport;
using DuneTransport.Transport.Interface;

namespace DunePresentation.Peer
{
    public class Peer : IPeer
    {
        private readonly IConnection _connection;
        private readonly PacketRegistry _packetRegistry;
        private readonly IPacketEncryptor? _encryptor;
        private IPacket? _currentSendingPacket;
        private int _disposed;

        public bool IsConnected => _connection.IsConnected;

        public event Action? OnDisconnected;

        public event Action<IPacket, Action<IPacket>>? OnPacketReceived;
        public event Action? OnPacketSent;

        public event Action<TransportError>? OnHandlingPacketReceiveFailed;
        public event Action<TransportError>? OnPacketReceiveFailed;

        public event Action<TransportError>? OnHandlingPacketSendFailed;
        public event Action<IPacket, TransportError>? OnPacketSendFailed;

        public Peer(IConnection connection, PacketRegistry packetRegistry, IPacketEncryptor? encryptor = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _packetRegistry = packetRegistry ?? throw new ArgumentNullException(nameof(packetRegistry));
            _encryptor = encryptor;

            _connection.Transport.OnPacketReceived += OnPacketReceivedHandler;
            _connection.Transport.OnPacketSent += OnPacketSentHandler;

            _connection.Transport.OnPacketReceiveFailed += OnPacketReceiveFailedHandler;
            _connection.Transport.OnPacketSendFailed += OnPacketSendFailedHandler;

            _connection.OnDisconnected += OnDisconnectedHandler;
        }

        public void Receive()
        {
            _connection.Transport.ReceiveAsync();
        }

        public void Send<T>(T packet) where T : IPacket
        {
            try
            {
                ushort packetId = packet.PacketId;
                IPacketEncryptor? encryptor = _encryptor;

                if (!packet.Serialize(_connection.Transport, (seg, size) =>
                {
                    var span = seg.Memory.Span;
                    PresentationHeader.Write(span, packetId);

                    if (encryptor != null)
                        encryptor.Encrypt(span.Slice(0, size), span);
                }))
                {
                    OnHandlingPacketSendFailed?.Invoke(TransportError.SerializationError);
                    return;
                }

                _currentSendingPacket = packet;
                _connection.Transport.SendAsync(packet.segment, packet.PacketSize);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Peer.Send | Exception:\n{ex}", "error");
                OnHandlingPacketSendFailed?.Invoke(TransportError.HandlerFailed);
            }
        }

        private void OnPacketSendFailedHandler(ITransport transport, Segment segment, TransportError reason)
        {
            var packet = _currentSendingPacket!;
            _currentSendingPacket = null;
            segment.Release();
            OnPacketSendFailed?.Invoke(packet, reason);
        }

        private void OnPacketReceivedHandler(ITransport transport, SocketAsyncEventArgs args, Segment segment)
        {
            bool segmentOwned = true;
            try
            {
                var span = segment.Memory.Span;

                if (_encryptor != null)
                    _encryptor.Decrypt(span, span);

                PresentationHeader.Read(span, out ushort packetId);

                if (!_packetRegistry.TryGetEntry(packetId, out Entry entry))
                {
                    OnHandlingPacketReceiveFailed?.Invoke(TransportError.RegistryError);
                    return;
                }

                IPacket packet = entry.Factory();
                packet.segment = segment;
                packet.PacketSize = span.Length;

                segmentOwned = false;
                if (!packet.Deserialize())
                {
                    OnHandlingPacketReceiveFailed?.Invoke(TransportError.SerializationError);
                    return;
                }

                OnPacketReceived?.Invoke(packet, entry.Invoke);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Peer.OnPacketReceivedHandler | Exception:\n{ex}", "error");
                OnHandlingPacketReceiveFailed?.Invoke(TransportError.HandlerFailed);
            }
            finally
            {
                if (segmentOwned)
                    segment.Release();
            }
        }

        private void OnPacketSentHandler(ITransport transport)
        {
            _currentSendingPacket = null;
            OnPacketSent?.Invoke();
        }

        private void OnPacketReceiveFailedHandler(ITransport transport, TransportError reason)
        {
            OnPacketReceiveFailed?.Invoke(reason);
        }

        public void DisconnectAsync()
        {
            _connection.DisconnectAsync();
        }

        private void OnDisconnectedHandler()
        {
            OnDisconnected?.Invoke();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            _connection.Transport.OnPacketReceived -= OnPacketReceivedHandler;
            _connection.Transport.OnPacketReceiveFailed -= OnPacketReceiveFailedHandler;
            _connection.Transport.OnPacketSendFailed -= OnPacketSendFailedHandler;
            _connection.Transport.OnPacketSent -= OnPacketSentHandler;
            _connection.OnDisconnected -= OnDisconnectedHandler;

            _connection.Dispose();
        }
    }
}
