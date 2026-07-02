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
    /// <summary>
    /// Connected peer that can serialize, encrypt, send, decrypt, and deserialize packets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wraps an <see cref="IConnection"/> with packet registry lookup and optional encryption.
    /// Provides the main send/receive interface for application-level packet I/O.
    /// </para>
    /// </remarks>
    public class Peer : IPeer
    {
        private readonly IConnection _connection;
        private readonly PacketRegistry _packetRegistry;
        private readonly IPacketEncryptor? _encryptor;
        /// <summary>Disposed</summary>
        private int _disposed;

        /// <inheritdoc />
        public IConnection Connection => _connection;

        /// <inheritdoc />
        public bool IsConnected => _connection.IsConnected;

        public event Action<PacketError>? OnSerializeFailed;
        public event Action<PacketError>? OnDeserializeFailed;

        /// <summary>
        /// Creates a new peer for the given connection.
        /// </summary>
        /// <param name="connection">An active connection to wrap.</param>
        /// <param name="packetRegistry">Registry for incoming packet handler lookup.</param>
        /// <param name="encryptor">Optional encryptor for bidirectional encryption. Null disables encryption.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="connection"/> or <paramref name="packetRegistry"/> is null.</exception>
        public Peer(IConnection connection, PacketRegistry packetRegistry, IPacketEncryptor? encryptor = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _packetRegistry = packetRegistry ?? throw new ArgumentNullException(nameof(packetRegistry));
            _encryptor = encryptor;
        }

        /// <inheritdoc />
        public Segment SerializeAndEncrypt<T>(T packet) where T : IPacket
        {
            ushort packetId = packet.PacketId;
            IPacketEncryptor? encryptor = _encryptor;

            if (!packet.Serialize(_connection.Transport, (s, size) =>
            {
                var span = s.Memory.Span;
                PresentationHeader.Write(span, packetId);

                if (encryptor != null)
                    encryptor.Encrypt(span.Slice(0, size), span);
            }))
            {
                OnSerializeFailed?.Invoke(PacketError.PoolExhausted);
                return default;
            }
            return packet.segment;
        }

        /// <inheritdoc />
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
                segment.Release();
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
                segment.Release();
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

            // packet.Deserialize() internally calls ISegmentManager.Deserialize() which
            // ALWAYS releases the segment (regardless of success/failure).
            // Do NOT call segment.Release() again here — that would be a double-release.
            if (!packet.Deserialize())
            {
                OnDeserializeFailed?.Invoke(PacketError.DeserializeError);
                return null;
            }

            return (packet, entry.Invoke);
        }

        /// <inheritdoc />
        public void Send(Segment segment, int packetSize)
        {
            _connection.Transport.SendAsync(segment, packetSize);
        }

        /// <inheritdoc />
        public void DisconnectAsync()
        {
            _connection.DisconnectAsync();
        }

        /// <summary>Releases the underlying connection. Idempotent.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            _connection.Dispose();
        }
    }
}
