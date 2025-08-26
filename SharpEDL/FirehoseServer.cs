using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using SharpEDL.DataClass;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;

namespace SharpEDL
{
    public class FirehoseServer
    {
        /// <summary>
        /// 设置或获取当前串口实例,实例化该类时必须指定
        /// </summary>
        public SerialPort Port { get; set; }

        /// <summary>
        /// 设置或获取字库类型,默认为UFS<para/>
        /// 一般情况下不应直接设置此属性,可调用<see cref="GetDeviceConfig"/>获取设备配置
        /// </summary>
        public string MemoryName { get; set; } = "UFS";
        public int MaxPayloadSizeToTarget { get; set; } = 1048576;
        public int MaxPayloadSizeFromTarget { get; set; } = 1048576;
        public string TargetName { get; set; } = "unknown";

        /// <summary>
        /// 设置或获取扇区大小,默认为4096<para/>
        /// 一般情况下不应直接设置此属性,可调用<see cref="GetDeviceConfig"/>获取设备配置<para/>
        /// 在eMMC字库中,此值一般为512,UFS上一般为4096
        /// </summary>
        public int SectorSize { get; set; } = 4096;

        /// <summary>
        /// <para>进度改变通知器</para>
        /// <para>元组中第一个元素为当前操作已经完成的进度，第二个元素为该操作的总进度</para>
        /// </summary>
        public event EventHandler<(long, long)>? ProgressChanged;

        public int MaxSparseDataSizeToDevice { get; set; } = 1024 * 1024 * 128;

        public FirehoseServer(SerialPort port)
        {
            Port = port;
        }

        /// <summary>
        /// 一次性全部读取缓冲区中所有数据,若无数据则会阻塞
        /// </summary>
        /// <returns>读取到的数据</returns>
        public byte[] ReadFromDevice()
        {
            
            int firstByte = Port.ReadByte();
            byte[] buffer = new byte[Port.BytesToRead+1];
            buffer[0] = (byte)firstByte;
            if(buffer.Length > 1)
                Port.Read(buffer, 1, buffer.Length-1);
            return buffer;
        }

        /// <summary>
        /// <para>将给定的字节数组分割为多个xml字符串</para>
        /// <para>该方法将从头部开始分割,直到读取到非xml数据停止</para>
        /// <para>剩余的非xml部分将会被忽略</para>
        /// </summary>
        /// <param name="content">欲要分割的字节数组</param>
        /// <returns>分割出的xml字符串</returns>
        public static List<string> SplitXMLFromBytes(byte[] content)
        {
            List<string> result = new List<string> ();
            int index = 0, endIndex = 0;
            string xmlHeader = "<?xml", xmlEnd = "</data>";
            ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(content);
            while (index < content.Length)
            {
                ReadOnlySpan<byte> sliceData = span.Slice(index);
                int headerIndex = DataHelper.FindInBytes(sliceData, Encoding.UTF8.GetBytes(xmlHeader));
                if (headerIndex != 0)
                    break;
                endIndex = DataHelper.FindInBytes(sliceData, Encoding.UTF8.GetBytes(xmlEnd));
                if (endIndex == -1)
                    break;
                string xml = Encoding.UTF8.GetString(sliceData.Slice(0, endIndex + xmlEnd.Length).ToArray());
                result.Add(xml);
                index += endIndex + xmlEnd.Length;
            }
            return result;
        }

