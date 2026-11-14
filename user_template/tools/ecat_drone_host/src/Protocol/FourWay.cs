/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * FourWay.cs - AM32 / BLHeli "4-way interface" framing, independent of the
 *              transport.  Two frame flavours are supported:
 *
 *  1) ContainerBootloader (default, contract confirmed by the firmware side):
 *       request : [ESC][CMD][LEN][PARAM...][CRC_Hi][CRC_Lo]
 *       CRC     : CRC16-XMODEM (poly 0x1021, init 0x0000, no reflection)
 *                 over ESC..PARAM (everything before the CRC)
 *       The board puts the frame on the ESC signal line and returns the raw
 *       answer bytes.  Command 0x04 = READ_EEPROM, 0x05 = PROG_EEPROM,
 *       0x30 = TEST_ALIVE, 0x00 = RUN/EXIT.
 *
 *  2) Interface4Way (BLHeliSuite / ESC-Configurator compatible, the format
 *     spoken by the flight controller side serial_4way.c):
 *       request : [0x2F][CMD][ADDR_H][ADDR_L][LEN][PARAM...][CRC_Hi][CRC_Lo]
 *       reply   : [0x2E][CMD][ADDR_H][ADDR_L][LEN][DATA...][ACK][CRC_Hi][CRC_Lo]
 *       CRC     : CRC16-XMODEM over everything up to the CRC, Hi byte first
 *       0x3D = READ_EEPROM, 0x3E = PROG_EEPROM, 0x30 = TEST_ALIVE ...
 *
 * A "RawBytes" mode lets the user type arbitrary bytes for bring-up.
 *
 * References (local, see .work/refs/DSHOT_AM32_NOTES.md):
 *   .work/refs/am32/bf_serial_4way.c                     (interface protocol)
 *   .work/refs/am32/bf_serial_4way_avrootloader.c        (bootloader wire frames)
 *   .work/refs/am32/am32_eeprom.h                        (192 byte EEPROM map)
 */

using System;

namespace EcatDroneHost.Protocol
{
    /// <summary>How the PC frames its 4-way requests.</summary>
    public enum FourWayFrameMode
    {
        /// <summary>[ESC][CMD][LEN][PARAM][CRC16-XMODEM] - board side bridge contract.</summary>
        ContainerBootloader = 0,
        /// <summary>BLHeliSuite / ESC-Configurator interface frames (0x2F escape).</summary>
        Interface4Way = 1,
        /// <summary>The user supplies the exact bytes that go on the ESC line.</summary>
        RawBytes = 2
    }

    /// <summary>Command codes used by the 4-way protocol.</summary>
    public static class FourWayCommand
    {
        /* bootloader level (used by the container and by the direct wire protocol) */
        public const byte Run = 0x00;
        public const byte ProgFlash = 0x01;
        public const byte EraseFlash = 0x02;
        public const byte ReadFlashSil = 0x03;
        public const byte ReadEeprom = 0x04;
        public const byte ProgEeprom = 0x05;
        public const byte ReadSram = 0x06;
        public const byte BootInit = 0x07;
        public const byte BootSign = 0x08;
        public const byte KeepAlive = 0xFD;
        public const byte SetAddress = 0xFF;
        public const byte SetBuffer = 0xFE;

        /* interface level (serial_4way.c) */
        public const byte RemoteEscape = 0x2E;      /* '.' */
        public const byte LocalEscape = 0x2F;       /* '/' */
        public const byte TestAlive = 0x30;
        public const byte ProtocolGetVersion = 0x31;
        public const byte InterfaceGetName = 0x32;
        public const byte InterfaceGetVersion = 0x33;
        public const byte InterfaceExit = 0x34;
        public const byte DeviceReset = 0x35;
        public const byte DeviceInitFlash = 0x37;
        public const byte DeviceEraseAll = 0x38;
        public const byte DevicePageErase = 0x39;
        public const byte DeviceRead = 0x3A;
        public const byte DeviceWrite = 0x3B;
        public const byte DeviceReadEeprom = 0x3D;
        public const byte DeviceWriteEeprom = 0x3E;
        public const byte InterfaceSetMode = 0x3F;
        public const byte DeviceVerify = 0x40;

