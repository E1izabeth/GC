using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GC
{
    static class Extensions
    {
        public static int AlignTo(this int size, int alignment)
        {
            return size + (alignment - ((size - 1) % alignment)) - 1;
        }
    }
}
