using SharpEDL.DataClass;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpEDL
{
    public class SparseStream : Stream
    {
        public override bool CanRead { get; } = true;

        public override bool CanSeek { get; } = false;

        public override bool CanWrite { get; } = false;

        public override bool CanTimeout => false;

        public override long Length { get; }

        public override long Position { get => throw new NotSupportedException("Position not supported");
            set => throw new NotSupportedException("Position not supported"); }


        public static readonly uint HeaderMagic = 0xED26FF3A;
        public static readonly int HeaderSize = 28;
        public static readonly int ChunkHeaderSize = 12;

        private Stream BaseStream { get; set; }
        private Ext4FileHeader Header;
        private long CurrentChunkPosition, CurrentChunkSize, CurrentFillChunkIndex;
        private ushort ChunkType;

        public SparseStream(Stream stream)
        {
            BaseStream = stream;
            byte[] header = new byte[HeaderSize];
            stream.Read(header, 0, HeaderSize);
            Header = DataHelper.Bytes2Struct<Ext4FileHeader>(header, header.Length);
            if (Header.Magic !=  HeaderMagic)
                throw new ArgumentException("Not a valid sparse file", nameof(stream));
            Length = Header.BlockSize * Header.TotalBlocks;
            ReadChunkHeader();
        }

        private void ReadChunkHeader()
        {
            byte[] buffer = new byte[ChunkHeaderSize];
            BaseStream.Read(buffer, 0, ChunkHeaderSize);
            Ext4ChunkHeader header = DataHelper.Bytes2Struct<Ext4ChunkHeader>(buffer, ChunkHeaderSize);
            CurrentChunkPosition = 0;
            CurrentChunkSize = header.ChunkSize * Header.BlockSize;
            ChunkType = header.Type;
        }

        public override void Flush()
        {
            
        }

        public int Read(Stream stream, long count)
        {
            long totalReadSize = 0;
            while(totalReadSize < count)
            {
                if (BaseStream.Position >= BaseStream.Length &&
                    (ChunkType == 0xCAC1 || CurrentChunkPosition >= CurrentChunkSize))
                    break;
                if (CurrentChunkPosition >= CurrentChunkSize)
                    ReadChunkHeader();
                long readSize = Math.Min(count - totalReadSize, CurrentChunkSize - CurrentChunkPosition);
                byte[] tmpBuffer = new byte[readSize];
                if (ChunkType == 0xCAC1 || ChunkType == 0xCAC2 || ChunkType == 0xCAC3)
                {
                    if (ChunkType == 0xCAC1)
                    {
                        BaseStream.Read(tmpBuffer);
                        stream.Write(tmpBuffer);
                    }
                    else if (ChunkType == 0xCAC2)
                    {
                        int index = 0;
                        while (index < readSize)
                        {
                            if (CurrentFillChunkIndex == 4)
                            {
                                BaseStream.Position -= 4;
                                CurrentFillChunkIndex = 0;
                            }
                            stream.WriteByte((byte)BaseStream.ReadByte());
                            CurrentFillChunkIndex++;
                            index++;
                        }
                    }
                    else if (ChunkType == 0xCAC3)
                        stream.Write(tmpBuffer);
                    totalReadSize += readSize;
                    CurrentChunkPosition += readSize;
                }
                else if (ChunkType == 0xCAC4)
                    BaseStream.Position += 4;
                else
                {
                    throw new InvalidDataException("Invalid chunk type.");
                }
            }
            return (int)totalReadSize;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            MemoryStream stream = new MemoryStream(buffer, offset, count);
            int readSize = 0;
            try
            {
                readSize = Read(stream, count);
            }
            catch (Exception)
            {
                stream.Close();
                throw;
            }
            stream.Close();
            return readSize;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("Seek not supported");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("SetLength not supported");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Write not supported");
        }

        public override void Close()
        {
            base.Close();
            BaseStream.Close();
        }
    }
}
