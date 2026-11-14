/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * HostProtocol.cs - framing / command set of the USB CDC host protocol.
 *
 * Frame (exactly as documented in the firmware inc/host_proto.h):
 *
 *   +------+------+-----+-----+--------+--------+===========+-------+-------+
 *   | 0xA5 | 0x5A | CMD | SEQ | LEN_LO | LEN_HI |  PAYLOAD  | CRC_LO| CRC_HI|
 *   +------+------+-----+-----+--------+--------+===========+-------+-------+
 *
 *   - LEN is little endian (0..512, see APP_CFG_HOST_PROTO_MAX_PAYLOAD)
 *   - CRC is CRC16-CCITT-FALSE over CMD..PAYLOAD (header after sync + payload)
 *   - the device echoes CMD and SEQ in every response
 *   - response payload[0] is the status code (HostStatus)
 */

using System;

namespace EcatDroneHost.Protocol
{
    /// <summary>Protocol constants copied from inc/host_proto.h / inc/app_config.h.</summary>
    public static class HostProto
    {
        public const byte Sync0 = 0xA5;
        public const byte Sync1 = 0x5A;
        public const int HeaderSize = 6;      /* sync0 sync1 cmd seq len_lo len_hi */
        public const int CrcSize = 2;
        public const int Overhead = HeaderSize + CrcSize;
        public const int MaxPayload = 512;    /* APP_CFG_HOST_PROTO_MAX_PAYLOAD */

        public const int DefaultTimeoutMs = 300;
        public const int DefaultRetries = 2;  /* => 3 attempts in total */

        public const uint BootloaderMagic = 0x424F4F54; /* 'BOOT' */

        public const string FirmwareName = "ecat_drone_powertrain";
    }

    /// <summary>Command codes (host_cmd_t in inc/host_proto.h).</summary>
    public enum HostCmd : byte
    {
        Ping = 0x01,
        GetVersion = 0x02,
        GetTelemetry = 0x03,
        GetEcatStatus = 0x04,

        GetParam = 0x10,
        SetParam = 0x11,
        GetParamDesc = 0x12,
        ParamSave = 0x13,
        ParamReset = 0x14,
        ParamGetCount = 0x15,
        ListParams = 0x16,

        DshotTest = 0x20,
        DshotGetTlm = 0x21,
        DshotCmd = 0x22,

        ServoScan = 0x30,
        ServoMoveTest = 0x31,
        ServoRead = 0x32,
        ServoSetId = 0x33,
        ServoRaw = 0x34,

        AdcGet = 0x40,
        LedSet = 0x41,
        LedTest = 0x42,

        Am32Begin = 0x50,
        Am32Xfer = 0x51,
        Am32End = 0x52,
        Am32Status = 0x53,
        Am32ProgWrite = 0x54,
        Am32ProgSave = 0x55,

        Bootloader = 0x60,
        Reset = 0x61
    }

    /// <summary>Status codes returned in response payload[0] (host_status_t).</summary>
    public enum HostStatus : byte
    {
        Ok = 0x00,
        ErrUnknownCmd = 0x01,
        ErrLength = 0x02,
        ErrCrc = 0x03,
        ErrParamId = 0x04,
        ErrParamRange = 0x05,
        ErrParamReadOnly = 0x06,
        ErrBusy = 0x07,
        ErrNotReady = 0x08,
        ErrTimeout = 0x09,
        ErrUnsupported = 0x0A,
        ErrFlash = 0x0B
    }

    public static class HostStatusText
    {
        /// <summary>Short Chinese text for a status code (shown in the UI / log).</summary>
        public static string Describe(HostStatus status)
        {
            switch (status)
            {
                case HostStatus.Ok: return "成功";
                case HostStatus.ErrUnknownCmd: return "未知命令";
                case HostStatus.ErrLength: return "长度错误";
                case HostStatus.ErrCrc: return "CRC 错误";
                case HostStatus.ErrParamId: return "参数 ID 无效";
                case HostStatus.ErrParamRange: return "参数超出范围";
                case HostStatus.ErrParamReadOnly: return "参数只读";
                case HostStatus.ErrBusy: return "设备忙";
                case HostStatus.ErrNotReady: return "设备未就绪";
                case HostStatus.ErrTimeout: return "设备内部超时";
                case HostStatus.ErrUnsupported: return "命令不支持";
                case HostStatus.ErrFlash: return "Flash 操作失败";
                default: return "未知状态(0x" + ((byte)status).ToString("X2") + ")";
            }
        }
    }