        /// <summary>
        /// 阻塞等待从客户端获取到响应
        /// </summary>
        /// <param name="bytesToRead">指定从客户端读取的字节数,若指定为-1则读取缓冲区中所有数据</param>
        public QCResponse WaitForResponse(int bytesToRead = -1)
        {
            QCResponse response = new() { Response = "ACK" };
            bool responseFound = false;
            while (true)
            {
                byte[] data;
                if (bytesToRead == -1)
                    data = ReadFromDevice();
                else
                {
                    byte[] buffer = new byte[bytesToRead];
                    int readSize = Port.Read(buffer, 0, buffer.Length);
                    data = new byte[readSize];
                    Array.Copy(buffer, data, data.Length);
                }
                List<string> xmls = SplitXMLFromBytes(data);
                foreach(var item in xmls)
                {
                    string nonHeaderItem = item.Substring(item.IndexOf("<data>"));
                    if (nonHeaderItem.Contains("<response value="))
                    {
                        Match match = Regex.Match(nonHeaderItem, @"<response value=""([\S]+)""");
                        if (!match.Success) continue;
                        response.Response = match.Groups[1].Value;
                        MatchCollection collection = Regex.Matches(nonHeaderItem, @"([\S]+)=""([\S]+)""");
                        foreach(Match property in collection)
                        {
                            if (property.Groups[1].Value == "value")
                                continue;
                            response.ResponseProperites.Add(property.Groups[1].Value,
                                property.Groups[2].Value);
                        }
                        responseFound = true;
                        break;
                    }
                    else if(nonHeaderItem.Contains("log value="))
                    {
                        Match match = Regex.Match(nonHeaderItem, @"log value=""([^""]+)""");
                        if (!match.Success) continue;
                        response.Logs.Add(match.Groups[1].Value);
                    }
                }
                if (responseFound)
                {
                    int allXmlsLength = Encoding.UTF8.GetByteCount(string.Join("", xmls));
                    if (allXmlsLength == data.Length)
                        response.UnhandledData = Array.Empty<byte>();
                    else
                        response.UnhandledData = new ReadOnlySpan<byte>(data).Slice(allXmlsLength).ToArray();
                    break;
                }
            }
            return response;
        }

        /// <summary>
        /// 读取客户端配置
        /// </summary>
        /// <returns>
        /// <para>客户端响应</para>
        /// <para>同时,若响应中存在对应字段,
        /// <see cref="MemoryName"/>, <see cref="MaxPayloadSizeFromTarget"/>,
        /// <see cref="MaxPayloadSizeToTarget"/>, <see cref="TargetName"/>,
        /// <see cref="SectorSize"/>将被设置</para>
        /// </returns>
        public QCResponse GetDeviceConfig()
        {
            Port.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" ?><data><configure MemoryName=\"ufs\" Verbose=\"0\" AlwaysValidate=\"0\" MaxPayloadSizeToTargetInBytes=\"1048576\" ZlpAwareHost=\"1\" SkipStorageInit=\"0\" /></data>");
            QCResponse response = WaitForResponse();
            if (response.Response != "ACK")
                return response;
            if (response.ResponseProperites.ContainsKey("MemoryName"))
            {
                MemoryName = response.ResponseProperites["MemoryName"];
                SectorSize = MemoryName == "eMMC" ? 512 : 4096;
            }
            if (response.ResponseProperites.ContainsKey("MaxPayloadSizeFromTargetInBytes"))
                MaxPayloadSizeFromTarget = int.Parse(response.ResponseProperites["MaxPayloadSizeFromTargetInBytes"]);

            if (response.ResponseProperites.ContainsKey("MaxPayloadSizeToTargetInBytesSupported"))
                MaxPayloadSizeToTarget = int.Parse(response.ResponseProperites["MaxPayloadSizeToTargetInBytesSupported"]);

            if (response.ResponseProperites.ContainsKey("TargetName"))
                TargetName = response.ResponseProperites["TargetName"];

            return response;
        }

        private QCResponse CommonStreamMethod(PartitionInfo info, Func<PartitionInfo, Stream, QCResponse> func, 
            FileMode mode = FileMode.Open)
        {
            if (string.IsNullOrEmpty(info.FilePath))
                throw new ArgumentNullException(nameof(info.FilePath));
            FileStream stream = new FileStream(info.FilePath, mode, FileAccess.ReadWrite);
            try
            {
                var response = func(info, stream);
                stream.Close();
                return response;
            }
            catch (Exception)
            {
                stream.Close();
                throw;
            }
        }

