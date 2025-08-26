using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using SharpEDL.DataClass;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Buffers.Binary;
using System.Numerics;

namespace SharpEDL
{
    public class SaharaServer
    {
        /// <summary>
        /// 设置或获取当前串口实例,实例化该类时必须指定
        /// </summary>
        public SerialPort Port { get; set; }

        /// <summary>
        /// (From QSaharaServer)数据缓冲区大小
        /// </summary>
        public static readonly int SaharaRawBufferSize = 0x100000;

        private byte[] RxBuffer { get; set; } = new byte[SaharaRawBufferSize];

        public SaharaServer(SerialPort port)
        {
            Port = port;
        }

        /// <summary>
        /// (unsafe)检查响应状态是否为成功
        /// </summary>
        /// <param name="header">指向响应中SaharaHeader响应头的指针</param>
        /// <param name="dataSize">该响应的非托管大小</param>
        public unsafe bool IsSuccessResponse(SaharaHeader* header, int dataSize)
        {
             return !(header->Command == (uint)SaharaCommand.EndImageTransfer && 
                    dataSize == Marshal.SizeOf(typeof(SaharaEndImageTransferResponse)) && 
                    ((SaharaEndImageTransferResponse*)&header)->Status != (uint)SaharaStatusCode.Success &&
                    ((SaharaEndImageTransferResponse*)&header)->Status < (uint)SaharaStatusCode.StatusMax);
        }

        /// <summary>
        /// (unsafe)检查响应是否符合预期
        /// </summary>
        /// <param name="expectedCommand">响应中应当被指定的指令</param>
        /// <param name="header">指向响应中SaharaHeader响应头的指针</param>
        /// <param name="dataSize">该响应的非托管大小</param>
        public unsafe bool IsValidResponse(SaharaCommand expectedCommand, SaharaHeader* header, int dataSize)
        {
            if (dataSize == 0 || !IsSuccessResponse(header, dataSize))
                return false;
            if (expectedCommand != 0 && header->Command == (uint)expectedCommand)
                return true;
            return expectedCommand == SaharaCommand.ReadData;
        }

        /// <summary>
        /// 从客户端获取Hello握手请求
        /// </summary>
        public SaharaHelloRequest ReadHello()
        {
            int structLength = Marshal.SizeOf(typeof(SaharaHelloRequest));
            Port.Read(RxBuffer, 0, structLength);
            return DataHelper.Bytes2Struct<SaharaHelloRequest>(RxBuffer, structLength);
        }

