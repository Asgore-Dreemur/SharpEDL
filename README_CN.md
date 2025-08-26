# SharpEDL
## ����

**����:����ĿĿǰ����ʵ���Խ׶�,��������Ҫ�豸��ʹ��**

SharpEDL��һ��ʹ��C#ԭ��ʵ�ֵĸ�ͨ9008ͨ�ſ�  
ʹ�õ�����ʱΪ.net8.0���Ѿ����ҵ��豸(Redmi 5 plus)��ͨ������  
����Ŀʵ�ֵĹ�����:
- Saharaģʽ�»�ȡ��Ϣ����������
- ��/д/������
- ϡ��(sparse)�ļ�ˢд
- ����������
- ˢдflat buildˢ����,����rawprogram�ļ�
- С���豸��Ȩ/����Ȩ

## Get Started
### Sahara ��ȡ��Ϣ
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

### Sahara ��������
```csharp
SaharaServer server = new SaharaServer { Port = port };
server.SwitchMode(SaharaMode.ImageTxPending);
var state = server.DoHelloHandshake(SaharaMode.ImageTxPending);
FileStream stream = new FileStream("I:\\tmp\\qc\\mi_noauth_625.mbn", FileMode.Open, FileAccess.Read);
server.SendProgrammer(state.ImageTransfer, stream, (uint)stream.Length);
```

### �ض�����
```csharp
FirehoseServer Server = new FirehoseServer { Port = port };
Server.ProgressChanged += OnProgressChanged; //��ѡ
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

### д����(sparse)
```csharp
FirehoseServer Server = new FirehoseServer { Port = port };
Server.ProgressChanged += OnProgressChanged; //��ѡ
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

### д����(unsparse)
```csharp
FirehoseServer Server = new FirehoseServer { Port = port };
Server.ProgressChanged += OnProgressChanged; //��ѡ
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

����ʾ����μ�Demo

## Credits
* [bkerler/edl](https://github.com/bkerler/edl)
* [openpst/libopenpst](https://github.com/openpst/libopenpst)
* [TalAloni/SparseConverter](https://github.com/TalAloni/SparseConverter)