    /// <summary>One complete protocol frame (also used as the raw-trace record).</summary>
    public sealed class HostFrame
    {
        public byte Cmd;
        public byte Seq;
        public byte[] Payload;

        public HostFrame()
        {
            Payload = new byte[0];
        }

        public HostFrame(byte cmd, byte seq, byte[] payload)
        {
            Cmd = cmd;
            Seq = seq;
            Payload = payload ?? new byte[0];
        }

        public HostCmd Command
        {
            get { return (HostCmd)Cmd; }
        }

        public bool TryGetStatus(out HostStatus status)
        {
            if (Payload.Length < 1)
            {
                status = HostStatus.ErrLength;
                return false;
            }
            status = (HostStatus)Payload[0];
            return true;
        }

        /// <summary>Payload without the status byte (may be empty).</summary>
        public byte[] Body
        {
            get
            {
                if (Payload.Length <= 1)
                {
                    return new byte[0];
                }
                byte[] body = new byte[Payload.Length - 1];
                Array.Copy(Payload, 1, body, 0, body.Length);
                return body;
            }
        }

        public override string ToString()
        {
            return string.Format("CMD=0x{0:X2} SEQ={1} LEN={2}", Cmd, Seq, Payload.Length);
        }
    }

    /// <summary>Protocol exception base class.</summary>
    public class HostException : Exception
    {
        public HostException(string message) : base(message) { }
        public HostException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Thrown when the device answered with a non-zero status code.</summary>
    public sealed class HostStatusException : HostException
    {
        public HostCmd Cmd { get; private set; }
        public HostStatus Status { get; private set; }

        public HostStatusException(HostCmd cmd, HostStatus status)
            : base(string.Format("命令 0x{0:X2} 返回错误: {1} (0x{2:X2})",
                   (byte)cmd, HostStatusText.Describe(status), (byte)status))
        {
            Cmd = cmd;
            Status = status;
        }
    }

    /// <summary>Thrown when no (matching) response arrived within the timeout.</summary>
    public sealed class HostTimeoutException : HostException
    {
        public HostCmd Cmd { get; private set; }
        public int TimeoutMs { get; private set; }
        public int Attempts { get; private set; }

        public HostTimeoutException(HostCmd cmd, int timeoutMs, int attempts)
            : base(string.Format("命令 0x{0:X2} 超时（{1} 次尝试，每次 {2} ms）",
                   (byte)cmd, attempts, timeoutMs))
        {
            Cmd = cmd;
            TimeoutMs = timeoutMs;
            Attempts = attempts;
        }
    }

    /// <summary>Frame encoder / decoder for the board protocol.</summary>
    public static class FrameCodec
    {
        /// <summary>Encode one request frame. Payload may be null or empty.</summary>
        public static byte[] Encode(HostCmd cmd, byte seq, byte[] payload)
        {
            return Encode((byte)cmd, seq, payload);
        }

        /// <summary>Encode one frame with a raw command byte (used by the raw terminal).</summary>
        public static byte[] Encode(byte cmd, byte seq, byte[] payload)
        {
            int payloadLen = payload == null ? 0 : payload.Length;
            if (payloadLen > HostProto.MaxPayload)
            {
                throw new ArgumentOutOfRangeException("payload", "payload exceeds APP_CFG_HOST_PROTO_MAX_PAYLOAD");
            }

            byte[] frame = new byte[HostProto.Overhead + payloadLen];
            frame[0] = HostProto.Sync0;
            frame[1] = HostProto.Sync1;
            frame[2] = cmd;
            frame[3] = seq;
            frame[4] = (byte)(payloadLen & 0xFF);
            frame[5] = (byte)((payloadLen >> 8) & 0xFF);
            if (payloadLen > 0)
            {
                Array.Copy(payload, 0, frame, HostProto.HeaderSize, payloadLen);
            }

            /* CRC over CMD..PAYLOAD */
            ushort crc = Crc16.CcittFalse(frame, 2, 4 + payloadLen);
            frame[HostProto.HeaderSize + payloadLen] = (byte)(crc & 0xFF);
            frame[HostProto.HeaderSize + payloadLen + 1] = (byte)((crc >> 8) & 0xFF);
            return frame;
        }