        /// <summary>
        /// 发送Hello握手响应并获取客户端对下一步操作的响应
        /// </summary>
        /// <param name="mode">欲使客户端所处的模式</param>
        /// <param name="version">Sahara版本,可从握手请求中取得</param>
        /// <param name="minVersion">支持的最小Sahara版本</param>
        /// <returns>SaharaState结构体,其中的Mode,Version与MinVersion与实参保持一致<para/>
        /// 该方法自动通过传入的<b>mode</b>实参判断客户端应当返回的响应,若响应合法则将其填充进特定的结构体成员中<para/>
        /// 具体规则如下:<para/>
        /// <list type="bullet">
        ///     <item><see cref="SaharaMode.Command"/> --> <see cref="SaharaState.ClientCommand"/></item>
        ///     <item><see cref="SaharaMode.ImageTxPending"/> --> <see cref="SaharaState.ImageTransfer"/></item>
        ///     <item><see cref="SaharaMode.MemoryDebug"/> --> <see cref="SaharaState.MemoryDebug"/></item>
        /// </list>
        /// </returns>
        public SaharaState SendHelloResponse(SaharaMode mode, uint version, uint minVersion)
        {
            SaharaHelloResponse response = new();
            SaharaState state = new();
            int structLength = Marshal.SizeOf(typeof(SaharaHelloResponse));
            response.Header.Command = (uint)SaharaCommand.HelloResponse;
            response.Header.Length = (uint)structLength;
            response.Mode = (uint)mode;
            response.Version = version;
            response.MinVersion = minVersion;

            Port.Write(DataHelper.Struct2Bytes(response), 0, structLength);
            Port.Read(RxBuffer, 0, RxBuffer.Length);

            unsafe
            {
                fixed (byte* ptr = RxBuffer) {
                    if(mode == SaharaMode.Command && IsValidResponse(SaharaCommand.Ready,
                        (SaharaHeader*)ptr, structLength))
                    {
                        Buffer.MemoryCopy(ptr, &state.ClientCommand, Marshal.SizeOf(state.ClientCommand), Marshal.SizeOf(state.ClientCommand));
                    }
                    if(mode == SaharaMode.ImageTxPending && IsValidResponse(SaharaCommand.ReadData,
                        (SaharaHeader*)ptr, structLength))
                    {
                        Buffer.MemoryCopy(ptr, &state.ImageTransfer, Marshal.SizeOf(state.ImageTransfer), Marshal.SizeOf(state.ImageTransfer));
                    }
                    if (mode == SaharaMode.MemoryDebug && IsValidResponse(SaharaCommand.MemoryDebug,
                        (SaharaHeader*)ptr, structLength))
                    {
                        Buffer.MemoryCopy(ptr, &state.MemoryDebug, Marshal.SizeOf(state.MemoryDebug), Marshal.SizeOf(state.MemoryDebug));
                    }
                }
            }
            state.Mode = (uint)mode;
            state.Version = version;
            state.MinVersion = minVersion;
            return state;
        }

        /// <summary>
        /// 进行一次Hello握手(该方法实际上为ReadHello与SendHelloResponse的整合)
        /// </summary>
        /// <param name="mode">欲使客户端所处的模式</param>
        /// <param name="minVersion">支持的最小Sahara版本</param>
        /// <param name="version">Sahara版本</param>
        /// <returns>SaharaState结构体,详见<see cref="SendHelloResponse"/>
        /// </returns>
        public SaharaState DoHelloHandshake(SaharaMode mode, uint minVersion=1, uint version=2)
        {
            SaharaHelloRequest request = ReadHello();
            return SendHelloResponse(mode, version, minVersion);
        }

        /// <summary>
        /// 按照客户端的读取请求发送镜像
        /// </summary>
        /// <param name="request">从客户端获取的SaharaReadDataRequest请求</param>
        /// <param name="dataStream">镜像文件数据流</param>
        /// <param name="dataLength">镜像文件大小(字节)</param>
        /// <exception cref="ArgumentException">请求中的数据偏移量与数据大小的总和超过dataLength的值时会抛出此异常</exception>
        public void ImageTransfer(SaharaReadDataRequest request, Stream dataStream, uint dataLength)
        {
            uint data_offset = request.Offset;
            uint data_size = request.Size;
            if(data_size == 0 || data_offset + data_size > dataLength)
            {
                throw new ArgumentException("Invalid data length", "dataLength");
            }
            dataStream.Seek(data_offset, SeekOrigin.Begin);
            int read_size = 0;
            int bytes_read = 0;
            while(bytes_read < data_size)
            {
                read_size = (int)(data_size - bytes_read < SaharaRawBufferSize ? data_size - bytes_read : SaharaRawBufferSize);
                dataStream.Read(RxBuffer, 0, read_size);
                Port.Write(RxBuffer, 0, read_size);
                bytes_read += read_size;
            }
        }

        /// <summary>
        /// 发送Done(完成)数据包
        /// </summary>
        public void SendDonePacket()
        {
            int dataLength = Marshal.SizeOf(typeof(SaharaDonePacket));
            SaharaDonePacket packet = new SaharaDonePacket();
            packet.Header.Command = (uint)SaharaCommand.Done;
            packet.Header.Length = (uint)dataLength;
            Port.Write(DataHelper.Struct2Bytes(packet), 0, dataLength);
        }

