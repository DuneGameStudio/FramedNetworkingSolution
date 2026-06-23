using System;
using System.Threading;
using DunePresentation.Encryption.Interface;
using DunePresentation.Packet;
using DunePresentation.Packet.Interfaces;
using DunePresentation.Peer.Interfaces;
using DuneSession.SocketConnectors.Interface;
using DuneTransport.BufferManager;

namespace DunePresentation.Peer
{
    public class Peer : IPeer
    {
        private readonly IConnection _connection;
        private readonly PacketRegistry _packetRegistry;
        private readonly IPacketEncryptor? _encryptor;
        private int _disposed;

        public IConnection Connection => _connection;
        public bool IsConnected => _connection.IsConnected;

        public event Action<PacketError>? OnSerializeFailed;
        public event Action<PacketError>? OnDeserializeFailed;

        public Peer(IConnection connection, PacketRegistry packetRegistry, IPacketEncryptor? encryptor = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _packetRegistry = packetRegistry ?? throw new ArgumentNullException(nameof(packetRegistry));
            _encryptor = encryptor;
        }

        public Segment SerializeAndEncrypt<T>(T packet) where T : IPacket
        {
            ushort packetId = packet.PacketId;
            IPacketEncryptor? encryptor = _encryptor;
            Segment reserved = default;

            if (!_connection.Transport.TryReserveSendPacket(out Segment seg))
            {
                OnSerializeFailed?.Invoke(PacketError.PoolExhausted);
                return default;
            }

            seg.Memory = seg.Memory.Slice(PresentationHeader.Size);

            try
            {
                if (!packet.Serialize(_connection.Transport, (s, size) =>
                {
                    var span = s.Memory.Span;
                    PresentationHeader.Write(span, packetId);

                    if (encryptor != null)
                        encryptor.Encrypt(span.Slice(0, size), span);
                }))
                {
                    OnSerializeFailed?.Invoke(PacketError.SerializationError);
                    return default;
                }
                return packet.segment;
            }
            catch
            {
                reserved.Release();
                OnSerializeFailed?.Invoke(PacketError.SerializationError);
                return default;
            }
        }

        public (IPacket Packet, Action<IPacket> Handler)? DecryptAndDeserialize(Segment segment)
        {
            var span = segment.Memory.Span;

            // Stage 1: Decrypt
            try
            {
                _encryptor?.Decrypt(span, span);
            }
            catch
            {
                OnDeserializeFailed?.Invoke(PacketError.DecryptError);
                return null;
            }

            // Stage 2: Read header
            ushort packetId;
            try
            {
                PresentationHeader.Read(span, out packetId);
            }
            catch
            {
                OnDeserializeFailed?.Invoke(PacketError.SerializationError);
                return null;
            }

            // Stage 3: Registry lookup
            if (!_packetRegistry.TryGetEntry(packetId, out Entry entry))
            {
                segment.Release();
                OnDeserializeFailed?.Invoke(PacketError.RegistryError);
                return null;
            }

            // Stage 4: Factory + deserialize (factory exceptions propagate)
            IPacket packet = entry.Factory();
            packet.segment = segment;
            packet.PacketSize = span.Length;

            if (!packet.Deserialize())
            {
                OnDeserializeFailed?.Invoke(PacketError.DeserializeError);
                return null;
            }

            return (packet, entry.Invoke);
        }

        public void Send(Segment segment, int packetSize)
        {
            _connection.Transport.SendAsync(segment, packetSize);
        }

        public void DisconnectAsync()
        {
            _connection.DisconnectAsync();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            _connection.Dispose();
        }
    }
}
