using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Portable.Gc.Integration;

namespace GC.FirstFit
{
    unsafe class ExpandableFirstFitMemMan : IMemoryManager
    {
        const int Alignment = 16;

        IMemoryManager _underlyingMM;

        MMPool* _pools = MMPool.ZeroPtr;
        int _poolSize;

        MMFreeBlockHeader* _freeBlocks = MMFreeBlockHeader.ZeroPtr;
        MMStatsFrame _stats;

        public ExpandableFirstFitMemMan(IMemoryManager underlyingMM, int poolSize)
        {
            _underlyingMM = underlyingMM;
            _poolSize = poolSize;

            this.Expand();

            _stats.currentAllocatedBlocksCount = 0;
            _stats.currentAllocatedBytes = 0;
            _stats.currentAllBlocksCount = 1;
            _stats.currentAvailableBytes = _poolSize - sizeof(MMBusyBlockHeader);

            _stats.totalBlocksCreated = 1;
            _stats.totalBlocksMerged = 0;
            _stats.totalBlocksAllocated = 0;
            _stats.totalBlocksFreed = 0;
        }

        private void Expand()
        {
            var poolSize = _poolSize;
            var poolHeaderOffset = poolSize.AlignTo(Alignment);
            var newPool = _underlyingMM.Alloc((poolHeaderOffset + sizeof(MMPool)).AlignTo(Alignment)).value;

            if (newPool == IntPtr.Zero)
                throw new OutOfMemoryException("Failed to expand memory pool");

            MMFreeBlockHeader* item = (MMFreeBlockHeader*)newPool;
            item->next = MMFreeBlockHeader.ZeroPtr;
            item->size = poolSize - sizeof(MMFreeBlockHeader);
            this.InsertFreeBlockToList(item);

            MMPool* poolHeader = (MMPool*)(((int)newPool) + poolHeaderOffset);
            poolHeader->next = _pools;
            poolHeader->rangePtr = newPool;
            poolHeader->size = poolSize;
            _pools = poolHeader;
        }

        public BlockPtr Alloc(int size)
        {
            var ptr = this.InternalAlloc(size);

            if (ptr == (void*)0)
            {
                this.Expand();

                ptr = this.InternalAlloc(size);
             
                if (ptr == (void*)0)
                    throw new OutOfMemoryException();
            }

            return new BlockPtr(new IntPtr(ptr));
        }

        public void Free(BlockPtr ptr)
        {
            this.InternalFree(ptr.value.ToPointer());
        }

        public void ForceCollection()
        {
            throw new NotImplementedException();
        }

        void* InternalAlloc(int size)
        {
            MMFreeBlockHeader* pprev;
            MMFreeBlockHeader* p;
            MMFreeBlockHeader* next;
            MMBusyBlockHeader* ret;
            int retSize;

            // size += (MM_ALIGNMENT - ((size - 1) % MM_ALIGNMENT)) - 1;
            size = size.AlignTo(Alignment);

            pprev = MMFreeBlockHeader.ZeroPtr;
            p = _freeBlocks;

            while (p != MMFreeBlockHeader.ZeroPtr)
            {
                if (p->size + sizeof(MMFreeBlockHeader) - sizeof(MMBusyBlockHeader) >= size)
                {
                    if (p->size + sizeof(MMFreeBlockHeader) - sizeof(MMBusyBlockHeader) - size > sizeof(MMFreeBlockHeader))
                    {

                        MMFreeBlockHeader* newBlock = (MMFreeBlockHeader*)(((int)p) + (sizeof(MMBusyBlockHeader) + size));
                        newBlock->size = (p->size + sizeof(MMFreeBlockHeader)) - (sizeof(MMBusyBlockHeader) + size) - sizeof(MMFreeBlockHeader);
                        newBlock->next = p->next;

                        p->size = sizeof(MMBusyBlockHeader) + size - sizeof(MMFreeBlockHeader);
                        p->next = newBlock;

                        _stats.currentAllBlocksCount++;
                        _stats.currentAvailableBytes -= sizeof(MMBusyBlockHeader);
                        _stats.totalBlocksCreated++;
                    }

                    next = p->next;

                    if (pprev == MMFreeBlockHeader.ZeroPtr && next == MMFreeBlockHeader.ZeroPtr)
                    {
                        _freeBlocks = MMFreeBlockHeader.ZeroPtr;
                    }
                    if (pprev == MMFreeBlockHeader.ZeroPtr)
                    {
                        _freeBlocks = next;
                    }
                    else if (next == MMFreeBlockHeader.ZeroPtr)
                    {
                        pprev->next = MMFreeBlockHeader.ZeroPtr;
                    }
                    else
                    {
                        pprev->next = next;
                    }

                    retSize = p->size + sizeof(MMFreeBlockHeader) - sizeof(MMBusyBlockHeader);
                    ret = (MMBusyBlockHeader*)p;
                    ret->size = retSize;

                    _stats.currentAllocatedBytes += ret->size;
                    _stats.currentAllocatedBlocksCount++;
                    _stats.currentAvailableBytes -= ret->size;
                    _stats.totalBlocksAllocated++;

                    return (void*)(((int)ret) + sizeof(MMBusyBlockHeader));
                }

                pprev = p;
                p = p->next;
            }

            return MMFreeBlockHeader.ZeroPtr;
        }