        /// <summary>
        /// 获取客户端对于Done数据包的响应
        /// </summary>
        public SaharaDonePacketResponse GetDoneResponse()
        {
            int dataLength = Marshal.SizeOf(typeof(SaharaDonePacketResponse));
            Port.Read(RxBuffer, 0, dataLength);
            unsafe
            {
                fixed(byte* ptr = RxBuffer)
                    return *(SaharaDonePacketResponse*)(ulong)ptr;
            }
        }

        /// <summary>
        /// 向客户端发送指令(客户端应处于SaharaMode.Command模式)
        /// </summary>
        /// <param name="command">欲要发送的指令</param>
        /// <returns>从客户端获取的响应</returns>
        /// <exception cref="InvalidDataException">当客户端返回不合法请求时将抛出此异常</exception>
        public byte[] SendCommand(SaharaClientCMD command)
        {
            SaharaClientCommandRequest request = new();
            SaharaExecuteDataRequest execPacket = new();
            MemoryStream stream = new MemoryStream();
            int dataLength = Marshal.SizeOf(typeof(SaharaClientCommandRequest));
            int dataSize = 0;
            request.Header.Command = (uint)SaharaCommand.Execute;
            request.Header.Length = (uint)dataLength;
            request.Command = (uint)command;
            Port.Write(DataHelper.Struct2Bytes(request), 0, dataLength);
            int readSize = Port.Read(RxBuffer, 0, RxBuffer.Length);
            unsafe {
                fixed(byte* ptr = RxBuffer)
                {
                    if (!IsValidResponse(SaharaCommand.ExecuteResponse, (SaharaHeader*)(ulong)ptr, readSize)) {
                        throw new InvalidDataException("Error from device, command " + ((SaharaHeader*)(ulong)ptr)->Command.ToString());
                    }
                    dataSize = (int)((SaharaClientCommandResponse*)(ulong)ptr)->Size;
                }
            }
            execPacket.Header.Command = (uint)SaharaCommand.ExecuteData;
            execPacket.Header.Length = (uint)dataLength;
            execPacket.Command = (uint)command;
            Port.Write(DataHelper.Struct2Bytes(execPacket), 0, dataLength);
            do
            {
                readSize = Port.Read(RxBuffer, 0, SaharaRawBufferSize);
                stream.Write(RxBuffer, 0, readSize);
            } while (stream.Length < dataSize);
            if (stream.Length == 0)
                return [];
            byte[] buffer = new byte[stream.Length];
            stream.Seek(0, SeekOrigin.Begin);
            stream.Read(buffer, 0, buffer.Length);
            stream.Close();
            return buffer;
        }

        /// <summary>
        /// 切换客户端所处模式
        /// </summary>
        /// <param name="mode">欲要切换到的模式</param>
        public void SwitchMode(SaharaMode mode)
        {
            SaharaSwitchMode packet = new();
            packet.Header.Command = (uint)SaharaCommand.SwitchMode;
            packet.Header.Length = (uint)Marshal.SizeOf(packet);
            packet.Mode = (uint)mode;
            Port.Write(DataHelper.Struct2Bytes(packet), 0, (int)packet.Header.Length);
        }

        /// <summary>
        /// 在命令模式下获取客户端的MsmHWID
        /// </summary>
        /// <returns>一个长度为16的十六进制字符串<para/>
        /// 该返回值已经过处理,处理过程如下:<para/>
        /// <list type="number">
        ///     <item>截取客户端响应的前八个字节</item>
        ///     <item>经小端序转换全部为BigInteger,再转为十六进制字符串</item>
        ///     <item>在字符串前方补0到长度为16</item>
        /// </list>
        /// </returns>
        public string GetMsmHWID()
        {
            byte[] data = SendCommand(SaharaClientCMD.MSMHWIDRead).Take(8).ToArray();
            return new BigInteger(data, isUnsigned: true, isBigEndian: false).ToString("x").PadLeft(16, '0');
        }

