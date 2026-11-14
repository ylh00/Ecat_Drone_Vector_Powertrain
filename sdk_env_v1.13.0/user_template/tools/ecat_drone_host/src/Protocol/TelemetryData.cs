/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * TelemetryData.cs - 54 byte telemetry blob + status/fault bit decoding
 *
 * Wire layout (packed, little endian) - see host_telemetry_t in inc/host_proto.h:
 *   0  u16 status_word
 *   2  u16 fault_code
 *   4  u32 esc_erpm[2]            (raw electrical rpm from BDShot, 0 = stopped)
 *   12 u16 esc_tlm_period_us[2]   (raw eRPM period, 0 = no telemetry)
 *   16 i16 servo_pos_d10[2]       (0.1 deg)
 *   20 i16 servo_speed_rpm[2]
 *   24 i16 servo_current_ma[2]
 *   28 u8  servo_temp_c[2]
 *   30 u8  servo_error[2]
 *   32 u16 sys_voltage_dv         (0.1 V)
 *   34 i16 sys_current_ca         (0.01 A)
 *   36 u16 esc_current_ma[2]
 *   40 u16 loop_counter
 *   42 u16 led_status
 *   44 u16 ecat_link_status       (bit0/1 = port0/1 link, bit4..7 = AL state)
 *   46 u32 host_rx_frames
 *   50 u32 host_crc_errors
 *   54 end
 */

using System;
using System.Collections.Generic;
using System.Text;

namespace EcatDroneHost.Protocol
{
    /// <summary>Decoded GET_TELEMETRY response.</summary>
    public sealed class TelemetryData
    {
        public const int WireSize = 54;

        public ushort StatusWord;
        public ushort FaultCode;
        public uint[] EscErpm = new uint[2];
        public ushort[] EscTlmPeriodUs = new ushort[2];
        public short[] ServoPosD10 = new short[2];
        public short[] ServoSpeedRpm = new short[2];
        public short[] ServoCurrentMa = new short[2];
        public byte[] ServoTempC = new byte[2];
        public byte[] ServoError = new byte[2];
        public ushort SysVoltageDv;
        public short SysCurrentCa;
        public ushort[] EscCurrentMa = new ushort[2];
        public ushort LoopCounter;
        public ushort LedStatus;
        public ushort EcatLinkStatus;
        public uint HostRxFrames;
        public uint HostCrcErrors;

        /// <summary>Number of bytes actually received (a real 54 byte packet sets this to 54).</summary>
        public int RawLength;
        /// <summary>False when the blob was shorter than 54 bytes (older / buggy firmware).</summary>
        public bool Complete;

        /// <summary>Raw telemetry blob as received (for the hex view in the UI).</summary>
        public byte[] Raw = new byte[0];

        public static TelemetryData Parse(byte[] blob)
        {
            if (blob == null)
            {
                throw new HostException("遥测数据为空");
            }
            TelemetryData t = new TelemetryData();
            t.RawLength = blob.Length;
            t.Complete = blob.Length >= WireSize;
            t.Raw = new byte[blob.Length];
            Array.Copy(blob, t.Raw, blob.Length);
            if (blob.Length < 16)
            {
                throw new HostException("遥测数据长度不足: " + blob.Length);
            }
            t.StatusWord = Bytes.GetU16(blob, 0);
            t.FaultCode = Bytes.GetU16(blob, 2);
            t.EscErpm[0] = Bytes.GetU32(blob, 4);
            t.EscErpm[1] = Bytes.GetU32(blob, 8);
            if (blob.Length >= 20)
            {
                t.EscTlmPeriodUs[0] = Bytes.GetU16(blob, 12);
                t.EscTlmPeriodUs[1] = Bytes.GetU16(blob, 14);
                t.ServoPosD10[0] = Bytes.GetI16(blob, 16);
                t.ServoPosD10[1] = Bytes.GetI16(blob, 18);
            }
            if (blob.Length >= 28)
            {
                t.ServoSpeedRpm[0] = Bytes.GetI16(blob, 20);
                t.ServoSpeedRpm[1] = Bytes.GetI16(blob, 22);
                t.ServoCurrentMa[0] = Bytes.GetI16(blob, 24);
                t.ServoCurrentMa[1] = Bytes.GetI16(blob, 26);
            }
            if (blob.Length >= 32)
            {
                t.ServoTempC[0] = blob[28];
                t.ServoTempC[1] = blob[29];
                t.ServoError[0] = blob[30];
                t.ServoError[1] = blob[31];
            }
            if (blob.Length >= 40)
            {
                t.SysVoltageDv = Bytes.GetU16(blob, 32);
                t.SysCurrentCa = Bytes.GetI16(blob, 34);
                t.EscCurrentMa[0] = Bytes.GetU16(blob, 36);
                t.EscCurrentMa[1] = Bytes.GetU16(blob, 38);
            }
            if (blob.Length >= 46)
            {
                t.LoopCounter = Bytes.GetU16(blob, 40);
                t.LedStatus = Bytes.GetU16(blob, 42);
                t.EcatLinkStatus = Bytes.GetU16(blob, 44);
            }
            if (blob.Length >= 50)
            {
                t.HostRxFrames = Bytes.GetU32(blob, 46);
            }
            if (blob.Length >= 54)
            {
                t.HostCrcErrors = Bytes.GetU32(blob, 50);
            }
            return t;
        }

