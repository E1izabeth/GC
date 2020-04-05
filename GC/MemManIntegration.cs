using Portable.Gc.Integration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GC
{
    internal class MemManIntegration : IMemManIntegration
    {
        public int MarkBitFieldNumber { get; private set; }
        public int RealBlockSizeFieldNumber { get; private set; }

        public MemManIntegration()
        {
        }

        public void AugmentObjectLayout(INativeStructureBuilder structureBuilder)
        {
            var f = structureBuilder.DefineField("mark-bit");
            f.BitsCount = 1;
            this.MarkBitFieldNumber = f.Number;

            var nbf = structureBuilder.DefineField("realBlockSize");
            nbf.Size = sizeof(int);
            this.RealBlockSizeFieldNumber = nbf.Number;
        }
    }
}
