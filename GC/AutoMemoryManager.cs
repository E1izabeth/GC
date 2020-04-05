using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Portable.Gc.Integration;
using MMFreeBlockHeader = GC.FirstFit.MMFreeBlockHeader;
using MMStatsFrame = GC.FirstFit.MMStatsFrame;

namespace GC
{
    internal unsafe class AutoMemoryManager : IAutoMemoryManager
    {
        private const int _initialPoolSize = 1024 * 1000; // 10MB
        private const int _alignment = 16;

        private IntPtr _pool;
        private int _poolSize;
        private MMFreeBlockHeader* _freeBlocks;
        private MMStatsFrame _stats;

        private IMemoryManager _underlying;
        private AutoMemoryManagementContext _autoMemoryManagementContext;

        private readonly INativeStructureLayoutInfo _defaultLayout;
        private readonly INativeStructureFieldInfo _markBitFieldInfo, _realBlockSizeFieldInfo;

        private StreamWriter _sw = new StreamWriter(new MemoryStream()); //@"d:\temp\gc-log.txt");

        public AutoMemoryManager(AutoMemoryManagementContext autoMemoryManagementContext, IMemoryManager underlying)
        {
            _sw.BaseStream.SetLength(0);

            _autoMemoryManagementContext = autoMemoryManagementContext;
            _underlying = underlying;

            _poolSize = _initialPoolSize;
            _pool = underlying.Alloc(_initialPoolSize).value;

            _defaultLayout = autoMemoryManagementContext.RuntimeGlobalAccessor.GetDefaultLayoutInfo();
            _markBitFieldInfo = _defaultLayout.Fields[autoMemoryManagementContext.MarkBitFieldNumber];
            _realBlockSizeFieldInfo = _defaultLayout.Fields[autoMemoryManagementContext.RealBlockSizeFieldNumber];

            MMFreeBlockHeader* item = (MMFreeBlockHeader*)_pool;

            item->next = MMFreeBlockHeader.ZeroPtr;
            item->size = _poolSize - sizeof(MMFreeBlockHeader);

            _freeBlocks = item;

            _stats.currentAllocatedBlocksCount = 0;
            _stats.currentAllocatedBytes = 0;
            _stats.currentAllBlocksCount = 1;
            _stats.currentAvailableBytes = _poolSize;

            _stats.totalBlocksCreated = 1;
            _stats.totalBlocksMerged = 0;
            _stats.totalBlocksAllocated = 0;
            _stats.totalBlocksFreed = 0;
        }

        private void Log(string line)
        {
            // _sw.WriteLine(line);
            _sw.Flush();
        }

        private void SetBlockSize(IntPtr blockPtr, int size)
        {
            Marshal.WriteInt32(blockPtr, _realBlockSizeFieldInfo.Offset, size);
        }

        private int GetBlockSize(IntPtr blockPtr)
        {
            var size = Marshal.ReadInt32(blockPtr, _realBlockSizeFieldInfo.Offset);

            if (size <= 0)
                throw new InvalidOperationException();
            if (!_knownBlocks.Contains(new BlockPtr(blockPtr)))
                throw new InvalidOperationException();

            return size;
        }

        private void Expand()
        {
            var newPool = _underlying.Alloc(_poolSize).value;

            if (newPool == IntPtr.Zero)
                throw new OutOfMemoryException("Failed to expand memory pool");

            MMFreeBlockHeader* item = (MMFreeBlockHeader*)newPool;
            item->next = MMFreeBlockHeader.ZeroPtr;
            item->size = _poolSize - sizeof(MMFreeBlockHeader);
            this.InsertFreeBlockToList(item);
        }

        private List<BlockPtr> _knownBlocks = new List<BlockPtr>();
        private List<BlockPtr> _currAvailableBlocks = new List<BlockPtr>();
        private List<BlockPtr> _releasedBlocks = new List<BlockPtr>();

        public BlockPtr Alloc(int size)
        {
            var result = this.InternalAlloc(size);
            if (result == IntPtr.Zero)
            {
                this.ForceCollection();

                result = this.InternalAlloc(size);
                //if (result == IntPtr.Zero)
                //    throw new OutOfMemoryException();

                if (result == IntPtr.Zero)
                {
                    this.Expand();

                    result = this.InternalAlloc(size);
                    if (result == IntPtr.Zero)
                        throw new OutOfMemoryException();
                }
            }

            this.Log(string.Join(" ", _stats.currentAllocatedBytes, _stats.currentAvailableBytes));


            var ptr = new BlockPtr(result);
            _knownBlocks.Add(ptr);
            return ptr;
        }

