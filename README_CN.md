# SharpEDL
## 概述

**警告:本项目目前处于实验性阶段,请勿在重要设备上使用**

SharpEDL是一个使用C#原生实现的高通9008通信库  
使用的运行时为.net8.0，已经在我的设备(Redmi 5 plus)上通过测试  
该项目实现的功能有:
- Sahara模式下获取信息，发送引导
- 读/写/擦分区
- 稀疏(sparse)文件刷写
- 解析分区表
- 刷写flat build刷机包,生成rawprogram文件
- 小米设备授权/免授权

## Get Started
### Sahara 读取信息
```csharp
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
```

### Sahara 发送引导
```csharp
SaharaServer server = new SaharaServer { Port = port };
server.SwitchMode(SaharaMode.ImageTxPending);
var state = server.DoHelloHandshake(SaharaMode.ImageTxPending);
FileStream stream = new FileStream("I:\\tmp\\qc\\mi_noauth_625.mbn", FileMode.Open, FileAccess.Read);
server.SendProgrammer(state.ImageTransfer, stream, (uint)stream.Length);
```

### 回读分区
```csharp
FirehoseServer Server = new FirehoseServer { Port = port };
Server.ProgressChanged += OnProgressChanged; //可选
Server.ReadbackImage(new PartitionInfo
{
    Label = "boot",
    FileSectorOffset = 0,
    StartSector = "790528",
    SectorLen = 131072,
    BytesPerSector = Server.SectorSize,
    Sparse = false,
    Lun = 0,
    FilePath = "I:\\tmp\\qc\\boot.img"
});
```

### 写分区(sparse)
```csharp
FirehoseServer Server = new FirehoseServer { Port = port };
Server.ProgressChanged += OnProgressChanged; //可选
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
Server.WriteSparseImage(info);
```

### 写分区(unsparse)
```csharp
FirehoseServer Server = new FirehoseServer { Port = port };
Server.ProgressChanged += OnProgressChanged; //可选
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
});
```

更多示例请参见Demo

## Credits
* [bkerler/edl](https://github.com/bkerler/edl)
* [openpst/libopenpst](https://github.com/openpst/libopenpst)
* [TalAloni/SparseConverter](https://github.com/TalAloni/SparseConverter)