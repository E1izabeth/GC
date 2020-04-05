using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.ObjectModel;

using Portable.Gc.Integration;

[assembly: ExportMemoryManager(typeof(GC.AutoMemoryManagerFabric))]

namespace GC
{    
    public class Program
    {
        static void Main(string[] args)
        {

        }
    }
}
