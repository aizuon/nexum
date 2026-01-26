using System;
using System.Buffers.Binary;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Nexum.Core.Configuration;

namespace Nexum.Core.Serialization
{
    public class NetMessage : ByteArray
    {
        public NetMessage()
        {
        }

        public NetMessage(NetMessage message)
        {
            EncryptMode = message.EncryptMode;
            Write(message.GetBufferSpan());
        }

        public NetMessage(ByteArray packet)
            : base(packet)
        {
        }

        public NetMessage(byte[] data, bool useExternalBuffer = false)
            : base(data, useExternalBuffer)
        {
        }

        public NetMessage(byte[] data, int length, bool useExternalBuffer = false)
            : base(data, length, useExternalBuffer)
        {
        }

        internal bool Compress { get; set; }
        internal EncryptMode EncryptMode { get; set; }
        internal uint RelayFrom { get; set; }
        internal bool Reliable { get; set; } = true;

        internal bool Encrypt => EncryptMode != EncryptMode.None;

        public void Write(NetMessage obj)
        {
            Write(obj.GetBufferSpan());
            Compress = obj.Compress;
            EncryptMode = obj.EncryptMode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out Guid obj)
        {
            if (ReadOffset + 16 > Length)
            {
                obj = Guid.Empty;
                return false;
            }

            obj = new Guid(GetBufferSpan().Slice(ReadOffset, 16));
            ReadOffset += 16;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(Guid obj)
        {
            Span<byte> bytes = stackalloc byte[16];
            obj.TryWriteBytes(bytes);
            Write(bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out Version obj)
        {
            obj = null;
            int array = 0;
            ushort major = 0;
            ushort minor = 0;
            ushort build = 0;
            ushort revision = 0;
            if (!Read(ref array) || !Read(ref major) || !Read(ref minor) ||
                !Read(ref build) || !Read(ref revision))
                return false;
            obj = new Version(major, minor, build, revision);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(Version obj)
        {
            Write(4);
            Write((ushort)obj.Major);
            Write((ushort)obj.Minor);
            Write((ushort)obj.Build);
            Write((ushort)obj.Revision);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadStringEndPoint(out IPEndPoint obj)
        {
            obj = null;
            if (!Read(out string ipString))
                return false;
            ushort num = 0;
            if (!Read(ref num))
                return false;
            obj = new IPEndPoint(IPAddress.Parse(ipString), num);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteStringEndPoint(IPEndPoint obj)
        {
            Write(obj.Address.ToString());
            Write((ushort)obj.Port);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out IPEndPoint obj)
        {
            obj = null;
            uint address = 0;
            if (!Read(ref address))
                return false;
            ushort port = 0;
            if (!Read(ref port))
                return false;
            Span<byte> addressBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(addressBytes, address);
            obj = new IPEndPoint(new IPAddress(addressBytes), port);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(IPEndPoint obj)
        {
            Span<byte> addressBytes = stackalloc byte[4];
            obj.Address.TryWriteBytes(addressBytes, out _);
            Write(BinaryPrimitives.ReadUInt32LittleEndian(addressBytes));
            Write((ushort)obj.Port);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(string obj, bool unicode = false)
        {
            Write(unicode ? (byte)2 : (byte)1);

            var encoding = unicode ? Encoding.Unicode : Constants.Encoding;
            WriteScalar(obj.Length);

            int byteCount = unicode ? obj.Length * 2 : obj.Length;
            var targetSpan = GetWritableSpan(byteCount);
            encoding.GetBytes(obj, targetSpan);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out sbyte obj)
        {
            obj = 0;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out byte obj)
        {
            obj = 0;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out short obj)
        {
            obj = 0;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out ushort obj)
        {
            obj = 0;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out int obj)
        {
            obj = 0;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out uint obj)
        {
            obj = 0U;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out long obj)
        {
            obj = 0L;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out ulong obj)
        {
            obj = 0UL;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out float obj)
        {
            obj = 0f;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out double obj)
        {
            obj = 0.0;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out bool obj)
        {
            obj = false;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out string obj)
        {
            return Read(out obj, out bool _);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out string obj, out bool isUnicode)
        {
            obj = null;
            isUnicode = false;
            byte unicodeFlag = 0;
            long length = 0;
            if (!Read(ref unicodeFlag) || !ReadScalar(ref length))
                return false;
            if (length == 0L)
            {
                obj = string.Empty;
                return true;
            }

            if (unicodeFlag == 2)
            {
                length = 2L * length;
                if (ReadOffset + length > Length)
                    return false;
                obj = Encoding.Unicode.GetString(
                    GetBufferSpan().Slice(ReadOffset, (int)length));
                isUnicode = true;
            }
            else
            {
                if (ReadOffset + length > Length)
                    return false;
                obj = Constants.Encoding.GetString(
                    GetBufferSpan().Slice(ReadOffset, (int)length));
            }

            ReadOffset += (int)length;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read<T>(out T obj) where T : struct, Enum
        {
            obj = default(T);
            return Read(ref obj);
        }
    }
}
