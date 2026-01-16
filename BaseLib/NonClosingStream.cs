using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaseLib
{
    public sealed class NonClosingStream : Stream
    {
        public NonClosingStream(Stream input)
        {
            BaseStream = input;
        }

        public Stream BaseStream { get; }

        public override bool CanRead => BaseStream.CanRead;

        public override bool CanSeek => BaseStream.CanSeek;

        public override bool CanWrite => BaseStream.CanWrite;

        public override bool CanTimeout => BaseStream.CanTimeout;

        public override long Length => BaseStream.Length;

        public override long Position
        {
            get => BaseStream.Position;
            set => BaseStream.Position = value;
        }

        public override int ReadTimeout
        {
            get => BaseStream.ReadTimeout;
            set => BaseStream.ReadTimeout = value;
        }

        public override int WriteTimeout
        {
            get => BaseStream.WriteTimeout;
            set => BaseStream.WriteTimeout = value;
        }

        public override void Flush()
        {
            BaseStream.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return BaseStream.FlushAsync(cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return BaseStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            BaseStream.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return BaseStream.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return BaseStream.Read(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return BaseStream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return BaseStream.ReadAsync(buffer, cancellationToken);
        }

        public override int ReadByte()
        {
            return BaseStream.ReadByte();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            BaseStream.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            BaseStream.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return BaseStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return BaseStream.WriteAsync(buffer, cancellationToken);
        }

        public override void WriteByte(byte value)
        {
            BaseStream.WriteByte(value);
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            BaseStream.CopyTo(destination, bufferSize);
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            return BaseStream.CopyToAsync(destination, bufferSize, cancellationToken);
        }
    }
}
