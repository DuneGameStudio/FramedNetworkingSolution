using System.Diagnostics;
using FramedNetworkingSolution.Transport.Interface;

namespace FramedNetworkingSolution.Utils
{
    public interface SegmentManager
    {
        /// <summary>
        ///     Segment Reference Produced Oreginally From a SegmentedBuffer Instance.
        /// </summary>
        /// <value></value>
        Segment segment { get; set; }

        /// <summary>
        ///     The Number of Actually Used Bytes Of The Segment.
        /// </summary>
        /// <value></value>
        public int PacketSize { get; set; }

        /// <summary>
        ///     Ment To be Overridden To Implment Data Deserialization.
        /// </summary>
        void OnDeserialize();

        /// <summary>
        ///     Meant To be Overridden To Implement Data Serialization.
        /// </summary>
        void OnSerialize();

        /// <summary>
        ///     Starts an Async Send Operation and Releases the Segment.
        /// </summary>
        /// <param name="transport"></param>
        void OnSend(ITransport transport)
        {
            transport.SendAsync(transport.sendBuffer.GetRegisteredMemory(segment.SegmentIndex, PacketSize));
            segment.Release();
        }

        /// <summary>
        ///     Reserves a Segment To Serialize the Packet Into it, and Then Calls the OnSerialize 
        /// </summary>
        /// <param name="segmentedBuffer"></param>
        void Serialize(SegmentedBuffer segmentedBuffer)
        {
            if (segmentedBuffer.ReserveMemory(out Segment newSegment))
            {
                Debug.WriteLine("New Segment");

                segment = newSegment;
            };

            OnSerialize();
        }

        /// <summary>
        ///     Calles OnDeserialize and Releases The Segment.
        /// </summary>
        void Deserialize()
        {
            OnDeserialize();
            segment.Release();
        }
    }
}