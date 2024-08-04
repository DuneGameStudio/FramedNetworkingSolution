using System;
using System.Diagnostics;

namespace FramedNetworkingSolution.Utils
{
    public class SegmantedBuffer
    {
        /// <summary>
        ///     The byte array that holds all the data.
        /// </summary>
        byte[] data;

        /// <summary>
        ///     Packet Segmant Size.
        /// </summary>
        int segmentSize;

        /// <summary>
        ///     Number of segments in the Byte Array
        /// </summary>
        int segmentCount
        {
            get
            {
                return data.Length / segmentSize;
            }
        }

        /// <summary>
        ///     
        /// </summary>
        int freeFrom;

        /// <summary>
        ///     Next Free Segmant Count.
        /// </summary>
        int freeUpTo;

        /// <summary>
        ///     
        /// </summary>
        public int currentIndex;

        /// <summary>
        ///     
        /// </summary>
        /// <param name="arrayLength"></param>
        /// <param name="segmentSize"></param>
        public SegmantedBuffer(int arrayLength = 8192, int segmentSize = 256)
        {
            this.segmentSize = segmentSize;

            data = new byte[arrayLength];

            freeFrom = 1;
            freeUpTo = segmentCount;
        }

        /// <summary>
        ///     0123456
        ///     1234567
        /// </summary>
        /// <param name="memory"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public bool ReserveAndRegisterMemory(out Memory<byte> memory, out int index)
        {
            if (freeFrom == 0)
            {
                memory = new Memory<byte>();
                index = 0;
                return false;
            }

            index = freeFrom;
            currentIndex = freeFrom;
            var nextSegmentStart = (freeFrom - 1) * segmentSize;
            memory = data.AsMemory(nextSegmentStart, segmentSize);

            if (freeFrom + 1 > segmentCount)
            {
                if (freeUpTo == segmentCount)
                {
                    freeUpTo = 0;
                    freeFrom = 0;
                    return false;
                }
                else if (freeUpTo >= 1)
                {
                    freeFrom = 1;
                    return true;
                }
            }
            else if (freeFrom + 1 > freeUpTo && freeFrom <= freeUpTo)
            {
                freeUpTo = 0;
                freeFrom = 0;
                return false;
            }

            freeFrom++;

            return true;
        }

        public void UnregisterMemory(int index)
        {
            freeUpTo = index;

            if (freeFrom == 0)
            {
                freeFrom = index;
            }
        }

        public Memory<byte> GetRegisteredMemory(int index, int length)
        {
            return data.AsMemory((index - 1) * segmentSize, length);
        }
    }
}