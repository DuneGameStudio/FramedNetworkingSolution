using System;
using DuneTransport.Transport.Interface;

namespace DuneTransport.BufferManager.Interface
{
    /// <summary>
    /// Contract for objects that own a <see cref="Segment"/> during serialization or deserialization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides the segment lifecycle: reserve a segment, serialize/deserialize, then release.
    /// The <see cref="Serialize"/> and <see cref="Deserialize"/> default methods orchestrate
    /// the full lifecycle, calling the abstract hooks (<see cref="OnSerialize"/>, <see cref="OnDeserialize"/>)
    /// at the appropriate points.
    /// </para>
    /// <para>
    /// Segment ownership is critical: after <see cref="Serialize"/> succeeds, the segment
    /// is handed to the transport. After <see cref="Deserialize"/> completes (success or failure),
    /// the segment is automatically released.
    /// </para>
    /// </remarks>
    public interface ISegmentManager
    {
        /// <summary>
        /// The current <see cref="Segment"/> associated with this instance.
        /// </summary>
        /// <remarks>
        /// Set during <see cref="Serialize"/> (reserve) or by the transport during receive.
        /// Cleared or released after the operation completes.
        /// </remarks>
        Segment segment { get; set; }

        /// <summary>
        /// The total serialized size of the packet, including any framing headers.
        /// </summary>
        int PacketSize { get; set; }

        /// <summary>
        /// Called by <see cref="Serialize"/> to perform the actual serialization.
        /// </summary>
        /// <returns><c>true</c> if serialization succeeded; otherwise <c>false</c>.</returns>
        /// <remarks>
        /// Implementations should write to <see cref="segment"/>. On failure,
        /// <see cref="Serialize"/> releases the segment automatically.
        /// </remarks>
        bool OnSerialize();

        /// <summary>
        /// Called by <see cref="Deserialize"/> to perform the actual deserialization.
        /// </summary>
        /// <returns><c>true</c> if deserialization succeeded; otherwise <c>false</c>.</returns>
        /// <remarks>
        /// The segment is released by <see cref="Deserialize"/> regardless of the return value.
        /// </remarks>
        bool OnDeserialize();

        /// <summary>
        /// Orchestrates the full serialization lifecycle: reserve, serialize, callback.
        /// </summary>
        /// <param name="transport">
        /// The transport to reserve a send segment from.
        /// </param>
        /// <param name="afterSerialize">
        /// Optional callback invoked after successful serialization with the segment and size.
        /// </param>
        /// <returns><c>true</c> if the segment was reserved and serialization succeeded; otherwise <c>false</c>.</returns>
        /// <remarks>
        /// <para>
        /// Lifecycle:
        /// <list type="number">
        ///   <item>Reserve a segment from <paramref name="transport"/>.</item>
        ///   <item>Call <see cref="OnSerialize"/> to write data into the segment.</item>
        ///   <item>If serialization fails, release the segment and return <c>false</c>.</item>
        ///   <item>If successful, invoke <paramref name="afterSerialize"/> with the segment and <see cref="PacketSize"/>.</item>
        /// </list>
        /// </para>
        /// </remarks>
        bool Serialize(ITransport transport, Action<Segment, int>? afterSerialize = null)
        {
            if (!transport.TryReserveSendPacket(out Segment newSegment))
                return false;

            segment = newSegment;

            if (!OnSerialize())
            {
                segment.Release();
                return false;
            }

            afterSerialize?.Invoke(segment, PacketSize);
            return true;
        }

        /// <summary>
        /// Orchestrates the full deserialization lifecycle: callback, deserialize, release.
        /// </summary>
        /// <param name="beforeDeserialize">
        /// Optional callback invoked before deserialization begins, with the current segment and size.
        /// </param>
        /// <returns><c>true</c> if deserialization succeeded; otherwise <c>false</c>.</returns>
        /// <remarks>
        /// <para>
        /// Lifecycle:
        /// <list type="number">
        ///   <item>Invoke <paramref name="beforeDeserialize"/> if provided.</item>
        ///   <item>Call <see cref="OnDeserialize"/> to read data from the segment.</item>
        ///   <item>Release the segment (regardless of success or failure).</item>
        /// </list>
        /// </para>
        /// </remarks>
        bool Deserialize(Action<Segment, int>? beforeDeserialize = null)
        {
            beforeDeserialize?.Invoke(segment, PacketSize);

            bool result = OnDeserialize();
            segment.Release();
            return result;
        }
    }
}