        /* -------- derived values -------------------------------------------------- */

        /// <summary>Raw electrical rpm reported by BDShot (0 = motor stopped).</summary>
        public double GetErpm(int esc)
        {
            return EscErpm[esc];
        }

        /// <summary>
        /// Mechanical rpm = electrical rpm / pole pairs.
        /// (USB_PROTOCOL.md §3: mechanical speed = esc_erpm / PARAM_DSHOT_POLE_PAIRS)
        /// </summary>
        public double GetMechanicalRpm(int esc, int polePairs)
        {
            if (polePairs <= 0)
            {
                polePairs = 1;
            }
            return EscErpm[esc] / (double)polePairs;
        }

        public double SysVoltageVolt
        {
            get { return SysVoltageDv / 10.0; }
        }

        public double SysCurrentAmp
        {
            get { return SysCurrentCa / 100.0; }
        }

        public double ServoPosDeg(int ch)
        {
            return ServoPosD10[ch] / 10.0;
        }

        /* -------- status_word / fault / link decoding ----------------------------- */

        public bool StatusBit(int bit)
        {
            return (StatusWord & (1 << bit)) != 0;
        }

        public bool HasFault
        {
            get { return FaultCode != 0; }
        }

        public int EcatAlState
        {
            get { return (EcatLinkStatus >> 4) & 0x0F; }
        }

        public bool EcatPort0Link
        {
            get { return (EcatLinkStatus & 0x0001) != 0; }
        }

        public bool EcatPort1Link
        {
            get { return (EcatLinkStatus & 0x0002) != 0; }
        }

        /// <summary>List of set fault bits as human readable text.</summary>
        public List<string> GetFaultTexts()
        {
            List<string> list = new List<string>();
            for (int i = 0; i < FaultBits.Length; i++)
            {
                if ((FaultCode & FaultBits[i].Mask) != 0)
                {
                    list.Add(FaultBits[i].Chinese);
                }
            }
            return list;
        }

        public List<string> GetStatusTexts()
        {
            List<string> list = new List<string>();
            for (int i = 0; i < StatusBits.Length; i++)
            {
                if ((StatusWord & StatusBits[i].Mask) != 0)
                {
                    list.Add(StatusBits[i].Chinese);
                }
            }
            return list;
        }

        public static string DescribeEcatAlState(int alState)
        {
            switch (alState)
            {
                case 0: return "未知/未启动";
                case 1: return "INIT (初始化)";
                case 2: return "PRE-OP (预运行)";
                case 3: return "BOOT (引导)";
                case 4: return "SAFE-OP (安全运行)";
                case 8: return "OP (正常运行)";
                default: return "AL=" + alState;
            }
        }

        public static string DescribeLedStatus(ushort ledStatus)
        {
            /* led_status is a firmware defined status code; show the raw value and
             * a best effort decode of the low byte (documented in HOST_TOOL.md). */
            return "0x" + ledStatus.ToString("X4");
        }

        /// <summary>A bit definition with English and Chinese labels.</summary>
        public sealed class BitDef
        {
            public ushort Mask;
            public string English;
            public string Chinese;

            public BitDef(ushort mask, string english, string chinese)
            {
                Mask = mask;
                English = english;
                Chinese = chinese;
            }
        }

        /// <summary>app_fault_t bits from inc/ecat_pdo.h.</summary>
        public static readonly BitDef[] FaultBits = new BitDef[]
        {
            new BitDef(0x0001, "ESC1_NO_TLM", "ESC1 无遥测"),
            new BitDef(0x0002, "ESC2_NO_TLM", "ESC2 无遥测"),
            new BitDef(0x0004, "ESC1_NO_CMD", "ESC1 无控制指令"),
            new BitDef(0x0008, "ESC2_NO_CMD", "ESC2 无控制指令"),
            new BitDef(0x0010, "SERVO1_OFFLINE", "舵机1 离线"),
            new BitDef(0x0020, "SERVO2_OFFLINE", "舵机2 离线"),
            new BitDef(0x0040, "SERVO1_ERROR", "舵机1 报错"),
            new BitDef(0x0080, "SERVO2_ERROR", "舵机2 报错"),
            new BitDef(0x0100, "OVER_VOLTAGE", "系统过压"),
            new BitDef(0x0200, "UNDER_VOLTAGE", "系统欠压"),
            new BitDef(0x0400, "OVER_CURRENT_SYS", "系统过流"),
            new BitDef(0x0800, "OVER_CURRENT_ESC1", "ESC1 过流"),
            new BitDef(0x1000, "OVER_CURRENT_ESC2", "ESC2 过流"),
            new BitDef(0x2000, "ECAT_OP_TIMEOUT", "EtherCAT OP 超时"),
        };

