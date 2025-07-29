using SharpEDL.DataClass;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SharpEDL
{
    /// <summary>
    /// 处理稀疏文件写入
    /// </summary>
    public class SparseWriter : IDisposable
    {
        public FirehoseServer Server { get; set; }
        public Ext4FileHeader FileHeader { get; set; }
        public PartitionInfo PartitionInfo { get; set; }
        private FileStream FileHandle { get; set; }

        /// <summary>
        /// <para>进度改变通知器</para>
        /// <para>元组中第一个元素为已经写入的扇区数，第二个元素为总扇区数</para>
        /// </summary>
        public event EventHandler<(long, long)>? ProgressChanged;

        /// <summary>
        /// 一次性写入的数据大小
        /// </summary>
        public int OnceReadSize { get; set; } = 128 * 1024 * 1024;

        private Thread? DataHandleThread, WriteThread;
        private BlockingCollection<byte[]> DataBuffer;
        private Exception? InnerException = null;
        
        private int RemainingChunks;
        private long TotalSectors;
        private int MaxItemCountInBuffer;

        public static readonly uint HeaderMagic = 0xED26FF3A;
        public static readonly int HeaderSize = 28;
        public static readonly int ChunkHeaderSize = 12;

        /// <param name="server"><see cref="FirehoseServer"/>实例</param>
        /// <param name="info">要被刷入的分区信息,必须指定<see cref="PartitionInfo.FilePath"/>为镜像路径</param>
        /// <param name="maxItemCountInBuffer">数据缓冲区的最大字节数组数量大小,默认为1024</param>
        /// <exception cref="ArgumentNullException"><see cref="PartitionInfo.FilePath"/>为空时将抛出此异常</exception>
        public SparseWriter(FirehoseServer server, PartitionInfo info, int maxItemCountInBuffer = 1024)
        {
            PartitionInfo = info;
            Server = server;
            if(string.IsNullOrEmpty(info.FilePath))
                throw new ArgumentNullException(nameof(info.FilePath));
            FileHandle = new FileStream(info.FilePath, FileMode.Open, FileAccess.Read);
            byte[] header = new byte[HeaderSize];
            FileHandle.Read(header);
            FileHeader = DataHelper.Bytes2Struct<Ext4FileHeader>(header, header.Length);
            RemainingChunks = (int)FileHeader.TotalChunks;
            TotalSectors = FileHeader.BlockSize * FileHeader.TotalBlocks / info.BytesPerSector;
            MaxItemCountInBuffer = maxItemCountInBuffer;
            DataBuffer = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), MaxItemCountInBuffer);
        }

        public void DataHandle()
        {
            try
            {
                byte[] headerBuffer = new byte[ChunkHeaderSize];
                while (RemainingChunks > 0 && InnerException == null)
                {
                    FileHandle.Read(headerBuffer);
                    Ext4ChunkHeader header = DataHelper.Bytes2Struct<Ext4ChunkHeader>(headerBuffer, ChunkHeaderSize);
                    int dataSize = (int)(header.ChunkSize * FileHeader.BlockSize);
                    byte[] data = new byte[dataSize];
                    if (header.Type == 0xCAC1 || header.Type == 0xCAC2 || header.Type == 0xCAC3)
                    {
                        if (header.Type == 0xCAC1)
                            FileHandle.Read(data);
                        DataBuffer.Add(data);
                    }
                    else if (header.Type == 0xCAC4)
                        FileHandle.Position += 4;

                    RemainingChunks--;
                }
            }catch(Exception e)
            {
                InnerException = e;
            }
            finally
            {
                DataBuffer.CompleteAdding();
            }
        }

        /// <summary>
        /// 写入数据到指定扇区偏移
        /// </summary>
        /// <param name="sectorOffset">扇区偏移量</param>
        /// <param name="dataArray">需要写入的全部字节数组</param>
        /// <param name="totalSize">要写入的总数据大小(字节)</param>
        /// <returns>写入后新的扇区偏移量，由原有偏移+数据所占扇区数向上取整得到</returns>
        private int WriteDataToDevice(int sectorOffset, List<byte[]> dataArray, int totalSize)
        {
            byte[] buffer = new byte[Server.MaxPayloadSizeToTarget];
            long numSectors = (int)Math.Ceiling((double)totalSize / PartitionInfo.BytesPerSector);
            long sectorsToWrite = numSectors;
            Server.Port.Write($"<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                $"<data><program physical_partition_number=\"{PartitionInfo.Lun}\" label=\"{PartitionInfo.Label}\"" +
                $" start_sector=\"{long.Parse(PartitionInfo.StartSector) + sectorOffset}\" num_partition_sectors=\"{numSectors}\" SECTOR_SIZE_IN_BYTES=\"{PartitionInfo.BytesPerSector}\"" +
                $" sparse=\"true\" /></data>");
            Server.WaitForResponse().CheckAndThrow();
            int totalWriteSize = 0;
            foreach (byte[] data in dataArray)
            {
                MemoryStream stream = new MemoryStream(data);
                while (true)
                {
                    int readSize = stream.Read(buffer);
                    if (readSize <= 0) break;
                    Server.Port.Write(buffer, 0, readSize);
                    totalWriteSize += readSize;
                }
            }
            if(totalWriteSize % PartitionInfo.BytesPerSector != 0)
            {
                byte[] fillBuffer = new byte[PartitionInfo.BytesPerSector - totalWriteSize % PartitionInfo.BytesPerSector];
                Server.Port.Write(fillBuffer, 0, fillBuffer.Length);
            }
            sectorOffset += (int)numSectors;
            ProgressChanged?.Invoke(this, (sectorOffset, TotalSectors));
            Server.WaitForResponse().CheckAndThrow();
            return sectorOffset;
        }

        private void WriteToDevice()
        {
            try
            {
                int sectorOffset = 0;
                while (!DataBuffer.IsCompleted) {
                    int totalSize = 0;
                    List<byte[]> data = new();
                    while(!DataBuffer.IsCompleted && totalSize < OnceReadSize)
                    {
                        byte[] onceData = DataBuffer.Take();
                        data.Add(onceData);
                        totalSize += onceData.Length;
                    }
                    sectorOffset = WriteDataToDevice(sectorOffset, data, totalSize);
                }
            }
            catch(Exception e)
            {
                InnerException = e;
            }
        }

        /// <summary>
        /// 开始写入操作
        /// </summary>
        public void StartWrite()
        {
            DataHandleThread = new Thread(DataHandle);
            WriteThread = new Thread(WriteToDevice);
            DataHandleThread.Start();
            WriteThread.Start();
        }

        /// <summary>
        /// <para>等待写入操作完成</para>
        /// <para>若写入过程中出现异常,将会在这一方法中抛出</para>
        /// </summary>
        public void WaitForComplete()
        {
            DataHandleThread?.Join();
            if (InnerException != null)
                throw InnerException;
            WriteThread?.Join();
            if (InnerException != null)
                throw InnerException;
        }

        /// <summary>
        /// 关闭内部文件流
        /// </summary>
        public void Dispose()
        {
            if (FileHandle != null)
                FileHandle.Close();
        }
    }
}