        bool TryMergeFreeBlocks(MMFreeBlockHeader* first, MMFreeBlockHeader* second)
        {
            if (((int)first) + first->size + sizeof(MMFreeBlockHeader) == (int)second)
            {
                first->size += sizeof(MMFreeBlockHeader) + second->size;

                _stats.currentAllBlocksCount--;
                _stats.currentAvailableBytes += sizeof(MMBusyBlockHeader);
                _stats.totalBlocksMerged++;

                return true;
            }
            else
            {
                return false;
            }
        }

        int GetBusyBlockSize(void* ptr)
        {
            MMBusyBlockHeader* bblock;
            if (ptr == (void*)0)
                return 0;

            bblock = (MMBusyBlockHeader*)(((int)ptr) - sizeof(MMBusyBlockHeader));

            return bblock->size;
        }

        void InternalFree(void* ptr)
        {
            MMBusyBlockHeader* bblock;
            MMFreeBlockHeader* block;
            int size;

            if (ptr == (void*)0)
                return;

            bblock = (MMBusyBlockHeader*)(((int)ptr) - sizeof(MMBusyBlockHeader));

            _stats.currentAllocatedBytes -= bblock->size;
            _stats.currentAllocatedBlocksCount--;
            _stats.currentAvailableBytes += bblock->size;
            _stats.totalBlocksFreed++;

            block = (MMFreeBlockHeader*)bblock;
            size = bblock->size + sizeof(MMBusyBlockHeader) - sizeof(MMFreeBlockHeader);
            block->size = size;
            block->next = MMFreeBlockHeader.ZeroPtr;

            InsertFreeBlockToList(block);
        }

        void InsertFreeBlockToList(MMFreeBlockHeader* block)
        {
            if (_freeBlocks == MMFreeBlockHeader.ZeroPtr)
            {
                _freeBlocks = block;
            }
            else
            {
                MMFreeBlockHeader* p = _freeBlocks;
                MMFreeBlockHeader* pprev = MMFreeBlockHeader.ZeroPtr;

                while (p != MMFreeBlockHeader.ZeroPtr)
                {
                    if (p > block)
                        break;

                    pprev = p;
                    p = p->next;
                }

                // insert before p, after pprev

                if (pprev == MMFreeBlockHeader.ZeroPtr) // try merge [block, p] 
                {
                    if (TryMergeFreeBlocks(block, p))
                    {
                        block->next = p->next;
                        _freeBlocks = block;
                    }
                    else
                    {
                        block->next = p;
                        _freeBlocks = block;
                    }
                }
                else if (p == MMFreeBlockHeader.ZeroPtr) // try merge [pprev, block]
                {
                    if (TryMergeFreeBlocks(pprev, block))
                    {
                        // do nothing
                    }
                    else
                    {
                        pprev->next = block;
                        block->next = MMFreeBlockHeader.ZeroPtr;
                    }
                }
                else // try merge [pprev, block] and [block, p]
                {
                    if (TryMergeFreeBlocks(pprev, block))
                    {

                        if (TryMergeFreeBlocks(pprev, p))
                        {
                            pprev->next = p->next;
                        }
                    }
                    else
                    {
                        if (TryMergeFreeBlocks(block, p))
                        {
                            pprev->next = block;
                            block->next = p->next;
                        }
                        else
                        {
                            pprev->next = block;
                            block->next = p;
                        }
                    }
                }
            }
        }

        MMStatsFrame GetStats()
        {
            return _stats;
        }

        public void Dispose()
        {
        }
    }
}