        /// <summary>
        /// Try to decode a complete frame from a buffer.
        /// Returns false if the buffer does not hold a complete valid frame.
        /// </summary>
        public static bool TryDecode(byte[] buffer, int offset, int count, out HostFrame frame, out int consumed)
        {
            frame = null;
            consumed = 0;
            if (buffer == null || count < HostProto.Overhead || offset < 0 || offset + count > buffer.Length)
            {
                return false;
            }
            if (buffer[offset] != HostProto.Sync0 || buffer[offset + 1] != HostProto.Sync1)
            {
                return false;
            }
            int payloadLen = buffer[offset + 4] | (buffer[offset + 5] << 8);
            if (payloadLen > HostProto.MaxPayload || count < HostProto.Overhead + payloadLen)
            {
                return false;
            }
            int payloadOffset = offset + HostProto.HeaderSize;
            ushort expected = (ushort)(buffer[payloadOffset + payloadLen] |
                                       (buffer[payloadOffset + payloadLen + 1] << 8));
            ushort actual = Crc16.CcittFalse(buffer, offset + 2, 4 + payloadLen);
            if (expected != actual)
            {
                return false;
            }
            byte[] payload = new byte[payloadLen];
            Array.Copy(buffer, payloadOffset, payload, 0, payloadLen);
            frame = new HostFrame(buffer[offset + 2], buffer[offset + 3], payload);
            consumed = HostProto.Overhead + payloadLen;
            return true;
        }
    }

    /// <summary>
    /// Streaming frame parser with resynchronisation support.
    /// Feed() raw bytes, poll TryRead() until it returns false.
    /// A corrupted / truncated frame only costs one sync byte before the parser
    /// scans forward for the next A5 5A pair, so a noisy link recovers by itself.
    /// </summary>
    public sealed class FrameParser
    {
        private byte[] _buffer = new byte[HostProto.Overhead + HostProto.MaxPayload + 64];
        private int _length;

        public int FramesOk { get; private set; }
        public int CrcErrors { get; private set; }
        public int Resyncs { get; private set; }
        public int BytesDiscarded { get; private set; }

        /// <summary>Bytes currently buffered but not yet consumed.</summary>
        public int Buffered
        {
            get { return _length; }
        }

        public void Reset()
        {
            _length = 0;
        }

        public void ResetStatistics()
        {
            FramesOk = 0;
            CrcErrors = 0;
            Resyncs = 0;
            BytesDiscarded = 0;
        }

        /// <summary>Append raw bytes received from the transport.</summary>
        public void Feed(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0)
            {
                return;
            }
            if (offset < 0 || offset + count > data.Length)
            {
                throw new ArgumentOutOfRangeException("offset");
            }
            if (_length + count > _buffer.Length)
            {
                /* keep the tail (it may start a valid frame) and drop the head */
                int keep = _buffer.Length - count;
                if (keep < 0)
                {
                    keep = 0;
                }
                int drop = _length - keep;
                if (drop > 0)
                {
                    Array.Copy(_buffer, drop, _buffer, 0, keep);
                    _length = keep;
                    BytesDiscarded += drop;
                }
                if (count > _buffer.Length)
                {
                    /* absurdly large read: only keep the last bytes */
                    Array.Copy(data, offset + count - _buffer.Length, _buffer, 0, _buffer.Length);
                    _length = _buffer.Length;
                    BytesDiscarded += count - _buffer.Length;
                    return;
                }
            }
            Array.Copy(data, offset, _buffer, _length, count);
            _length += count;
        }