        /// <summary>
        /// 回读分区，可通过<see cref="ProgressChanged"/>事件监听进度
        /// </summary>
        /// <param name="info">分区信息</param>
        /// <param name="stream">用于存储数据的流</param>
        public QCResponse ReadbackImage(PartitionInfo info, Stream stream)
        {
            byte[] buffer = new byte[MaxPayloadSizeFromTarget];
            Port.Write($"<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                $"<data><read physical_partition_number=\"{info.Lun}\"" +
                $" label=\"{info.Label}\" start_sector=\"{info.StartSector}\" num_partition_sectors=\"{info.SectorLen}\"" +
                $" SECTOR_SIZE_IN_BYTES=\"{info.BytesPerSector}\" /></data>");
            long totalSize = info.SectorLen * info.BytesPerSector;
            long bytesNeedRead = totalSize;
            QCResponse response = WaitForResponse(8192);
            if (response.Response != "ACK")
            {
                stream.Close();
                return response;
            }
            if (response.UnhandledData.Length > 0)
            {
                stream.Write(response.UnhandledData, 0, response.UnhandledData.Length);
                bytesNeedRead -= response.UnhandledData.Length;
            }
            while (bytesNeedRead > 0)
            {
                int readSize = (int)(bytesNeedRead > MaxPayloadSizeFromTarget ? MaxPayloadSizeFromTarget : bytesNeedRead);
                readSize = Port.Read(buffer, 0, readSize);
                stream.Write(buffer, 0, readSize);
                bytesNeedRead -= readSize;
                ProgressChanged?.Invoke(this, (totalSize - bytesNeedRead, totalSize));
            }
            stream.Close();
            return WaitForResponse();
        }

        /// <summary>
        /// 回读分区，可通过<see cref="ProgressChanged"/>事件监听进度
        /// </summary>
        /// <param name="info">分区信息,必须指定<see cref="PartitionInfo.FilePath"/>为镜像路径</param>
        /// <param name="stream">用于存储数据的流</param>
        public QCResponse ReadbackImage(PartitionInfo info) => CommonStreamMethod(info, ReadbackImage, FileMode.Create);

        /// <summary>
        /// 写入稀疏文件，可通过<see cref="ProgressChanged"/>事件监听进度
        /// </summary>
        /// <param name="info">分区信息</param>
        /// <param name="stream">用于存储稀疏文件数据的流</param>
        public QCResponse WriteSparseImage(PartitionInfo info, Stream stream)
        {
            SparseStream sparseStream = new SparseStream(stream);

            long numSectors = (long)Math.Ceiling((double)sparseStream.Length / info.BytesPerSector);
            long totalSize = numSectors * info.BytesPerSector;
            long wroteSize = 0;

            Port.Write($"<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                    $"<data><program physical_partition_number=\"{info.Lun}\" label=\"{info.Label}\"" +
                    $" start_sector=\"{long.Parse(info.StartSector)}\" num_partition_sectors=\"{numSectors}\" SECTOR_SIZE_IN_BYTES=\"{info.BytesPerSector}\"" +
                    $" sparse=\"true\" /></data>");
            var response = WaitForResponse();
            if (response.Response != "ACK")
                return response;

            byte[] buffer = new byte[MaxPayloadSizeToTarget];
            while (true)
            {
                int readSize = sparseStream.Read(buffer, 0, buffer.Length);
                if (readSize <= 0)
                    break; 
                Port.Write(buffer, 0, readSize);
                wroteSize += readSize;
                ProgressChanged?.Invoke(this, (wroteSize, totalSize));
            }

            if (wroteSize % info.BytesPerSector != 0)
            {
                byte[] fillBuffer = new byte[info.BytesPerSector - wroteSize % info.BytesPerSector];
                Port.Write(fillBuffer, 0, fillBuffer.Length);
            }
            return WaitForResponse();
        }

        /// <summary>
        /// 写入稀疏文件，可通过<see cref="ProgressChanged"/>事件监听进度
        /// </summary>
        /// <param name="info">分区信息，必须指定<see cref="PartitionInfo.FilePath"/>为镜像路径</param>
        /// <returns></returns>
        public QCResponse WriteSparseImage(PartitionInfo info) => CommonStreamMethod(info, WriteSparseImage);

