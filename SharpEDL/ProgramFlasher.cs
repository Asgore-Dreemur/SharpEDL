using SharpEDL.DataClass;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace SharpEDL
{
    /// <summary>
    /// 处理flat build刷机包刷入
    /// </summary>
    public class ProgramFlasher
    {
        public FirehoseServer Server { get; set; }
        public string ProgramPath { get; set; }

        /// <summary>
        /// 从刷机包rawprogram文件中获取到的分区信息
        /// </summary>
        public List<PartitionInfo> Partitions { get; set; } = new List<PartitionInfo>();

        /// <summary>
        /// 从刷机包patch文件中获取到的patch信息
        /// </summary>
        public List<PatchInfo> Patches { get; set; } = new List<PatchInfo>();

        /// <summary>
        /// <para>指定应当跳过刷入的分区</para>
        /// <para>元组中，第一个元素为分区名，第二个元素为lun号(指定为-1以跳过所有lun中该分区的刷写)</para>
        /// </summary>
        public List<(string, int)> BypassPartitions { get; set; } = new List<(string, int)>();

        /// <summary>
        /// 参见<see cref="SparseWriter.MaxItemCountInBuffer"/>
        /// </summary>
        public int MaxItemInSparseBuffer = 1024;

        /// <summary>
        /// <para>进度改变通知器</para>
        /// <para>元组中第一个元素为当前正在处理的步骤(若在刷入分区则为分区名，应用patch时固定为<b>Patch</b></para>
        /// <para>第二个元素为一个数组，其中第一个元素表示当前操作已完成进度，第二个表示当前操作的全部进度</para>
        /// </summary>
        public event EventHandler<(string?, (long, long))>? ProgressChanged;

        private string? CurrentStep;

        /// <summary>
        /// <para>初始化该类的实例</para>
        /// <para>此方法将根据给定的刷机包路径自动获取分区信息与patch信息</para>
        /// </summary>
        /// <param name="server"><see cref="FirehoseServer"/>类的实例</param>
        /// <param name="programPath">刷机包路径</param>
        public ProgramFlasher(FirehoseServer server, string programPath)
        {
            Server = server;
            ProgramPath = programPath;
            Partitions = ParseProgramFiles(programPath, 
                Directory.GetFiles(programPath).Where(name => Regex.IsMatch(name, @"rawprogram[\d]+.xml")).ToArray());
            Patches = ParsePatchFiles(Directory.GetFiles(programPath)
                .Where(name => Regex.IsMatch(name, @"patch[\d]+.xml")).ToArray());
        }

        /// <summary>
        /// 解析刷机包中的rawprogram文件
        /// </summary>
        /// <param name="programPath">刷机包路径</param>
        /// <param name="programFiles">所有需要解析的rawprogram文件</param>
        /// <returns>
        /// <para>解析出的所有分区信息</para>
        /// <para>对于<b>filename</b>非空的条目,将设定<see cref="PartitionInfo.FilePath"/>为分区镜像路径</para>
        /// </returns>
        public static List<PartitionInfo> ParseProgramFiles(string programPath, string[] programFiles)
        {
            List<PartitionInfo> partitions = new List<PartitionInfo>();
            foreach(var programFile in programFiles)
            {
                string content = File.ReadAllText(programFile);
                XmlDocument document = new XmlDocument();
                document.LoadXml(content);
                XmlNode? dataNode = document.SelectSingleNode("data");
                if (dataNode == null) continue;
                XmlNodeList? nodeList = dataNode.SelectNodes("program");
                if(nodeList == null || nodeList.Count == 0) continue;
                foreach(XmlNode node in nodeList)
                {
                    PartitionInfo info = new PartitionInfo { Label = "", StartSector = "" };
                    string? SectorSizeStr = node.Attributes?.GetNamedItem("SECTOR_SIZE_IN_BYTES")?.Value;
                    string? Label = node.Attributes?.GetNamedItem("label")?.Value;
                    string? FileSectorOffsetStr = node.Attributes?.GetNamedItem("file_sector_offset")?.Value;
                    string? StartSector = node.Attributes?.GetNamedItem("start_sector")?.Value;
                    string? SectorLenStr = node.Attributes?.GetNamedItem("num_partition_sectors")?.Value;
                    string? LunStr = node.Attributes?.GetNamedItem("physical_partition_number")?.Value;
                    string? SparseStr = node.Attributes?.GetNamedItem("sparse")?.Value;
                    string? Filename = node.Attributes?.GetNamedItem("filename")?.Value;
                    if (string.IsNullOrEmpty(SectorSizeStr) || string.IsNullOrEmpty(Label) ||
                        string.IsNullOrEmpty(StartSector) || string.IsNullOrEmpty(SectorLenStr) ||
                        string.IsNullOrEmpty(LunStr))
                        continue;
                    info.BytesPerSector = int.Parse(SectorSizeStr);
                    info.Label = Label;
                    info.FileSectorOffset = string.IsNullOrEmpty(FileSectorOffsetStr) ? 0 : int.Parse(FileSectorOffsetStr);
                    info.StartSector = StartSector;
                    info.SectorLen = long.Parse(SectorLenStr);
                    info.Lun = int.Parse(LunStr);
                    info.Sparse = !string.IsNullOrEmpty(SparseStr) && SparseStr == "true" ? true : false;
                    if (!string.IsNullOrEmpty(Filename))
                        info.FilePath = Path.Combine(programPath, Filename);
                    partitions.Add(info);
                }
            }
            return partitions;
        }

        /// <summary>
        /// 解析刷机包中的patch文件
        /// </summary>
        /// <param name="patchFiles">所有需要解析的patch文件</param>
        /// <param name="includesNonDiskPath">是否在返回结果中包含<b>filename</b>不为DISK的条目</param>
        /// <returns>解析出的所有patch信息</returns>
        public static List<PatchInfo> ParsePatchFiles(string[] patchFiles, bool includesNonDiskPath = false)
        {
            List<PatchInfo> patches = new List<PatchInfo>();
            foreach (var patchFile in patchFiles)
            {
                string content = File.ReadAllText(patchFile);
                XmlDocument document = new XmlDocument();
                document.LoadXml(content);
                XmlNode? dataNode = document.SelectSingleNode("patches");
                if (dataNode == null) continue;
                XmlNodeList? nodeList = dataNode.SelectNodes("patch");
                if (nodeList == null || nodeList.Count == 0) continue;
                foreach (XmlNode node in nodeList)
                {
                    PatchInfo info = new PatchInfo();
                    string? sectorSizeStr = node.Attributes?.GetNamedItem("SECTOR_SIZE_IN_BYTES")?.Value;
                    string? fileName = node.Attributes?.GetNamedItem("filename")?.Value;
                    string? byteOffsetStr = node.Attributes?.GetNamedItem("byte_offset")?.Value;
                    string? startSector = node.Attributes?.GetNamedItem("start_sector")?.Value;
                    string? lunStr = node.Attributes?.GetNamedItem("physical_partition_number")?.Value;
                    string? value = node.Attributes?.GetNamedItem("value")?.Value;
                    string? sizeInBytesStr = node.Attributes?.GetNamedItem("size_in_bytes")?.Value;
                    string? what = node.Attributes?.GetNamedItem("what")?.Value;
                    if (string.IsNullOrEmpty(sectorSizeStr) || string.IsNullOrEmpty(fileName) ||
                        string.IsNullOrEmpty(byteOffsetStr) || string.IsNullOrEmpty(value) ||
                        string.IsNullOrEmpty(lunStr) || string.IsNullOrEmpty(sizeInBytesStr))
                        continue;
                    info.ByteOffset = int.Parse(byteOffsetStr);
                    info.Value = value;
                    info.SectorSize = int.Parse(sectorSizeStr);
                    info.FileName = fileName;
                    info.StartSector = startSector;
                    info.Lun = int.Parse(lunStr);
                    info.What = what;
                    info.SizeInBytes = int.Parse(sizeInBytesStr);
                    if (info.FileName != "DISK" && !includesNonDiskPath)
                        continue;
                    patches.Add(info);
                }
            }
            return patches;
        }

        /// <summary>
        /// 为给定的分区生成rawprogram
        /// </summary>
        /// <param name="partitions">分区信息</param>
        /// <param name="defaultNonSparse">是否默认指定sparse属性为false</param>
        /// <returns>rawprogram字符串</returns>
        public static string GenerateRawprogram(List<PartitionInfo> partitions, bool defaultNonSparse = true)
        {
            XElement root = new XElement("data");
            foreach(var item in partitions)
            {
                XElement element = new XElement("program");
                element.SetAttributeValue("SECTOR_SIZE_IN_BYTES", item.BytesPerSector);
                element.SetAttributeValue("file_sector_offset", item.FileSectorOffset);
                element.SetAttributeValue("filename", !string.IsNullOrEmpty(item.FilePath) ? Path.GetFileName(item.FilePath) : "");
                element.SetAttributeValue("label", item.Label);
                element.SetAttributeValue("num_partition_sectors", item.SectorLen);
                element.SetAttributeValue("physical_partition_number", item.Lun);
                element.SetAttributeValue("sparse", defaultNonSparse ? "false" : item.Sparse ? "true" : "false");
                if (item.Label == "BackupGPT")
                    element.SetAttributeValue("start_byte_hex", $"({item.BytesPerSector}*NUM_DISK_SECTORS)-{item.BytesPerSector * item.SectorLen}.");
                else
                    element.SetAttributeValue("start_byte_hex", $"0x{int.Parse(item.StartSector):x8}");
                    element.SetAttributeValue("start_sector", item.StartSector);
                root.Add(element);
            }
            XDocument doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\n" + doc.ToString();
        }

        private void OnProgressChanged(object? sender, (long, long) progress)
        {
            ProgressChanged?.Invoke(this, (CurrentStep, progress));
        }

        /// <summary>
        /// 刷入分区并应用patch
        /// </summary>
        /// <returns>最后一次操作中客户端的响应</returns>
        public QCResponse FlashPartitions()
        {
            QCResponse response = new QCResponse { Response = "ACK" };
            Server.ProgressChanged += OnProgressChanged;
            try
            {
                foreach (var item in Partitions)
                {
                    if (BypassPartitions.Find(partition => 
                                item.Label == partition.Item1 && (partition.Item2 == -1 || partition.Item2 == item.Lun)) != default)
                        continue;
                    if (string.IsNullOrEmpty(item.FilePath))
                        continue;
                    CurrentStep = item.Label;
                    OnProgressChanged(this, (0, 1));
                    if (item.Sparse)
                        response = Server.WriteSparseImage(item);
                    else
                        response = Server.WriteUnsparseImage(item);
                }
                CurrentStep = "Patch";
                OnProgressChanged(this, (0, Patches.Count));
                foreach (var item in Patches)
                {
                    if (item.FileName != "DISK")
                        continue;
                    OnProgressChanged(this, (Patches.IndexOf(item)+1, Patches.Count));
                    response = Server.SendPatch(item);
                    if (response.Response != "ACK")
                        return response;
                }
                Server.ProgressChanged -= OnProgressChanged;
                return response;
            }
            catch (Exception)
            {
                Server.ProgressChanged -= OnProgressChanged;
                throw;
            }
        }
    }
}
