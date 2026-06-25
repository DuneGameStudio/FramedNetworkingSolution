using System;
using DunePresentation.Packet;
using DunePresentation.Packet.Interfaces;
using DuneSession.SocketConnectors.Interface;
using DuneTransport.BufferManager;

namespace DunePresentation.Peer.Interfaces
{
    /// <summary>
    /// A connected peer that can serialize, encrypt, send, decrypt, and deserialize packets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The core abstraction for peer-to-peer communication. Wraps an <see cref="IConnection"/>
    /// with packet registry lookup and optional encryption. Provides the main send/receive
    /// interface for application-level packet I/O.
    /// </para>
    /// <para>
    /// <b>Send path:</b> <see cref="SerializeAndEncrypt{T}"/> serializes a packet into a segment,
    /// optionally encrypts, then <see cref="Send"/> transmits it.
    /// </para>
    /// <para>
    /// <b>Receive path:</b> Application receives a raw segment from <see cref="IConnection"/>,
    /// calls <see cref="DecryptAndDeserialize"/> to decrypt, look up the handler, and deserialize.
    /// </para>
    /// </remarks>
    public interface IPeer : IDisposable
    {
        /// <summary>Gets the underlying connection this peer operates on.</summary>
        IConnection Connection { get; }

        /// <summary>Gets whether the underlying connection is currently active.</summary>
        bool IsConnected { get; }

        /// <summary>
        /// Raised when packet serialization fails.
        /// </summary>
        event Action<PacketError>? OnSerializeFailed;

        /// <summary>
        /// Raised when packet deserialization fails.
        /// </summary>
        event Action<PacketError>? OnDeserializeFailed;

        /// <summary>
        /// Serializes and optionally encrypts a packet, returning the prepared segment.
        /// </summary>
        /// <typeparam name="T">The packet type to serialize.</typeparam>
        /// <param name="packet">The packet instance to serialize.</param>
        /// <returns>A <see cref="Segment"/> ready for transmission, or default on failure.</returns>
        /// <remarks>
        /// <para>
        /// Pipeline: reserve segment → serialize fields → write header → encrypt (if configured).
        /// On failure, fires <see cref="OnSerializeFailed"/> and releases the segment.
        /// </para>
        /// </remarks>
        Segment SerializeAndEncrypt<T>(T packet) where T : IPacket;

        /// <summary>
        /// Decrypts, deserializes, and looks up the handler for an incoming packet.
        /// </summary>
        /// <param name="segment">A segment received from the transport.</param>
        /// <returns>
        /// A tuple of (packet, handler) on success, or null on failure.
        /// On success, the segment is consumed (released by deserialization).
        /// </returns>
        /// <remarks>
        /// <para>
        /// Four-stage pipeline:
        /// <list type="number">
        ///   <item>Decrypt (if encryptor configured)</item>
        ///   <item>Read and validate presentation header</item>
        ///   <item>Look up handler in registry</item>
        ///   <item>Factory create + deserialize fields</item>
        /// </list>
        /// </para>
        /// <para>
        /// On any failure, fires <see cref="OnDeserializeFailed"/> with the specific error code.
        /// </para>
        /// </remarks>
        (IPacket Packet, Action<IPacket> Handler)? DecryptAndDeserialize(Segment segment);

        /// <summary>
        /// Transmits a prepared segment to the remote peer.
        /// </summary>
        /// <param name="segment">The segment containing the serialized packet data.</param>
        /// <param name="packetSize">The total packet size including the presentation header.</param>
        void Send(Segment segment, int packetSize);

        /// <summary>
        /// Initiates a graceful disconnect of the underlying connection.
        /// </summary>
        void DisconnectAsync();
    }
}