        /// <summary>
        /// 写入非稀疏文件，可通过<see cref="ProgressChanged"/>事件监听进度
        /// </summary>
        /// <param name="info">分区信息</param>
        /// <param name="stream">存储文件数据的流</param>
        public QCResponse WriteUnsparseImage(PartitionInfo info, Stream stream)
        {
            stream.Seek(info.FileSectorOffset * info.BytesPerSector, SeekOrigin.Begin);

            long fileSize = stream.Length - info.FileSectorOffset * info.BytesPerSector;
            long numSectors = (long)Math.Ceiling((double)fileSize / info.BytesPerSector);
            long sectorsToWrite = numSectors;

            byte[] buffer = new byte[MaxPayloadSizeToTarget];
            Port.Write($"<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                $"<data><program physical_partition_number=\"{info.Lun}\" label=\"{info.Label}\"" +
                $" start_sector=\"{info.StartSector}\" num_partition_sectors=\"{numSectors}\" SECTOR_SIZE_IN_BYTES=\"{info.BytesPerSector}\"" +
                $" sparse=\"false\" /></data>");
            QCResponse response = WaitForResponse();
            if (response.Response != "ACK")
            {
                stream.Close();
                return response;
            }
            while (sectorsToWrite > 0)
            {
                int readSize = stream.Read(buffer, 0, buffer.Length);
                long readSectorLen = readSize / info.BytesPerSector;
                byte[] writeBuffer = buffer;
                if (readSize % info.BytesPerSector != 0)
                {
                    readSectorLen++;
                    long fillSize = readSectorLen * info.BytesPerSector - readSize;
                    writeBuffer = buffer.Concat(new byte[fillSize]).ToArray();
                }
                Port.Write(writeBuffer, 0, (int)readSectorLen * info.BytesPerSector);
                sectorsToWrite -= readSectorLen;
                ProgressChanged?.Invoke(this, (numSectors - sectorsToWrite, numSectors));
            }
            stream.Close();
            return WaitForResponse();
        }

        /// <summary>
        /// 写入非稀疏文件，可通过<see cref="ProgressChanged"/>事件监听进度
        /// </summary>
        /// <param name="info">分区信息,必须指定<see cref="PartitionInfo.FilePath"/>为镜像路径</param>
        public QCResponse WriteUnsparseImage(PartitionInfo info) => CommonStreamMethod(info, WriteUnsparseImage);

        /// <summary>
        /// 擦除指定分区
        /// </summary>
        /// <param name="info">要被擦除的分区信息</param>
        public QCResponse ErasePartition(PartitionInfo info)
        {
            Port.Write($"<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                $"<data><erase physical_partition_number=\"{info.Lun}\" label=\"{info.Label}\"" +
                $" start_sector=\"{info.StartSector}\" num_partition_sectors=\"{info.SectorLen}\" SECTOR_SIZE_IN_BYTES=\"{info.BytesPerSector}\"/>" +
                $" </data>");
            return WaitForResponse();
        }

        /// <summary>
        /// 解析给定的GPT分区表数据
        /// </summary>
        /// <param name="gptData">分区表数据(包括保护MBR)</param>
        /// <param name="lun">该分区表的LUN号</param>
        /// <param name="includesPGPT">是否将主分区表(PrimaryGPT)与备份分区表(BackupGPT)添加到返回中</param>
        public List<PartitionInfo> ParseGPTTable(byte[] gptData, int lun, bool includesPGPT = false)
        {
            List<PartitionInfo > partitions = new List<PartitionInfo>();
            byte[] readData = new ReadOnlySpan<byte>(gptData).Slice(SectorSize).ToArray();
            EFIHeader header = DataHelper.Bytes2Struct<EFIHeader>(readData, readData.Length);
            if (new string(header.Magic) != "EFI PART")
                return partitions;
            for(int i = 0; i < header.GPTMemberCount; i++)
            {
                int index = (int)header.GPTTableStartSector * SectorSize + i * header.SizePerTableMember;
                byte[] tableData = new ReadOnlySpan<byte>(gptData).Slice(index).ToArray();
                GPTTableMember table = DataHelper.Bytes2Struct<GPTTableMember>(tableData, header.SizePerTableMember);
                if (table.EndSector - table.StartSector <= 0)
                    break;
                partitions.Add(new PartitionInfo
                {
                    Label = Encoding.Unicode.GetString(table.Label).Replace("\0", "").Trim(),
                    StartSector = table.StartSector.ToString(),
                    Lun = lun,
                    SectorLen = table.EndSector - table.StartSector + 1,
                    BytesPerSector = SectorSize
                });
            }
            if (includesPGPT)
            {
                long sectorLen = MemoryName == "eMMC" ? 34 : 6;
                partitions.Add(new PartitionInfo
                {
                    Label = "PrimaryGPT",
                    StartSector = "0",
                    SectorLen = sectorLen,
                    Lun = lun,
                    BytesPerSector = SectorSize
                });
                partitions.Add(new PartitionInfo
                {
                    Label = "BackupGPT",
                    StartSector = $"NUM_DISK_SECTORS-{sectorLen-1}.",
                    SectorLen = sectorLen-1,
                    Lun = lun,
                    BytesPerSector = SectorSize
                });
            }
            return partitions;
        }

