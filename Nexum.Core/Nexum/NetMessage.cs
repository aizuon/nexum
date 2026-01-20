using System;
using System.Buffers.Binary;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace Nexum.Core
{
    public class NetMessage : ByteArray
    {
        private static readonly Encoding Latin1Encoding = Encoding.GetEncoding(1252);

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

        internal byte[] Buffer => GetBuffer();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool Read(ref MessageType obj)
        {
            byte num = 0;
            if (!Read(ref num))
                return false;
            obj = (MessageType)num;
            return true;
        }

        public void Write(NetMessage obj)
        {
            Write(obj.GetBufferSpan());
            Compress = obj.Compress;
            EncryptMode = obj.EncryptMode;
        }

        public void Write(string obj, bool unicode = false)
        {
            Write(unicode ? (byte)2 : (byte)1);
            if (unicode)
            {
                int maxByteCount = obj.Length * 2;
                if (maxByteCount <= 256)
                {
                    Span<byte> buffer = stackalloc byte[maxByteCount];
                    int bytesWritten = Encoding.Unicode.GetBytes(obj, buffer);
                    WriteScalar(bytesWritten / 2);
                    Write(buffer.Slice(0, bytesWritten));
                }
                else
                {
                    byte[] bytes = Encoding.Unicode.GetBytes(obj);
                    WriteScalar(bytes.Length / 2);
                    Write(bytes);
                }
            }
            else
            {
                WriteScalar(obj.Length);
                if (obj.Length <= 256)
                {
                    Span<byte> buffer = stackalloc byte[obj.Length];
                    Latin1Encoding.GetBytes(obj, buffer);
                    Write(buffer);
                }
                else
                {
                    Write(Latin1Encoding.GetBytes(obj));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(Guid obj)
        {
            Span<byte> bytes = stackalloc byte[16];
            obj.TryWriteBytes(bytes);
            Write(bytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out Guid b)
        {
            if (ReadOffset + 16 > Length)
            {
                b = Guid.Empty;
                return false;
            }

            b = new Guid(GetBufferSpan().Slice(ReadOffset, 16));
            ReadOffset += 16;
            return true;
        }

        public void Write(Version value)
        {
            Write(4);
            Write((ushort)value.Major);
            Write((ushort)value.Minor);
            Write((ushort)value.Build);
            Write((ushort)value.Revision);
        }

        public bool ReadVersion(ref Version value)
        {
            if (!Read(out Version value2))
            {
                value = new Version();
                return false;
            }

            value = value2;
            return true;
        }

        public bool Read(out Version value)
        {
            value = new Version();
            int num1 = 0;
            ushort num2 = 0;
            ushort num3 = 0;
            ushort num4 = 0;
            ushort num5 = 0;
            if (!Read(ref num1) || !Read(ref num3) || !Read(ref num2) || !Read(ref num4) || !Read(ref num5))
                return false;
            value = new Version(num3, num2, num4, num5);
            return true;
        }

        public void WriteStringEndPoint(IPEndPoint obj)
        {
            Write(obj.Address.ToString());
            Write((ushort)obj.Port);
        }

        public bool ReadStringEndPoint(ref IPEndPoint b)
        {
            string ipString = string.Empty;
            if (!ReadString(ref ipString))
                return false;
            ushort num = 0;
            if (!Read(ref num))
                return false;
            b = new IPEndPoint(IPAddress.Parse(ipString), num);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(IPEndPoint b)
        {
            Span<byte> addressBytes = stackalloc byte[4];
            b.Address.TryWriteBytes(addressBytes, out _);
            Write((uint)((addressBytes[3] << 24) + (addressBytes[2] << 16) + (addressBytes[1] << 8)) + addressBytes[0]);
            Write((ushort)b.Port);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadIPEndPoint(ref IPEndPoint b)
        {
            uint num1 = 0;
            if (!Read(ref num1))
                return false;
            ushort num2 = 0;
            if (!Read(ref num2))
                return false;
            Span<byte> addressBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(addressBytes, num1);
            b = new IPEndPoint(new IPAddress(addressBytes), num2);
            return true;
        }

        public string ReadString()
        {
            string str = string.Empty;
            ReadString(ref str);
            return str;
        }

        public bool ReadString(ref string obj)
        {
            return Read(ref obj, out bool isUnicode);
        }

        public bool Read(ref string obj, out bool isUnicode)
        {
            isUnicode = false;
            byte num1 = 0;
            long num2 = 0;
            if (!Read(ref num1) || !ReadScalar(ref num2))
                return false;
            if (num2 == 0L)
            {
                obj = string.Empty;
                return true;
            }

            if (num1 == 2)
            {
                num2 = 2L * num2;
                if (ReadOffset + num2 > Length)
                    return false;
                obj = Encoding.Unicode.GetString(GetBufferSpan().Slice(ReadOffset, (int)num2));
                isUnicode = true;
            }
            else
            {
                if (ReadOffset + num2 > Length)
                    return false;
                obj = Latin1Encoding.GetString(GetBufferSpan().Slice(ReadOffset, (int)num2));
            }

            ReadOffset += (int)num2;
            return true;
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
            obj = 0UL;
            return Read(ref obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Read(out double obj)
        {
            obj = 0UL;
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
            obj = string.Empty;
            return ReadString(ref obj);
        }
    }
}
