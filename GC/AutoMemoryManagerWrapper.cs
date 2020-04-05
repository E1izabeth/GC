using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Portable.Gc.Integration;

namespace GC
{
    class AutoMemoryManagerWrapper: IMemoryManager
    {
        private IMemoryManager _memoryManager;

        public AutoMemoryManagerWrapper(IMemoryManager memMan)
        {
            _memoryManager = memMan;
        }

        public BlockPtr Alloc(int size)
        {

            return _memoryManager.Alloc(size);
        }

        public void Dispose()
        {
            _memoryManager.Dispose();
        }

        public void Free(BlockPtr blockPtr)
        {
            _memoryManager.Free(blockPtr);
        }
    }
}
