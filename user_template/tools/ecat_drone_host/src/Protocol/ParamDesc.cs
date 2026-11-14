/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * ParamDesc.cs - parameter descriptor (70 byte wire format)
 *
 * Wire layout (little endian, packed) - see inc/host_proto.h:
 *   0  u16 id
 *   2  u8  type       (param_type_t)
 *   3  u8  persist
 *   4  u8  readonly
 *   5  u8  len
 *   6  i32 min
 *   10 i32 max
 *   14 i32 def
 *   18 i32 value
 *   22 char name[16]  (ASCII, NUL terminated)
 *   38 char desc[32]  (UTF-8, NUL terminated)
 *   70 end
 */

using System;
using System.Text;

namespace EcatDroneHost.Protocol
{
    /// <summary>param_type_t from inc/param.h.</summary>
    public enum ParamType : byte
    {
        U8 = 0,
        U16 = 1,
        U32 = 2,
        I16 = 3,
        I32 = 4,
        Str = 5
    }

    /// <summary>One parameter descriptor as sent by LIST_PARAMS / GET_PARAM_DESC.</summary>
    public sealed class ParamDesc
    {
        public const int WireSize = 70;
        public const int OffId = 0;
        public const int OffType = 2;
        public const int OffPersist = 3;
        public const int OffReadOnly = 4;
        public const int OffLen = 5;
        public const int OffMin = 6;
        public const int OffMax = 10;
        public const int OffDef = 14;
        public const int OffValue = 18;
        public const int OffName = 22;
        public const int NameLength = 16;
        public const int OffDesc = 38;
        public const int DescLength = 32;

        public ushort Id;
        public ParamType Type;
        public bool Persist;
        public bool ReadOnly;
        public byte Len;
        public int Min;
        public int Max;
        public int Default;
        public int Value;
        public string Name = string.Empty;
        public string Description = string.Empty;

        public string TypeName
        {
            get
            {
                switch (Type)
                {
                    case ParamType.U8: return "u8";
                    case ParamType.U16: return "u16";
                    case ParamType.U32: return "u32";
                    case ParamType.I16: return "i16";
                    case ParamType.I32: return "i32";
                    case ParamType.Str: return "str";
                    default: return "?";
                }
            }
        }

        public bool ValueInRange
        {
            get { return Value >= Min && Value <= Max; }
        }

        /// <summary>Decode one descriptor from a byte buffer (length validated by the caller).</summary>
        public static ParamDesc FromBytes(byte[] data, int offset, int size)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }
            if (size < OffName + 8)
            {
                throw new HostException("参数描述符长度过短: " + size);
            }
            if (offset < 0 || offset + size > data.Length)
            {
                throw new ArgumentOutOfRangeException("offset");
            }

            ParamDesc desc = new ParamDesc();
            desc.Id = Bytes.GetU16(data, offset + OffId);
            desc.Type = (ParamType)data[offset + OffType];
            desc.Persist = data[offset + OffPersist] != 0;
            desc.ReadOnly = data[offset + OffReadOnly] != 0;
            desc.Len = data[offset + OffLen];
            desc.Min = Bytes.GetI32(data, offset + OffMin);
            desc.Max = Bytes.GetI32(data, offset + OffMax);
            desc.Default = Bytes.GetI32(data, offset + OffDef);
            desc.Value = Bytes.GetI32(data, offset + OffValue);
            desc.Name = ReadFixedString(data, offset + OffName, NameLength);
            int descAvailable = size - OffDesc;
            if (descAvailable > DescLength)
            {
                descAvailable = DescLength;
            }
            if (descAvailable > 0)
            {
                desc.Description = ReadFixedString(data, offset + OffDesc, descAvailable);
            }
            return desc;
        }

        public static ParamDesc FromBody(byte[] body)
        {
            if (body == null)
            {
                throw new HostException("参数描述符为空");
            }
            return FromBytes(body, 0, body.Length);
        }

        /// <summary>Serialize into the canonical 70 byte wire format (used by the simulator).</summary>
        public byte[] ToBytes()
        {
            byte[] data = new byte[WireSize];
            Bytes.PutU16(data, OffId, Id);
            data[OffType] = (byte)Type;
            data[OffPersist] = Persist ? (byte)1 : (byte)0;
            data[OffReadOnly] = ReadOnly ? (byte)1 : (byte)0;
            data[OffLen] = Len;
            Bytes.PutU32(data, OffMin, (uint)Min);
            Bytes.PutU32(data, OffMax, (uint)Max);
            Bytes.PutU32(data, OffDef, (uint)Default);
            Bytes.PutU32(data, OffValue, (uint)Value);
            WriteFixedString(data, OffName, NameLength, Name);
            WriteFixedString(data, OffDesc, DescLength, Description);
            return data;
        }

        private static string ReadFixedString(byte[] data, int offset, int maxLength)
        {
            int length = 0;
            while (length < maxLength && data[offset + length] != 0)
            {
                length++;
            }
            if (length == 0)
            {
                return string.Empty;
            }
            string text = Encoding.UTF8.GetString(data, offset, length);
            return text.Trim();
        }

        private static void WriteFixedString(byte[] data, int offset, int maxLength, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text == null ? string.Empty : text);
            int count = Math.Min(bytes.Length, maxLength);
            /* never cut a multi byte UTF-8 sequence in half */
            while (count > 0 && count < bytes.Length && (bytes[count] & 0xC0) == 0x80)
            {
                count--;
            }
            if (count < maxLength)
            {
                /* leave room for the NUL terminator when the text is shorter */
                Array.Copy(bytes, 0, data, offset, count);
                for (int i = count; i < maxLength; i++)
                {
                    data[offset + i] = 0;
                }
            }
            else
            {
                /* field completely filled (e.g. 16 char parameter name), no NUL */
                Array.Copy(bytes, 0, data, offset, count);
            }
        }

        /// <summary>Split a LIST_PARAMS response into descriptors.</summary>
        public static ParamDesc[] FromList(byte[] body, int expectedCount)
        {
            if (body == null)
            {
                throw new HostException("LIST_PARAMS 响应为空");
            }
            if (expectedCount <= 0)
            {
                return new ParamDesc[0];
            }
            if (body.Length % expectedCount != 0)
            {
                throw new HostException(string.Format(
                    "LIST_PARAMS 响应长度 {0} 无法被数量 {1} 整除", body.Length, expectedCount));
            }
            int size = body.Length / expectedCount;
            if (size < OffName)
            {
                throw new HostException("参数描述符尺寸异常: " + size);
            }
            ParamDesc[] list = new ParamDesc[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                list[i] = FromBytes(body, i * size, size);
            }
            return list;
        }

        public override string ToString()
        {
            return string.Format("#{0} {1} {2} min={3} max={4} def={5} val={6}",
                Id, Name, TypeName, Min, Max, Default, Value);
        }
    }
}
