using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpEDL.DataClass
{
    public enum SaharaCommand
    {
         Hello = 0x01,
         HelloResponse = 0x02,
         ReadData = 0x03,
         EndImageTransfer = 0x04,
         Done = 0x05,
         DoneResponse = 0x06,
         Reset = 0x07,
         ResetResponse = 0x08,
         MemoryDebug = 0x09,
         MemoryRead = 0x0A,
         Ready = 0x0B,
         SwitchMode = 0x0C,
         Execute = 0x0D,
         ExecuteResponse = 0x0E,
         ExecuteData = 0x0F,
         MemoryDebug64 = 0x10,
         MemoryRead64 = 0x11
    }

    public enum SaharaMode
    {
         ImageTxPending = 0x00,
         TxComplete = 0x01,
         MemoryDebug = 0x02,
         Command = 0x03
    }

    public enum SaharaClientCMD
    {
         NOP = 0x00,
         SerialNumRead = 0x01,
         MSMHWIDRead = 0x02,
         OEMPkHashRead = 0x03,
         SwitchDMSS = 0x04,
         SwitchToStreamingDLoad = 0x05,
         ReadDebugData = 0x06,
         GetSblVersion = 0x07
    }

    public enum SaharaStatusCode
    {
         Success = 0x00,
	     InvalidCmd = 0x01,
	     ProtocolMismatch = 0x02,
	     InvalidTargetProtocol = 0x03,
	     InvalidHostProtocol = 0x04,
	     InvalidPacketSize = 0x05,
	     UnexpectedImageId = 0x06,
	     InvalidHeaderSize = 0x07,
	     InvalidDataSize = 0x08,
	     InvalidImageType = 0x09,
	     InvalidTxLength = 0x0A,
	     InvalidRxLength = 0x0B,
	     TxRxError = 0x0C,
	     ReadDataError = 0x0D,
	     UnsupportedNumPhdrs = 0x0E,
	     InvalidPhdrSize = 0x0F,
	     MultipleSharedSeg = 0x10,
	     UninitPhdrLoc = 0x11,
	     InvalidDestAddress = 0x12,
	     InvalidImageHeaderSize = 0x13,
	     InvalidElfHeader = 0x14,
	     UnknownError = 0x15,
	     TimeoutRx = 0x16,
	     TimeoutTx = 0x17,
	     InvalidMode = 0x18,
	     InvalidMemoryRead = 0x19,
	     InvalidDataSizeRequest = 0x1A,
	     MemoryDebugNotSupported = 0x1B,
	     InvalidModeSwitch = 0x1C,
	     ExecFailure = 0x1D,
	     ExecCmdInvalidParam = 0x1E,
	     ExecCmdUnsupported = 0x1F,
	     ExecDataInvalid = 0x20,
	     HashTableAuthFailure = 0x21,
	     HashVerificationFailure = 0x22,
	     HashTableNotFound = 0x23,
	     TargetInitFailure = 0x24,
	     ImageAuthFailure = 0x25,
	     InvalidImgHashTableSize = 0x26,
         StatusMax = 0x27
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct SaharaState
    {
        [FieldOffset(0)]
        public uint Version;

        [FieldOffset(4)]
        public uint MinVersion;

        [FieldOffset(8)]
        public uint Mode;

        [FieldOffset(12)]
        public SaharaMemoryDebugRequest MemoryDebug;

        [FieldOffset(12)]
        public SaharaCommandReadyResponse ClientCommand;

        [FieldOffset(12)]
        public SaharaReadDataRequest ImageTransfer;

        [FieldOffset(12)]
        public SaharaEndImageTransferResponse ErrorOrDone;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaHeader
    {
        public uint Command;
        public uint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaHelloRequest
    {
        public SaharaHeader Header;
        public uint Version;
        public uint VersionSupported;
        public uint MaxCMDPacketLength;
        public uint Mode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=6)]
        public uint[] Reversed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaHelloResponse
    {
        public SaharaHeader Header;
        public uint Version;
        public uint MinVersion;
        public uint Status;
        public uint Mode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public uint[] Reversed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaMemoryDebugRequest
    {
        public SaharaHeader Header;
        public uint MemoryTableAddress;
        public uint MemoryTableLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaCommandReadyResponse
    {
        public SaharaHeader Header;
        public uint ImageTXStatus;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaReadDataRequest
    {
        public SaharaHeader Header;
        public uint ImageID;
        public uint Offset;
        public uint Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaEndImageTransferResponse
    {
        public SaharaHeader Header;
        public uint File;
        public uint Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaDonePacket
    {
        public SaharaHeader Header;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaDonePacketResponse
    {
        public SaharaHeader Header;
        public uint ImageTransferStatus;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaClientCommandRequest
    {
        public SaharaHeader Header;
        public uint Command;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaExecuteDataRequest
    {
        public SaharaHeader Header;
        public uint Command;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaClientCommandResponse
    {
        public SaharaHeader Header;
        public uint Command;
        public uint Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaSwitchMode
    {
        public SaharaHeader Header;
        public uint Mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaResetRequest
    {
        public SaharaHeader Header;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SaharaResetResponse
    {
        public SaharaHeader Header;
    }
}
