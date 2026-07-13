using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace YakumoLib.Database
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AssetDatabaseFileEntry
    {
        public uint IdA;
        public uint IdB;
        public uint IdC;
        public uint IdD;
        public uint NameOffset;
        public uint PathOffset;
        public uint TypeOffset;
        public uint Unknown;
        public uint LanguageInfoIndex;
        public uint LanguageGroupCount;
    }
}
