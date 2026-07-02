using System;
using System.Collections.Concurrent;
using DunePresentation.Packet.Interfaces;

namespace DunePresentation.Packet
{
    /// <summary>
    /// Holds a packet factory and its associated handler for a registered packet type.
    /// </summary>
    internal readonly struct Entry
    {
        /// <summary>Creates a new instance of the registered packet type.</summary>
        public readonly Func<IPacket> Factory;

        /// <summary>Invoked with a deserialized packet for application-level processing.</summary>
        public readonly Action<IPacket> Invoke;

        public Entry(Func<IPacket> factory, Action<IPacket> invoke)
        {
            Factory = factory;
            Invoke = invoke;
        }
    }

    /// <summary>
    /// Thread-safe registry that maps packet IDs to deserialization + handler logic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by <see cref="Peer"/> to route incoming packets. Each packet ID is
    /// registered once with a factory and handler delegate. Lookups are concurrent-safe.
    /// </para>
    /// </remarks>
    public sealed class PacketRegistry
    {
        private readonly ConcurrentDictionary<ushort, Entry> _entries = new ConcurrentDictionary<ushort, Entry>();

        /// <summary>
        /// Registers a handler for the specified packet ID and type.
        /// </summary>
        /// <typeparam name="T">The packet type, which must have a parameterless constructor.</typeparam>
        /// <param name="packetId">The unique packet identifier.</param>
        /// <param name="handler">Delegate invoked with deserialized packets of type <typeparamref name="T"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if <paramref name="packetId"/> is already registered.</exception>
        public void RegisterHandler<T>(ushort packetId, Action<T> handler) where T : IPacket, new()
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (!_entries.TryAdd(packetId, new Entry(
                factory: () => new T(),
                invoke: packet => handler((T)packet))))
            {
                throw new InvalidOperationException($"PacketId {packetId} is already registered.");
            }
        }

        /// <summary>
        /// Looks up the handler entry for a given packet ID.
        /// </summary>
        /// <param name="packetId">The packet ID to look up.</param>
        /// <param name="entry">When this method returns, contains the entry if found; otherwise a default <see cref="Entry"/>.</param>
        /// <returns><c>true</c> if the packet ID is registered; otherwise <c>false</c>.</returns>
        internal bool TryGetEntry(ushort packetId, out Entry entry)
        {
            return _entries.TryGetValue(packetId, out entry);
        }
    }
}