        /// <summary>
        /// 根据给定的LUN号从客户端获取分区表并解析出分区信息
        /// </summary>
        /// <param name="lun">LUN号</param>
        /// <param name="includesGPT">是否将主分区表(PrimaryGPT)与备份分区表(BackupGPT)添加到返回中</param>
        public List<PartitionInfo> GetPartitionsFromDeviceByLUN(int lun, bool includesGPT = false)
        {
            List<PartitionInfo> partitions = new List<PartitionInfo>();
            int gptSectors = 33;
            Port.Write($"<?xml version=\"1.0\" encoding=\"UTF-8\" ?>" +
                $"<data><read physical_partition_number=\"{lun}\" label=\"PrimaryGPT\"" +
                $" start_sector=\"0\" num_partition_sectors=\"{gptSectors}\" SECTOR_SIZE_IN_BYTES=\"{SectorSize}\"/>" +
                $"</data>");
            QCResponse response = WaitForResponse(8192);
            response.CheckAndThrow();
            byte[] buffer = new byte[gptSectors * SectorSize];
            Array.Copy(response.UnhandledData, buffer, response.UnhandledData.Length);
            Port.Read(buffer, response.UnhandledData.Length, gptSectors * SectorSize - response.UnhandledData.Length);
            WaitForResponse().CheckAndThrow();
            return ParseGPTTable(buffer, lun, includesGPT);
        }

        /// <summary>
        /// <para>从客户端获取所有分区信息</para>
        /// <para>该方法将从lun=0开始获取,直到设备返回错误响应或其他异常</para>
        /// </summary>
        /// <param name="includesGPT">是否将主分区表(PrimaryGPT)与备份分区表(BackupGPT)添加到返回中</param>
        public List<PartitionInfo> GetPartitionsFromDevice(bool includesGPT = false)
        {
            List<PartitionInfo> partitions = new List<PartitionInfo>();
            int lun = 0;
            while (true)
            {
                try
                {
                    partitions = partitions.Concat(GetPartitionsFromDeviceByLUN(lun, includesGPT)).ToList();
                    lun++;
                }
                catch (Exception)
                {
                    break;
                }
            }
            return partitions;
        }

        /// <summary>
        /// <para>向客户端发送patch</para>
        /// <para>正常情况下,不应向客户端发送<see cref="PatchInfo.FileName"/>不为<b>DISK</b>的patch</para>
        /// </summary>
        /// <param name="info">patch信息</param>
        public QCResponse SendPatch(PatchInfo info)
        {
            Port.Write($"<?xml version=\"1.0\" ?><data><patch SECTOR_SIZE_IN_BYTES=\"{info.SectorSize}\" byte_offset=\"{info.ByteOffset}\" " +
                $"filename=\"{info.FileName}\" physical_partition_number=\"{info.Lun}\" size_in_bytes=\"{info.SizeInBytes}\" " +
                $"start_sector=\"{info.StartSector}\" value=\"{info.Value}\" " +
                $"what=\"{info.What}\"/></data>");
            return WaitForResponse();
        }

        /// <summary>
        /// 向客户端发送reset指令
        /// </summary>
        /// <param name="dealyInSeconds">延迟时间,默认为0即立即执行</param>
        /// <param name="value">电源操作类型,默认为<b>reset</b>即重启</param>
        public QCResponse ResetDevice(int dealyInSeconds = 0, string value = "reset")
        {
            Port.Write($"<?xml version=\"1.0\" encoding=\"UTF-8\" ?><data><power DelayInSeconds=\"{dealyInSeconds}\" value=\"{value}\"  /></data>");
            return WaitForResponse();
        }
    }
}
