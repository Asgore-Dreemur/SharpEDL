using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpEDL.DataClass
{
    [StructLayout(LayoutKind.Sequential)]
    public struct EFIHeader
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public char[] Magic;
        public int Version;
        public int HeaderSize;
        public int CRC;
        public int Reserved;
        public long HeaderStartSector;
        public long HeaderBackupStartSector;
        public long PartitionStartSector;
        public long PartitionEndSector;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] DiskGUID;
        public long GPTTableStartSector;
        public int GPTMemberCount;
        public int SizePerTableMember;
        public int _CRC;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPTTableMember
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] GUID;
        public long StartSector;
        public long EndSector;
        public long Attributes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 72)]
        public byte[] Label;
    }
}
