using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpEDL.DataClass
{
    public struct Ext4FileHeader
    {
        public uint Magic;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ushort FileHeaderSize;
        public ushort ChunkHeaderSize;
        public uint BlockSize;
        public uint TotalBlocks;
        public uint TotalChunks;
        public uint CRC32;
    }

    public struct Ext4ChunkHeader
    {
        public ushort Type;
        public ushort Reserved;
        public uint ChunkSize;
        public uint TotalSize;
    }
}
