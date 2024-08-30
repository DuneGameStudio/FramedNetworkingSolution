using System.Diagnostics;
using FramedNetworkingSolution.Transport.Interface;

namespace FramedNetworkingSolution.Utils
{
    public interface SegmentManager
    {
        Segment segment { get; set; }
        public int PacketSize { get; set; }

        void OnDeserialize();
        void OnSerialize();

        void OnSend(ITransport transport)
        {
            transport.SendAsync(transport.sendBuffer.GetRegisteredMemory(segment.SegmentIndex, PacketSize));
            segment.Release();
        }

        void Serialize(SegmentedBuffer segmentedBuffer)
        {
            if (segmentedBuffer.ReserveMemory(out Segment newSegment))
            {
                Debug.WriteLine("New Segment");

                segment = newSegment;
            };

            OnSerialize();
        }

        void Deserialize()
        {
            OnDeserialize();
            segment.Release();
        }
    }
}