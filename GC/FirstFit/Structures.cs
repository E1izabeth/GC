using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GC.FirstFit
{

    unsafe struct MMFreeBlockHeader
    {
        public static readonly MMFreeBlockHeader* ZeroPtr = (MMFreeBlockHeader*)0;

        public MMFreeBlockHeader* next;
        public int size;
    }

    struct MMBusyBlockHeader
    {
        public int size;
    }

    struct MMStatsFrame
    {
        public int currentAllocatedBlocksCount;
        public int currentAllocatedBytes;
        public int currentAvailableBytes;
        public int currentAllBlocksCount;

        public int totalBlocksCreated;
        public int totalBlocksMerged;
        public int totalBlocksAllocated;
        public int totalBlocksFreed;
    }

    unsafe struct MMPool
    {
        public static readonly MMPool* ZeroPtr = (MMPool*)0;

        public MMPool* next;
        public IntPtr rangePtr;
        public int size;
    }
}
