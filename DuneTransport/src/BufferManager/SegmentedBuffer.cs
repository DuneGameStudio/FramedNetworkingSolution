using System;
using System.Collections.Generic;

namespace DuneTransport.BufferManager
{
    public class SegmentedBuffer
    {
        readonly byte[] data;

        public int SegmentSize => segmentSize;

        public int SegmentCount => segmentCount;

        public int FreeCount => freeSegments.Count;

        readonly int segmentSize;

        readonly int segmentCount;

        readonly Queue<int> freeSegments;

        readonly bool[] isAllocated;

        public SegmentedBuffer(int arrayLength = 8192, int segmentCount = 32)
        {
            segmentSize = arrayLength / segmentCount;
            this.segmentCount = segmentCount;

            data = new byte[arrayLength];

            freeSegments = new Queue<int>(segmentCount);
            isAllocated = new bool[segmentCount + 1];

            for (int i = 1; i <= segmentCount; i++)
                freeSegments.Enqueue(i);
        }

        public bool TryReserveSegment(out Segment segment)
        {
            segment = new Segment();

            if (!freeSegments.TryDequeue(out int segmentIndex))
                return false;

            isAllocated[segmentIndex] = true;

            int segmentStart = (segmentIndex - 1) * segmentSize;
            segment.SegmentIndex = segmentIndex;
            segment.ReleaseMemoryCallback = ReleaseMemory;
            segment.Memory = data.AsMemory(segmentStart, segmentSize);
            return true;
        }

        public void ReleaseMemory(int segmentNumber)
        {
            if (segmentNumber < 1 || segmentNumber > segmentCount)
                throw new ArgumentOutOfRangeException(nameof(segmentNumber));

            if (!isAllocated[segmentNumber])
            {
                // Idempotent: already free. Transport relies on this for
                // handler-throw and dispose-race cleanup paths.
                return;
            }

            isAllocated[segmentNumber] = false;
            freeSegments.Enqueue(segmentNumber);
        }

        public bool GetRegisteredMemory(int segmentNumber, int length, out Memory<byte> registeredMemory)
        {
            registeredMemory = default;

            if (segmentNumber < 1 || segmentNumber > segmentCount)
                return false;

            if (length < 0 || length > segmentSize)
                return false;

            int start = (segmentNumber - 1) * segmentSize;
            registeredMemory = data.AsMemory(start, length);
            return true;
        }
    }
}
