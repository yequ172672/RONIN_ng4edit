using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace YakumoLib.DEFLATE
{
    internal static partial class DeflateSharp
    {
        [LibraryImport("DeflateSharp.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial int GDeflate_Decompress(
            IntPtr compressedData, nuint compressedSize,
            IntPtr outputBuffer, nuint outputSize,
            uint numWorkers
            );

        [LibraryImport("DeflateSharp.dll")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial int GDeflate_Compress(
            IntPtr inputBuffer, nuint inputSize,
            IntPtr outputBuffer, nuint outputSize,
            uint numWorkers
            );


    }
}