        public static string Describe(byte cmd)
        {
            switch (cmd)
            {
                case Run: return "RUN 退出引导";
                case ProgFlash: return "PROG_FLASH 写 Flash";
                case EraseFlash: return "ERASE_FLASH 擦除";
                case ReadFlashSil: return "READ_FLASH";
                case ReadEeprom: return "READ_EEPROM 读 EEPROM";
                case ProgEeprom: return "PROG_EEPROM 写 EEPROM";
                case ReadSram: return "READ_SRAM";
                case BootInit: return "BOOT_INIT";
                case BootSign: return "BOOT_SIGN";
                case KeepAlive: return "KEEP_ALIVE";
                case SetAddress: return "SET_ADDRESS 设地址";
                case SetBuffer: return "SET_BUFFER 设缓冲";
                case RemoteEscape: return "REMOTE_ESCAPE 应答前缀";
                case LocalEscape: return "LOCAL_ESCAPE 请求前缀";
                case TestAlive: return "TEST_ALIVE 测试在线";
                case ProtocolGetVersion: return "GET_PROTOCOL_VERSION";
                case InterfaceGetName: return "GET_INTERFACE_NAME";
                case InterfaceGetVersion: return "GET_INTERFACE_VERSION";
                case InterfaceExit: return "INTERFACE_EXIT";
                case DeviceReset: return "DEVICE_RESET 复位";
                case DeviceInitFlash: return "DEVICE_INIT_FLASH";
                case DeviceEraseAll: return "DEVICE_ERASE_ALL";
                case DevicePageErase: return "DEVICE_PAGE_ERASE";
                case DeviceRead: return "DEVICE_READ 读 Flash";
                case DeviceWrite: return "DEVICE_WRITE 写 Flash";
                case DeviceReadEeprom: return "DEVICE_READ_EEPROM 读 EEPROM";
                case DeviceWriteEeprom: return "DEVICE_WRITE_EEPROM 写 EEPROM";
                case InterfaceSetMode: return "SET_MODE";
                case DeviceVerify: return "DEVICE_VERIFY";
                default: return "CMD 0x" + cmd.ToString("X2");
            }
        }
    }

    /// <summary>ACK codes of the 4-way protocol.</summary>
    public static class FourWayAck
    {
        public const byte Ok = 0x00;
        public const byte InvalidCmd = 0x02;
        public const byte InvalidCrc = 0x03;
        public const byte VerifyError = 0x04;
        public const byte InvalidChannel = 0x08;
        public const byte InvalidParam = 0x09;
        public const byte GeneralError = 0x0F;

        public static string Describe(byte ack)
        {
            switch (ack)
            {
                case Ok: return "OK";
                case InvalidCmd: return "无效命令";
                case InvalidCrc: return "CRC 错误";
                case VerifyError: return "校验失败";
                case InvalidChannel: return "通道无效";
                case InvalidParam: return "参数无效";
                case GeneralError: return "一般错误";
                default: return "未知 ACK(0x" + ack.ToString("X2") + ")";
            }
        }

        public static bool IsSuccess(byte ack)
        {
            return ack == Ok;
        }
    }

    /// <summary>Parsed 4-way interface reply.</summary>
    public sealed class FourWayReply
    {
        public byte Cmd;
        public byte AddrHi;
        public byte AddrLo;
        public byte[] Data = new byte[0];
        public byte Ack;
        public bool CrcOk;
        public byte[] Raw = new byte[0];

        public int Address
        {
            get { return (AddrHi << 8) | AddrLo; }
        }

        public bool AckOk
        {
            get { return FourWayAck.IsSuccess(Ack); }
        }

        public override string ToString()
        {
            return string.Format("CMD=0x{0:X2} ADDR=0x{1:X4} LEN={2} ACK=0x{3:X2} CRC={4}",
                Cmd, Address, Data.Length, Ack, CrcOk ? "OK" : "BAD");
        }
    }

    /// <summary>Framing helpers. No transport access here, pure functions.</summary>
    public static class FourWayCodec
    {
        /// <summary>
        /// Build the container frame used by the board bridge:
        /// [ESC][CMD][LEN][PARAM...][CRC_Hi][CRC_Lo], CRC16-XMODEM over ESC..PARAM.
        /// </summary>
        public static byte[] BuildContainer(byte esc, byte cmd, byte[] param)
        {
            int paramLen = param == null ? 0 : param.Length;
            if (paramLen > 255)
            {
                throw new ArgumentOutOfRangeException("param", "4way 帧参数超过 255 字节");
            }
            byte[] frame = new byte[paramLen + 5];
            frame[0] = esc;
            frame[1] = cmd;
            frame[2] = (byte)paramLen;
            if (paramLen > 0)
            {
                Array.Copy(param, 0, frame, 3, paramLen);
            }
            ushort crc = Crc16.Xmodem(frame, 0, 3 + paramLen);
            frame[3 + paramLen] = (byte)((crc >> 8) & 0xFF);
            frame[4 + paramLen] = (byte)(crc & 0xFF);
            return frame;
        }

        /// <summary>Validate a container frame and split it. Returns null if invalid.</summary>
        public static byte[] ParseContainer(byte[] frame, out byte esc, out byte cmd, out string error)
        {
            esc = 0;
            cmd = 0;
            error = null;
            if (frame == null || frame.Length < 5)
            {
                error = "容器帧长度不足";
                return null;
            }
            int paramLen = frame[2];
            if (frame.Length != paramLen + 5)
            {
                error = string.Format("容器帧长度 {0} 与 LEN 字段 {1} 不符", frame.Length, paramLen);
                return null;
            }
            ushort expected = (ushort)((frame[3 + paramLen] << 8) | frame[4 + paramLen]);
            ushort actual = Crc16.Xmodem(frame, 0, 3 + paramLen);
            if (expected != actual)
            {
                error = string.Format("容器帧 CRC 错误: 帧内 0x{0:X4} 计算 0x{1:X4}", expected, actual);
                return null;
            }
            esc = frame[0];
            cmd = frame[1];
            byte[] param = new byte[paramLen];
            Array.Copy(frame, 3, param, 0, paramLen);
            return param;
        }

