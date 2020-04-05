using Portable.Gc.Integration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GC
{
    public class AutoMemoryManagerFabric : IAutoMemoryManagerFabric
    {
        string IAutoMemoryManagerFabric.Name => "MyGC";

        Version IAutoMemoryManagerFabric.Version => new Version("1.0.0");

        IAutoMemoryManagementContext IAutoMemoryManagerFabric.CreateManagerContext(IRuntimeGlobalAccessor runtimeInfoAccessor)
        {
            return new AutoMemoryManagementContext(runtimeInfoAccessor);
        }
    }
}