        /// <summary>status_word bits (ECAT_STS_*).</summary>
        public static readonly BitDef[] StatusBits = new BitDef[]
        {
            new BitDef(0x0001, "ECAT_OP", "EtherCAT 处于 OP"),
            new BitDef(0x0002, "OUTPUT_ENABLED", "输出使能"),
            new BitDef(0x0004, "FAULT_ACTIVE", "故障激活"),
            new BitDef(0x0008, "ESC1_TLM_OK", "ESC1 遥测正常"),
            new BitDef(0x0010, "ESC2_TLM_OK", "ESC2 遥测正常"),
            new BitDef(0x0020, "SERVO1_ONLINE", "舵机1 在线"),
            new BitDef(0x0040, "SERVO2_ONLINE", "舵机2 在线"),
            new BitDef(0x0080, "ADC_ALARM", "ADC 报警"),
            new BitDef(0x0100, "LED_TEST", "LED 测试"),
        };

        public string ToSummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("status=0x").Append(StatusWord.ToString("X4"));
            sb.Append(" fault=0x").Append(FaultCode.ToString("X4"));
            sb.Append(" AL=").Append(DescribeEcatAlState(EcatAlState));
            sb.Append(" p0=").Append(EcatPort0Link ? " link" : " down");
            sb.Append(" p1=").Append(EcatPort1Link ? " link" : " down");
            sb.Append(" V=").Append(SysVoltageVolt.ToString("F1")).Append('V');
            sb.Append(" I=").Append(SysCurrentAmp.ToString("F2")).Append('A');
            sb.Append(" eRPM=").Append(GetErpm(0).ToString("F0")).Append('/').Append(GetErpm(1).ToString("F0"));
            return sb.ToString();
        }
    }

    /// <summary>GET_ECAT_STATUS response (u8 al_state, u8 link, u16 fault, u32 cycles).</summary>
    public sealed class EcatStatus
    {
        public byte AlState;
        public byte Link;
        public ushort Fault;
        public uint Cycles;

        public static EcatStatus Parse(byte[] body)
        {
            if (body == null || body.Length < 8)
            {
                throw new HostException("ECAT 状态长度不足: " + (body == null ? 0 : body.Length));
            }
            EcatStatus status = new EcatStatus();
            status.AlState = body[0];
            status.Link = body[1];
            status.Fault = Bytes.GetU16(body, 2);
            status.Cycles = Bytes.GetU32(body, 4);
            return status;
        }

        public bool Port0Link { get { return (Link & 0x01) != 0; } }
        public bool Port1Link { get { return (Link & 0x02) != 0; } }

        public override string ToString()
        {
            return string.Format("AL={0} link=0x{1:X2} fault=0x{2:X4} cycles={3}",
                TelemetryData.DescribeEcatAlState(AlState), Link, Fault, Cycles);
        }
    }

    /// <summary>Firmware version info (GET_VERSION).</summary>
    public sealed class VersionInfo
    {
        public ushort Major;
        public ushort Minor;
        public ushort Patch;
        public string BuildDate = string.Empty;

        public static VersionInfo Parse(byte[] body)
        {
            if (body == null || body.Length < 6)
            {
                throw new HostException("版本信息长度不足: " + (body == null ? 0 : body.Length));
            }
            VersionInfo v = new VersionInfo();
            v.Major = Bytes.GetU16(body, 0);
            v.Minor = Bytes.GetU16(body, 2);
            v.Patch = Bytes.GetU16(body, 4);
            if (body.Length > 6)
            {
                int max = Math.Min(16, body.Length - 6);
                int length = 0;
                while (length < max && body[6 + length] != 0)
                {
                    length++;
                }
                if (length > 0)
                {
                    v.BuildDate = Encoding.ASCII.GetString(body, 6, length).Trim();
                }
            }
            return v;
        }

        public override string ToString()
        {
            return string.Format("v{0}.{1}.{2} ({3})", Major, Minor, Patch, BuildDate);
        }
    }
}
