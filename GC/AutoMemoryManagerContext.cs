using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Portable.Gc.Integration;

namespace GC
{
    internal class AutoMemoryManagementContext : IAutoMemoryManagementContext
    {
        public IRuntimeGlobalAccessor RuntimeGlobalAccessor { get; private set; }

        readonly MemManIntegration _integration = new MemManIntegration();
        public IMemManIntegration Integration { get { return _integration; } }

        public int MarkBitFieldNumber { get { return _integration.MarkBitFieldNumber; } }
        public int RealBlockSizeFieldNumber { get { return _integration.RealBlockSizeFieldNumber; } }

        AutoMemoryManager _memManInstance = null;

        public AutoMemoryManagementContext(IRuntimeGlobalAccessor runtimeGlobalAccessor)
        {
            this.RuntimeGlobalAccessor = runtimeGlobalAccessor;
        }

        public unsafe IAutoMemoryManager CreateManager(IMemoryManager underlying, IRuntimeContextAccessor runtimeContextAccessor)
        {
            return _memManInstance ?? (_memManInstance = new AutoMemoryManager(this, underlying));            
        }

        public void Dispose()
        {

        }
    }

}