        /// <summary>
        /// 在命令模式下获取客户端的序列号
        /// </summary>
        public int GetSerialNum()
        {
            byte[] data = SendCommand(SaharaClientCMD.SerialNumRead);
            return BinaryPrimitives.ReadInt32LittleEndian(data);
        }

        /// <summary>
        /// 在命令模式下获取客户端的SBL Version
        /// </summary>
        public int GetSblVersion()
        {
            byte[] data = SendCommand(SaharaClientCMD.GetSblVersion);
            return BinaryPrimitives.ReadInt32LittleEndian(data);
        }

        /// <summary>
        /// 在命令模式下获取客户端的OEM PK Hash
        /// </summary>
        /// <returns>一个字符串,处理过程如下:<para/>
        /// <list type="number">
        ///     <item>将客户端响应转为小写十六进制字符串</item>
        ///     <item>丢弃字符串中的重复部分</item>
        /// </list>
        /// </returns>
        public string GetOEMPkHash()
        {
            byte[] data = SendCommand(SaharaClientCMD.OEMPkHashRead);
            string hexdata = Convert.ToHexString(data).ToLower();
            int index = hexdata.Substring(4).IndexOf(hexdata.Substring(0, 4));
            if (index != -1)
                hexdata = hexdata.Substring(0, index+4);
            return hexdata;
        }

        /// <summary>
        /// <para>向客户端发送引导(客户端应处于SaharaMode.ImageTxPending模式)</para>
        /// <para>发送成功后，客户端将处于firehose模式，此时应当和引导通信</para>
        /// </summary>
        /// <param name="request">从客户端获取的数据读取请求</param>
        /// <param name="dataStream">引导文件数据流</param>
        /// <param name="dataLength">引导文件数据总长(字节)</param>
        /// <exception cref="InvalidDataException">客户端响应不合法时会抛出此异常</exception>
        public void SendProgrammer(SaharaReadDataRequest request, Stream dataStream, uint dataLength)
        {
            ImageTransfer(request, dataStream, dataLength);
            unsafe
            {
                while (true)
                {
                    Port.Read(RxBuffer, 0, RxBuffer.Length);
                    fixed(byte* ptr = RxBuffer)
                    {
                        if(((SaharaHeader*)(ulong)ptr)->Command == (uint)SaharaCommand.ReadData)
                            ImageTransfer(*(SaharaReadDataRequest*)(ulong)ptr, dataStream, dataLength);
                        else if(((SaharaHeader*)(ulong)ptr)->Command == (uint)SaharaCommand.EndImageTransfer)
                        {
                            if(((SaharaEndImageTransferResponse*)(ulong)ptr)->Status == (uint)SaharaStatusCode.Success)
                            {
                                SendDonePacket();
                                GetDoneResponse();
                                return;
                            }
                            else
                            {
                                throw new InvalidDataException("Error from device:code " + 
                                    ((SaharaEndImageTransferResponse*)(ulong)ptr)->Status.ToString());
                            }
                        }
                        else
                        {
                            throw new InvalidDataException("Unexpected command code:" +
                                    ((SaharaHeader*)(ulong)ptr)->Command.ToString());
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 向客户端发送reset指令
        /// </summary>
        public SaharaResetResponse ResetDevice()
        {
            SaharaResetRequest resetRequest = new();
            resetRequest.Header.Command = (uint)SaharaCommand.Reset;
            resetRequest.Header.Length = 8;
            Port.Write(DataHelper.Struct2Bytes(resetRequest), 0, 8);
            Port.Read(RxBuffer, 0, RxBuffer.Length);
            return DataHelper.Bytes2Struct<SaharaResetResponse>(RxBuffer, 8);
        }
    }
}
