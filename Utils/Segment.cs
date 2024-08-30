using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FramedNetworkingSolution.Utils
{
    public struct Segment
    {
        public int SegmentIndex { get; set; }

        public Memory<byte> Memory { get; set; }

        public Action<int> ReleaseMemoryCallback { get; set; }

        public void Release()
        {
            ReleaseMemoryCallback(SegmentIndex);
        }
    }
}