        private IntPtr InternalAlloc(int size)
        {
            MMFreeBlockHeader* pprev;
            MMFreeBlockHeader* p;
            MMFreeBlockHeader* next;
            IntPtr ret;
            int retSize;

            size += (_alignment - ((size - 1) % _alignment)) - 1;

            pprev = MMFreeBlockHeader.ZeroPtr;
            p = _freeBlocks;

            while (p != MMFreeBlockHeader.ZeroPtr)
            {
                if (p->size + sizeof(MMFreeBlockHeader) >= size)
                {
                    if (p->size + sizeof(MMFreeBlockHeader) - size > sizeof(MMFreeBlockHeader))
                    {

                        MMFreeBlockHeader* newBlock = (MMFreeBlockHeader*)(((int)p) + size);
                        newBlock->size = (p->size + sizeof(MMFreeBlockHeader)) - size - sizeof(MMFreeBlockHeader);
                        newBlock->next = p->next;

                        p->size = size - sizeof(MMFreeBlockHeader);
                        p->next = newBlock;

                        _stats.currentAllBlocksCount++;
                        //_stats.currentAvailableBytes -= sizeof(MMBusyBlockHeader);
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

                    retSize = p->size + sizeof(MMFreeBlockHeader);
                    ret = new IntPtr(p);
                    this.SetBlockSize(ret, retSize);

                    _stats.currentAllocatedBytes += retSize;
                    _stats.currentAllocatedBlocksCount++;
                    _stats.currentAvailableBytes -= retSize;
                    _stats.totalBlocksAllocated++;

                    return ret;
                }

                pprev = p;
                p = p->next;
            }

            return IntPtr.Zero;
        }

        private bool TryMergeFreeBlocks(MMFreeBlockHeader* first, MMFreeBlockHeader* second)
        {
            if (((int)first) + first->size + sizeof(MMFreeBlockHeader) == (int)second)
            {
                first->size += sizeof(MMFreeBlockHeader) + second->size;

                _stats.currentAllBlocksCount--;
                // _stats.currentAvailableBytes += sizeof(MMBusyBlockHeader);
                _stats.totalBlocksMerged++;

                return true;
            }
            else
            {
                return false;
            }
        }

        private void InternalFree(IntPtr ptr)
        {
            IntPtr bblock;
            MMFreeBlockHeader* block;
            int size;

            if (ptr == IntPtr.Zero)
                return;

            bblock = ptr;
            int bblockSize = this.GetBlockSize(bblock);

            _stats.currentAllocatedBytes -= bblockSize;
            _stats.currentAllocatedBlocksCount--;
            _stats.currentAvailableBytes += bblockSize;
            _stats.totalBlocksFreed++;

            block = (MMFreeBlockHeader*)bblock;
            size = bblockSize - sizeof(MMFreeBlockHeader);
            block->size = size;
            block->next = MMFreeBlockHeader.ZeroPtr;

            this.InsertFreeBlockToList(block);
        }

        private void InsertFreeBlockToList(MMFreeBlockHeader* block)
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
                    if (this.TryMergeFreeBlocks(block, p))
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
                    if (this.TryMergeFreeBlocks(pprev, block))
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
                    if (this.TryMergeFreeBlocks(pprev, block))
                    {

                        if (this.TryMergeFreeBlocks(pprev, p))
                        {
                            pprev->next = p->next;
                        }
                    }
                    else
                    {
                        if (this.TryMergeFreeBlocks(block, p))
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

        public void Dispose()
        {
            //do nothing
        }

        public void ForceCollection(int generation = -1)
        {
            if (generation != -1)
                throw new NotImplementedException();

            IRuntimeCollectionSession accessor;
            _autoMemoryManagementContext.RuntimeGlobalAccessor.RequestStop(() => {
                _currAvailableBlocks.Clear();

                using (accessor = _autoMemoryManagementContext.RuntimeGlobalAccessor.BeginCollection())
                {
                    var knownRoots = new HashSet<BlockPtr>();

                    for (int i = 0; i < accessor.RootPrioritiesCount; i++)
                    {
                        var roots = accessor.GetRoots(i).ToList();

                        var maxSubgraph = roots.Select(r => this.CountSubgraph(r)).Max();
                        System.Diagnostics.Debug.Print("" + maxSubgraph);

                        this.DoMark(roots);
                    }

                    this.Sweep(_pool, _pool + _poolSize);
                }

                _currAvailableBlocks.Clear();
            });
        }

        private void DoMark(IEnumerable<BlockPtr> roots)
        {
            _currAvailableBlocks.AddRange(roots);

            var ptrs = roots;
            foreach (var ptr in ptrs)
            {
                if (!this.GetMark(ptr))
                {
                    this.SetMark(ptr, true);

                    var refs = this.GetRefs(ptr);
                    if (refs.Any(r => !_knownBlocks.Contains(r)))
                        throw new InvalidOperationException();

                    this.DoMark(refs);
                }
            }
        }

        public unsafe int CountSubgraph(BlockPtr ptr)
        {
            var toDo = new Stack<BlockPtr>();
            var handled = new HashSet<BlockPtr>();
            var buff = stackalloc IntPtr[1];

            toDo.Push(ptr);

            while (toDo.Any())
            {
                var node = toDo.Pop();

                if (handled.Add(node))
                {
                    foreach (var item in _autoMemoryManagementContext.RuntimeGlobalAccessor.GetLayoutInfo(node).Fields.Where(f => f.IsReference))
                    {
                        item.GetValue(node, new IntPtr(buff));
                        var child = new BlockPtr(buff[0]);
                        if (buff[0] != IntPtr.Zero && !handled.Contains(child))
                        {
                            toDo.Push(child);
                        }
                    }
                }
            }

            return handled.Count;
        }

        private unsafe IEnumerable<BlockPtr> GetRefs(BlockPtr ptr)
        {
            var refBuff = stackalloc BlockPtr[1];

            var list = new List<BlockPtr>();
            foreach (var field in _autoMemoryManagementContext.RuntimeGlobalAccessor.GetLayoutInfo(ptr).Fields)
            {
                if (field.IsReference)
                {
                    field.GetValue(ptr, new IntPtr(refBuff));
                    var p = refBuff[0];

                    if (p.value.ToPointer() >= _pool.ToPointer() && p.value.ToPointer() < (_pool + _poolSize).ToPointer())
                    {
                        if (!_knownBlocks.Contains(ptr))
                            throw new InvalidOperationException();

                        list.Add(p);
                    }
                }
            }

            return list;
        }

        private unsafe void SetMark(BlockPtr ptr, bool value)
        {
            if (!_knownBlocks.Contains(ptr))
                throw new InvalidOperationException();

            var markField = _autoMemoryManagementContext.RuntimeGlobalAccessor.GetLayoutInfo(ptr).Fields[_autoMemoryManagementContext.MarkBitFieldNumber];

            var isMarked = stackalloc bool[1];
            isMarked[0] = value;
            markField.SetValue(ptr, new IntPtr(isMarked));
        }

        private unsafe bool GetMark(BlockPtr ptr)
        {
            if (!_knownBlocks.Contains(ptr))
                throw new InvalidOperationException();

            var markField = _autoMemoryManagementContext.RuntimeGlobalAccessor.GetLayoutInfo(ptr).Fields[_autoMemoryManagementContext.MarkBitFieldNumber];
            var isMarked = stackalloc bool[1];

            markField.GetValue(ptr, new IntPtr(isMarked));

            return isMarked[0];
        }

        private unsafe bool IsGarbage(BlockPtr ptr)
        {
            var isMarked = this.GetMark(ptr);
            bool isGarbage = !isMarked;
            if (_currAvailableBlocks.Contains(ptr) && isGarbage)
                throw new InvalidOperationException();

            return isGarbage;
        }

        private void Sweep(IntPtr heapStart, IntPtr heapEnd)
        {
            var blocksToRecycle = new List<BlockPtr>();

            MMFreeBlockHeader* freePtr = _freeBlocks;
            var ptr = new BlockPtr(heapStart);

            do
            {
                BlockPtr nextPtr;
                bool isFree = new IntPtr(freePtr) == ptr.value;

                if (_knownBlocks.Contains(ptr))
                {
                    Console.WriteLine("C");
                }
                else if (!isFree)
                {
                    Console.WriteLine("NC");
                }

                if (isFree)
                {
                    nextPtr = new BlockPtr(ptr.value + freePtr->size + sizeof(MMFreeBlockHeader));
                    freePtr = freePtr->next;
                }
                else
                {
                    if (this.IsGarbage(ptr))
                        blocksToRecycle.Add(ptr);
                    else
                        this.SetMark(ptr, false);

                    nextPtr = new BlockPtr(ptr.value + this.GetBlockSize(ptr.value));
                }

                if (freePtr < nextPtr.value.ToPointer() && freePtr != (void*)0)
                    throw new InvalidOperationException();

                ptr = nextPtr;
            } while (ptr.value.ToPointer() < heapEnd.ToPointer());

            blocksToRecycle.ForEach(p => this.Free(p));
        }

        public void Free(BlockPtr blockPtr)
        {
            Console.WriteLine("Free " + blockPtr);

            if (_currAvailableBlocks.Contains(blockPtr))
                throw new InvalidOperationException();

            this.InternalFree(blockPtr.value);

            _knownBlocks.Remove(blockPtr);
            _releasedBlocks.Add(blockPtr);
        }

        //write ref from obj to obj
        public void OnWriteRefMember(BlockPtr blockPtr, BlockPtr refPtr)
        {
            if (refPtr.CompareTo(blockPtr) == 0)
            {
                throw new NotSupportedException();
                //TODO: ref to self
            }
            //do nothing
        }
    }

}
