using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Portable.Gc.Integration;

[assembly: ExportMemoryManager(typeof(GC.Testing.AutoMemoryManagerCheckerFabric))]

namespace GC.Testing
{
    public class AutoMemoryManagerCheckerFabric : IAutoMemoryManagerFabric
    {
        string IAutoMemoryManagerFabric.Name => "MyGcChecker";

        Version IAutoMemoryManagerFabric.Version => new Version("1.0.0");

        private IAutoMemoryManagerFabric FindGcToCheck()
        {
            /*var asmFileName = Environment.GetEnvironmentVariable("GC_TO_CHECK_ASSEMBLY");
            var gcTypeName = Environment.GetEnvironmentVariable("GC_TO_CHECK_TYPE");

            if (string.IsNullOrWhiteSpace(asmFileName) || string.IsNullOrWhiteSpace(gcTypeName))
                throw new InvalidOperationException("Gc to check is not specified!");
                */
            var asmFileName = "D:/MyProjects/va/GC/GC/bin/Debug/GC.exe";
            var gcTypeName = "GC.AutoMemoryManagerFabric";

            var fileInfo = new FileInfo(asmFileName);
            var asm = Assembly.LoadFile(fileInfo.FullName);
            var gcCtors = asm.GetCustomAttributes<ExportMemoryManagerAttribute>()
                             .Where(a => a.FabricType.FullName == gcTypeName)
                             .Select(a => a.FabricType?.GetConstructor(Type.EmptyTypes))
                             .Where(c => c != null && c.DeclaringType.GetInterfaces().Any(i => i == typeof(IAutoMemoryManagerFabric)))
                             .ToArray();

            if (!gcCtors.Any())
                throw new InvalidOperationException("Failed to find suitable gc to check!");

            var gcFabric = (IAutoMemoryManagerFabric)gcCtors.First().Invoke(null);
            return gcFabric;
        }

        IAutoMemoryManagementContext IAutoMemoryManagerFabric.CreateManagerContext(IRuntimeGlobalAccessor runtimeInfoAccessor)
        {
            var underlyingFabric = this.FindGcToCheck();

            return new AutoMemoryManagerCheckerContext(underlyingFabric.CreateManagerContext(new RuntimeGlobalAccessorChecker(runtimeInfoAccessor)));
        }
    }

    class RuntimeGlobalAccessorChecker : IRuntimeGlobalAccessor
    {
        private IRuntimeGlobalAccessor _underlyingRuntimeAccessor;

        public RuntimeGlobalAccessorChecker(IRuntimeGlobalAccessor runtimeInfoAccessor)
        {
            _underlyingRuntimeAccessor = runtimeInfoAccessor;
        }

        bool IRuntimeContextAccessor.IsRunning { get { return _underlyingRuntimeAccessor.IsRunning;  } }

        IRuntimeCollectionSession IRuntimeContextAccessor.BeginCollection()
        {
            var underlyingSession = _underlyingRuntimeAccessor.BeginCollection();
            return underlyingSession;
        }

        INativeStructureLayoutInfo IRuntimeGlobalAccessor.GetDefaultLayoutInfo()
        {
            return _underlyingRuntimeAccessor.GetDefaultLayoutInfo();
        }

        INativeStructureLayoutInfo IRuntimeGlobalAccessor.GetLayoutInfo(BlockPtr blockPtr)
        {
            return _underlyingRuntimeAccessor.GetLayoutInfo(blockPtr);
        }

        void IRuntimeContextAccessor.RequestStop(Action callback)
        {
            _underlyingRuntimeAccessor.RequestStop(callback);
        }
    }

    class AutoMemoryManagerCheckerContext : IAutoMemoryManagementContext
    {
        private IAutoMemoryManagementContext _underlyingContext;

        public AutoMemoryManagerCheckerContext(IAutoMemoryManagementContext autoMemoryManagementContext)
        {
            _underlyingContext = autoMemoryManagementContext;
        }

        IMemManIntegration IAutoMemoryManagementContext.Integration
        {
            get { return _underlyingContext.Integration; }
        }

        IAutoMemoryManager IAutoMemoryManagementContext.CreateManager(IMemoryManager underlying, IRuntimeContextAccessor runtimeAccessor)
        {
            
            var underlyingManager = _underlyingContext.CreateManager(underlying, runtimeAccessor);
            return underlyingManager;
        }

        void IDisposable.Dispose()
        {
            _underlyingContext.Dispose();
        }
    }
}
