using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpEDL.DataClass
{
    public class QCResponse
    {
        public required string Response { get; set; }
        public Dictionary<string, string> ResponseProperites { get; set; } = new Dictionary<string, string>();
        public List<string> Logs { get; set; } = new List<string>();
        public byte[] UnhandledData { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// 检查该响应是否为成功响应，若不是则抛出异常
        /// </summary>
        /// <exception cref="InvalidDataException">该响应非成功响应时抛出此异常</exception>
        public void CheckAndThrow()
        {
            if(Response != "ACK")
                throw new InvalidDataException("Error from device " +  Response + 
                    "\n" + string.Join("", Logs));
        }
    }

    public class PartitionInfo
    {
        public required string Label { get;set; }
        public int Lun { get; set; } = 0;
        public int FileSectorOffset { get; set; } = 0;
        public required string StartSector { get; set; } // 由于rawprogram文件中分区表条目的StartSector为表达式,将此属性设置为string
        public long SectorLen { get; set; }
        public int BytesPerSector { get; set; }
        public bool Sparse { get; set; } = false;
        public string? FilePath { get; set; }
    }

    public class PatchInfo
    {
        public int SectorSize { get; set; }
        public int Lun { get; set; }
        public long ByteOffset { get; set; }
        public string? FileName { get; set; }
        public int SizeInBytes { get; set; }
        public string? StartSector { get; set; }
        public string? Value { get; set; }
        public string? What { get; set; }
    }
}