        /// <summary>Extract the next complete frame, false when more bytes are needed.</summary>
        public bool TryRead(out HostFrame frame)
        {
            frame = null;
            while (true)
            {
                if (_length < 2)
                {
                    return false;
                }

                /* find the sync pattern */
                int sync = -1;
                for (int i = 0; i + 1 < _length; i++)
                {
                    if (_buffer[i] == HostProto.Sync0 && _buffer[i + 1] == HostProto.Sync1)
                    {
                        sync = i;
                        break;
                    }
                }
                if (sync < 0)
                {
                    /* keep a trailing 0xA5 - it may be the start of the next frame */
                    int drop = _length - 1;
                    if (_buffer[_length - 1] != HostProto.Sync0)
                    {
                        drop = _length;
                    }
                    if (drop > 0)
                    {
                        BytesDiscarded += drop;
                        Resyncs++;
                        Consume(drop);
                    }
                    return false;
                }
                if (sync > 0)
                {
                    BytesDiscarded += sync;
                    Resyncs++;
                    Consume(sync);
                    continue;
                }

                if (_length < HostProto.HeaderSize)
                {
                    return false;
                }
                int payloadLen = _buffer[4] | (_buffer[5] << 8);
                if (payloadLen > HostProto.MaxPayload)
                {
                    /* invalid length: skip one sync byte and scan again */
                    BytesDiscarded += 1;
                    Resyncs++;
                    Consume(1);
                    continue;
                }
                int total = HostProto.Overhead + payloadLen;
                if (_length < total)
                {
                    return false;
                }

                ushort expected = (ushort)(_buffer[HostProto.HeaderSize + payloadLen] |
                                           (_buffer[HostProto.HeaderSize + payloadLen + 1] << 8));
                ushort actual = Crc16.CcittFalse(_buffer, 2, 4 + payloadLen);
                if (expected != actual)
                {
                    CrcErrors++;
                    BytesDiscarded += 1;
                    Resyncs++;
                    Consume(1);
                    continue;
                }

                byte[] payload = new byte[payloadLen];
                Array.Copy(_buffer, HostProto.HeaderSize, payload, 0, payloadLen);
                frame = new HostFrame(_buffer[2], _buffer[3], payload);
                Consume(total);
                FramesOk++;
                return true;
            }
        }

        private void Consume(int count)
        {
            if (count >= _length)
            {
                _length = 0;
                return;
            }
            Array.Copy(_buffer, count, _buffer, 0, _length - count);
            _length -= count;
        }
    }

    /// <summary>Small helpers to build / read little endian payloads.</summary>
    public static class Bytes
    {
        public static void PutU16(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        public static void PutU32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        public static ushort GetU16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        public static short GetI16(byte[] buffer, int offset)
        {
            return (short)GetU16(buffer, offset);
        }

        public static uint GetU32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) |
                          (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }

        public static int GetI32(byte[] buffer, int offset)
        {
            return (int)GetU32(buffer, offset);
        }

        /// <summary>"A5 5A 01 00 ..." style dump.</summary>
        public static string ToHexString(byte[] data)
        {
            return ToHexString(data, 0, data == null ? 0 : data.Length);
        }

        public static string ToHexString(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0)
            {
                return string.Empty;
            }
            if (offset < 0 || offset + count > data.Length)
            {
                count = Math.Max(0, data.Length - Math.Max(0, offset));
            }
            char[] text = new char[count * 3];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                byte value = data[offset + i];
                text[p++] = "0123456789ABCDEF"[value >> 4];
                text[p++] = "0123456789ABCDEF"[value & 0x0F];
                if (i + 1 < count)
                {
                    text[p++] = ' ';
                }
            }
            return new string(text, 0, p);
        }

        /// <summary>Parse "A5 5A 01" or "A55A01" style hex text. Returns null on error.</summary>
        public static byte[] ParseHexString(string text, out string error)
        {
            error = null;
            if (text == null)
            {
                error = "空字符串";
                return null;
            }
            System.Text.StringBuilder digits = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == ',' || c == ';' || c == ':')
                {
                    continue;
                }
                if (Uri.IsHexDigit(c))
                {
                    digits.Append(c);
                    continue;
                }
                error = "非法字符 '" + c + "'";
                return null;
            }
            if (digits.Length == 0)
            {
                return new byte[0];
            }
            if ((digits.Length & 1) != 0)
            {
                /* tolerate "A 5 5A" style: pad the last nibble */
                digits.Append('0');
            }
            byte[] data = new byte[digits.Length / 2];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = Convert.ToByte(digits.ToString(i * 2, 2), 16);
            }
            return data;
        }
    }
}
