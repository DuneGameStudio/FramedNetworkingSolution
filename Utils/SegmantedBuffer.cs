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
        ///     Data Array Length.
        /// </summary>
        public int arrayLength;

        /// <summary>
        ///     Packet Segmant Size.
        /// </summary>
        int segmentSize;

        /// <summary>
        ///     Number of segments in the Byte Array
        /// </summary>
        int count
        {
            get
            {
                return arrayLength / segmentSize;
            }
        }

        /// <summary>
        ///     Next Free Segmant Count.
        /// </summary>
        int nextFree;

        /// <summary>
        ///     
        /// </summary>
        public Memory<byte> currentInUseMemory;

        /// <summary>
        ///     
        /// </summary>
        public int currentInUseMemoryArrayLocation;

        /// <summary>
        ///     
        /// </summary>
        /// <param name="arrayLength"></param>
        /// <param name="segmentSize"></param>
        public SegmantedBuffer(int arrayLength = 8192, int segmentSize = 256)
        {
            this.arrayLength = arrayLength;
            this.segmentSize = segmentSize;

            data = new byte[arrayLength];

            nextFree = 1;
        }

        /// <summary>
        ///     
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public bool TryReserveMemory(int size, out Memory<byte> memory)
        {
            if (nextFree == 0)
            {
                memory = new Memory<byte>();
                return false;
            }

            currentInUseMemoryArrayLocation = segmentSize * nextFree - segmentSize;
            memory = data.AsMemory(currentInUseMemoryArrayLocation, size);
            currentInUseMemory = memory;

            if (nextFree + 1 > count)
            {
                nextFree = 0;
            }
            else
            {
                nextFree++;
            }
            return true;
        }

        public Memory<byte> GetMemoryAtLocation(int currentInUseMemoryArrayLocation, int size)
        {
            return data.AsMemory(currentInUseMemoryArrayLocation, size);
        }

        /// <summary>
        ///     Reserves whole Segmant
        /// </summary>
        /// <param name="memory"></param>
        /// <returns></returns>
        public bool TryReserveAWholeSegmant(out Memory<byte> memory)
        {
            if (nextFree == 0)
            {
                memory = new Memory<byte>();
                return false;
            }

            currentInUseMemoryArrayLocation = segmentSize * nextFree - segmentSize;
            memory = data.AsMemory(currentInUseMemoryArrayLocation + 2, segmentSize - 2);
            
            // currentInUseMemory = data.AsMemory(currentInUseMemoryArrayLocation, segmentSize);

            if (nextFree + 1 > count)
            {
                nextFree = 0;
            }
            else
            {
                nextFree++;
            }
            return true;
        }
    }
}