        /// <summary>
        /// Build a BLHeliSuite / ESC-Configurator interface frame:
        /// [0x2F][CMD][ADDR_H][ADDR_L][LEN][PARAM...][CRC_Hi][CRC_Lo],
        /// CRC16-XMODEM over everything before the CRC.
        /// </summary>
        public static byte[] BuildInterface(byte cmd, int address, byte[] param)
        {
            int paramLen = param == null ? 0 : param.Length;
            if (paramLen > 255)
            {
                throw new ArgumentOutOfRangeException("param", "4way 接口帧参数超过 255 字节");
            }
            byte[] frame = new byte[paramLen + 7];
            frame[0] = FourWayCommand.LocalEscape;
            frame[1] = cmd;
            frame[2] = (byte)((address >> 8) & 0xFF);
            frame[3] = (byte)(address & 0xFF);
            frame[4] = (byte)paramLen;
            if (paramLen > 0)
            {
                Array.Copy(param, 0, frame, 5, paramLen);
            }
            ushort crc = Crc16.Xmodem(frame, 0, 5 + paramLen);
            frame[5 + paramLen] = (byte)((crc >> 8) & 0xFF);
            frame[6 + paramLen] = (byte)(crc & 0xFF);
            return frame;
        }

        /// <summary>
        /// Parse a BLHeliSuite interface reply:
        /// [0x2E][CMD][ADDR_H][ADDR_L][LEN][DATA...][ACK][CRC_Hi][CRC_Lo]
        /// </summary>
        public static bool TryParseInterfaceReply(byte[] raw, out FourWayReply reply, out string error)
        {
            reply = null;
            error = null;
            if (raw == null || raw.Length < 7)
            {
                error = "应答长度不足 (" + (raw == null ? 0 : raw.Length) + " 字节)";
                return false;
            }
            if (raw[0] != FourWayCommand.RemoteEscape)
            {
                error = string.Format("应答前缀错误: 0x{0:X2}（应为 0x2E）", raw[0]);
                return false;
            }
            int dataLen = raw[4];
            int expectedLength = 5 + dataLen + 1 + 2;
            if (raw.Length < expectedLength)
            {
                error = string.Format("应答长度 {0} 少于预期 {1}", raw.Length, expectedLength);
                return false;
            }
            FourWayReply parsed = new FourWayReply();
            parsed.Raw = raw;
            parsed.Cmd = raw[1];
            parsed.AddrHi = raw[2];
            parsed.AddrLo = raw[3];
            parsed.Data = new byte[dataLen];
            if (dataLen > 0)
            {
                Array.Copy(raw, 5, parsed.Data, 0, dataLen);
            }
            parsed.Ack = raw[5 + dataLen];
            ushort expectedCrc = (ushort)((raw[5 + dataLen + 1] << 8) | raw[5 + dataLen + 2]);
            ushort actualCrc = Crc16.Xmodem(raw, 0, 5 + dataLen + 1);
            parsed.CrcOk = expectedCrc == actualCrc;
            reply = parsed;
            return true;
        }

        /// <summary>
        /// Build the raw BLHeli boot-loader wire frame:
        /// [CMD][LEN][PARAM...][CRC_Lo][CRC_Hi], CRC16-ARC (poly 0xA001, init 0)
        /// over CMD..PARAM.  Only needed for diagnostics / raw mode.
        /// </summary>
        public static byte[] BuildBootloaderFrame(byte cmd, byte[] param)
        {
            int paramLen = param == null ? 0 : param.Length;
            byte[] frame = new byte[paramLen + 4];
            frame[0] = cmd;
            frame[1] = (byte)paramLen;
            if (paramLen > 0)
            {
                Array.Copy(param, 0, frame, 2, paramLen);
            }
            ushort crc = Crc16.Arc(frame, 0, 2 + paramLen);
            frame[2 + paramLen] = (byte)(crc & 0xFF);
            frame[3 + paramLen] = (byte)((crc >> 8) & 0xFF);
            return frame;
        }

        /// <summary>Extract the boot-loader payload from a reply containing 2 CRC bytes + ACK.</summary>
        public static byte[] StripBootloaderTail(byte[] raw, int expectedDataLength, out byte ack)
        {
            ack = 0xFF;
            if (raw == null || raw.Length == 0)
            {
                return new byte[0];
            }
            if (raw.Length >= expectedDataLength + 3)
            {
                ack = raw[expectedDataLength + 2];
            }
            else if (raw.Length >= expectedDataLength + 1)
            {
                ack = raw[raw.Length - 1];
            }
            byte[] data = new byte[expectedDataLength];
            Array.Copy(raw, 0, data, 0, expectedDataLength);
            return data;
        }
    }
}
