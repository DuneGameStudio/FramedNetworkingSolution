using System;

namespace FramedNetworkingSolution.Utils
{
    public class SegmentedBuffer
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
        public int currentSegmentNumber;

        /// <summary>
        ///     
        /// </summary>
        /// <param name="arrayLength"></param>
        /// <param name="segmentSize"></param>
        public SegmentedBuffer(int arrayLength = 8192, int segmentSize = 256)
        {
            this.segmentSize = segmentSize;

            data = new byte[arrayLength];

            freeFrom = 1;
            freeUpTo = segmentCount;
        }

        /// <summary>
        ///     
        ///     
        /// </summary>
        /// <param name="memory"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public bool ReserveMemory(out Memory<byte> memory, bool sending = true)
        {
            if (freeFrom == 0)
            {
                memory = new Memory<byte>();
                return false;
            }

            currentSegmentNumber = freeFrom;
            var nextSegmentStart = (freeFrom - 1) * segmentSize;

            if (sending)
            {
                memory = data.AsMemory(nextSegmentStart + 2, segmentSize - 2);
            }
            else
            {
                memory = data.AsMemory(nextSegmentStart, segmentSize);
            }

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

        public void ReleaseMemory(int segmentNumber)
        {
            freeUpTo = segmentNumber;

            if (freeFrom == 0)
            {
                freeFrom = segmentNumber;
            }
        }

        public Memory<byte> GetRegisteredMemory(int segmentNumber, int length)
        {
            return data.AsMemory((segmentNumber - 1) * segmentSize, length + 2);
        }
    }
}