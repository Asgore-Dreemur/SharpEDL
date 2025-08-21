using SharpEDL;
using SharpEDL.Auth;
using SharpEDL.DataClass;
using System.IO.Ports;

namespace Demo
{
    public class Demo
    {
        public required FirehoseServer Server;

        public static void EnterFirehose(SerialPort port)
        {
            SaharaServer server = new SaharaServer { Port = port };
            server.DoHelloHandshake(SaharaMode.Command);
            string msmid = server.GetMsmHWID();
            string pkhash = server.GetOEMPkHash();
            int serial = server.GetSerialNum();
            int sblVersion = server.GetSblVersion();
            Console.WriteLine($"MSM HWID:     {msmid}\n" +
                              $"OEM PK Hash:  {pkhash}\n" +
                              $"SerialNum:    {serial}\n" +
                              $"SBL Version:  {sblVersion}");
            server.SwitchMode(SaharaMode.ImageTxPending);
            var state = server.DoHelloHandshake(SaharaMode.ImageTxPending);
            FileStream stream = new FileStream("D:\\tmp\\qc\\mi_noauth_625.mbn", FileMode.Open, FileAccess.Read);
            server.SendProgrammer(state.ImageTransfer, stream, (uint)stream.Length);
        }

        public bool MI_BypassAuth()
        {
            Console.WriteLine("Do MI noauth");
            MiAuth auth = new MiAuth { Server = Server };
            return auth.BypassAuth();
        }

        public void Readback()
        {
            Console.WriteLine("Do readback");
            DateTime time1 = DateTime.Now;
            FileStream stream = new FileStream("D:\\tmp\\qc\\boot.img", FileMode.Open, FileAccess.Write);
            Server.ReadbackImage(new PartitionInfo
            {
                Label = "boot",
                FileSectorOffset = 0,
                StartSector = "790528",
                SectorLen = 131072,
                BytesPerSector = Server.SectorSize,
                Sparse = false,
                Lun = 0,
                FilePath = "D:\\tmp\\qc\\boot.img"
            }, stream);
            DateTime time2 = DateTime.Now;
            Console.WriteLine("Total seconds: " + (time2 - time1).TotalSeconds);
        }

        public void WriteUnSparseImage()
        {
            Console.WriteLine("Do write unsparse image");
            DateTime time1 = DateTime.Now;
            FileStream stream = new FileStream("D:\\tmp\\qc\\boot.img", FileMode.Open, FileAccess.Read);
            Server.WriteUnsparseImage(new PartitionInfo
            {
                Label = "boot",
                FileSectorOffset = 0,
                StartSector = "790528",
                SectorLen = 131072,
                BytesPerSector = Server.SectorSize,
                Sparse = false,
                Lun = 0,
                FilePath = "I:\\tmp\\qc\\boot.img"
            }, stream);
            DateTime time2 = DateTime.Now;
            Console.WriteLine("Total seconds: " + (time2 - time1).TotalSeconds);
        }

        public void WriteSparseImage()
        {
            Console.WriteLine("Do write sparse image");
            DateTime time1 = DateTime.Now;
            FileStream stream = new FileStream("D:\\tmp\\qc\\system.img", FileMode.Open, FileAccess.Read);
            PartitionInfo info = new PartitionInfo
            {
                Label = "system",
                FileSectorOffset = 0,
                StartSector = "1054720",
                SectorLen = 6291456,
                BytesPerSector = Server.SectorSize,
                Sparse = true,
                Lun = 0,
                FilePath = "I:\\tmp\\qc\\system.img"
            };
            Server.WriteSparseImage(info, stream);
            DateTime time2 = DateTime.Now;
            Console.WriteLine("Total seconds: " + (time2 - time1).TotalSeconds);
        }

        public void ErasePartition()
        {
            Console.WriteLine("Do erase partition");
            DateTime time1 = DateTime.Now;
            PartitionInfo info = new PartitionInfo
            {
                Label = "boot",
                FileSectorOffset = 0,
                StartSector = "790528",
                SectorLen = 131072,
                BytesPerSector = Server.SectorSize,
                Sparse = false,
                Lun = 0
            };
            var resp = Server.ErasePartition(info);
            Console.WriteLine(resp.Response);
            DateTime time2 = DateTime.Now;
            Console.WriteLine("Total seconds: " + (time2 - time1).TotalSeconds);
        }

        public void ReadGPT()
        {
            Console.WriteLine("Do read GPT");
            DateTime time1 = DateTime.Now;
            foreach (var item in Server.GetPartitionsFromDevice(true))
            {
                Console.WriteLine($"Label:           {item.Label}\n" +
                                  $"Start Sector:    {item.StartSector}\n" +
                                  $"Sector Len:      {item.SectorLen}\n" +
                                  $"BytesPerSector:  {item.BytesPerSector}\n" +
                                  $"Sparse:          {item.Sparse}");
            }
            DateTime time2 = DateTime.Now;
            Console.WriteLine("Total seconds: " + (time2 - time1).TotalSeconds);
        }

        public void FlashProgram()
        {
            Console.WriteLine("Do flash program");
            ProgramFlasher flasher = new ProgramFlasher(Server, "D:\\tmp\\vince_images_V11.0.3.0.OEGCNXM_20191118.0000.00_8.1_cn\\images");
            flasher.ProgressChanged += (sender, e) => Console.WriteLine(e);
            flasher.BypassPartitions.Add("userdata", -1); //Bypass userdata
            flasher.BypassPartitions.Add("modem", -1); //Bypass modem
            DateTime time1 = DateTime.Now;
            flasher.FlashPartitions();
            DateTime time2 = DateTime.Now;
            Console.WriteLine("Total seconds: " + (time2 - time1).TotalSeconds);
        }
    }
    
    class Program
    {
        static void OnProgressChanged(object? sender, (long, long) e)
        {
            Console.WriteLine(e);
        }

        static void Main(string[] args)
        {
            SerialPort port = new SerialPort("COM3");
            port.BaudRate = 115200;
            port.Open();

            Demo.EnterFirehose(port);
            FirehoseServer server = new FirehoseServer { Port = port };
            Demo demo = new Demo { Server = server };
            demo.MI_BypassAuth();
            server.GetDeviceConfig();
            server.ProgressChanged += OnProgressChanged;
            demo.WriteSparseImage();
            demo.Readback();
            demo.WriteUnSparseImage();
            demo.ErasePartition();
            server.ProgressChanged -= OnProgressChanged;
            demo.FlashProgram();
            server.ResetDevice();
            port.Close();
        }
    }
}