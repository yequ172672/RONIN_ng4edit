using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace YakumoLib.Database
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AssetDatabaseHeader
    {
        public uint Magic;
        public uint Group1Count;
        public uint LanguageInfoCount;
        public uint FileCount;
        public uint LanguageCount;
    }
}
