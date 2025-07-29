using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpEDL
{
    public class DataHelper
    {
        public static T Bytes2Struct<T>(byte[] data, int length) where T : struct 
        {
            T str;
            IntPtr ptr = Marshal.AllocHGlobal(length);
            Marshal.Copy(data, 0, ptr, length);
            str = Marshal.PtrToStructure<T>(ptr);
            Marshal.FreeHGlobal(ptr);
            return str;
        }

        public static byte[] Struct2Bytes<T>(T str) where T : struct
        {
            int length = Marshal.SizeOf(str);
            byte[] data = new byte[length];
            IntPtr ptr = Marshal.AllocHGlobal(length);
            Marshal.StructureToPtr(str, ptr, true);
            Marshal.Copy(ptr, data, 0, length);
            Marshal.FreeHGlobal(ptr);
            return data;
        }

        public static byte[] HexStr2Bytes(string hexStr)
        {
            if (hexStr.Length % 2 != 0)
                throw new ArgumentException("Hex string must have an even length.", "hexStr");
            int len = hexStr.Length / 2;
            byte[] data = new byte[len];
            for(int i=0;i<len; i++)
            {
                data[i] = Convert.ToByte(hexStr.Substring(i * 2, 2), 16);
            }
            return data;
        }

        public static int FindInBytes(ReadOnlySpan<byte> source, ReadOnlySpan<byte> pattern)
        {
            for(int i=0;i<=source.Length - pattern.Length; i++)
            {
                if(source.Slice(i, pattern.Length).SequenceEqual(pattern))
                    return i;
            }
            return -1;
        }
    }
}
