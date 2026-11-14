/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * BoardData.cs - decoding of the per-module response blobs.
 *
 * The blobs mirror the firmware structs that the dispatcher serializes:
 *   DSHOT_GET_TLM   -> dshot_telemetry_t  (inc/dshot.h)
 *   SERVO_READ      -> servo_status_t     (inc/servo_s288.h)
 *   ADC_GET         -> adc_mon_data_t     (inc/adc_mon.h)
 *   AM32_STATUS     -> u8 state, u8 last_error
 * The decoders are tolerant: they accept shorter blobs (older firmware) and
 * only fail when the mandatory prefix is missing.
 */

using System;

namespace EcatDroneHost.Protocol
{
    /// <summary>One ESC telemetry snapshot (dshot_telemetry_t).</summary>
    public sealed class DshotTelemetryData
    {
        public const int WireSize = 26;

        public uint Erpm;
        public ushort PeriodUs;
        public bool Valid;
        public byte State;
        public ushort CrcErrors;
        public uint OkCount;
        public uint ErrCount;
        public uint TimeoutCount;
        public uint LastOkTickMs;

        public static DshotTelemetryData Parse(byte[] body)
        {
            if (body == null || body.Length < 6)
            {
                throw new HostException("DShot 遥测长度不足: " + (body == null ? 0 : body.Length));
            }
            DshotTelemetryData t = new DshotTelemetryData();
            t.Erpm = Bytes.GetU32(body, 0);
            t.PeriodUs = Bytes.GetU16(body, 4);
            t.Valid = body.Length >= 7 && body[6] != 0;
            t.State = body.Length >= 8 ? body[7] : (byte)0;
            if (body.Length >= 10)
            {
                t.CrcErrors = Bytes.GetU16(body, 8);
            }
            if (body.Length >= 14)
            {
                t.OkCount = Bytes.GetU32(body, 10);
            }
            if (body.Length >= 18)
            {
                t.ErrCount = Bytes.GetU32(body, 14);
            }
            if (body.Length >= 22)
            {
                t.TimeoutCount = Bytes.GetU32(body, 18);
            }
            if (body.Length >= 26)
            {
                t.LastOkTickMs = Bytes.GetU32(body, 22);
            }
            return t;
        }

        public string StateName
        {
            get
            {
                switch (State)
                {
                    case 0: return "未初始化";
                    case 1: return "发送";
                    case 2: return "切换接收";
                    case 3: return "等待 ESC";
                    case 4: return "接收";
                    case 5: return "解码";
                    case 6: return "错误";
                    default: return "状态" + State;
                }
            }
        }

        public override string ToString()
        {
            return string.Format("eRPM={0} period={1}us valid={2} ok={3} err={4} timeout={5}",
                Erpm, PeriodUs, Valid ? "是" : "否", OkCount, ErrCount, TimeoutCount);
        }
    }

    /// <summary>One bus servo status snapshot (servo_status_t).</summary>
    public sealed class ServoStatusData
    {
        public const int WireSize = 20;

        public short PosD10;
        public short SpeedRpm;
        public short CurrentMa;
        public ushort VoltageMv;
        public byte TempC;
        public byte Error;
        public byte Online;
        public byte Enabled;
        public ushort OkCount;
        public ushort ErrCount;
        public uint LastOkTickMs;

        public static ServoStatusData Parse(byte[] body)
        {
            if (body == null || body.Length < 8)
            {
                throw new HostException("舵机状态长度不足: " + (body == null ? 0 : body.Length));
            }
            ServoStatusData s = new ServoStatusData();
            s.PosD10 = Bytes.GetI16(body, 0);
            s.SpeedRpm = Bytes.GetI16(body, 2);
            s.CurrentMa = Bytes.GetI16(body, 4);
            s.VoltageMv = Bytes.GetU16(body, 6);
            if (body.Length >= 10)
            {
                s.TempC = body[8];
                s.Error = body[9];
            }
            if (body.Length >= 12)
            {
                s.Online = body[10];
                s.Enabled = body[11];
            }
            if (body.Length >= 16)
            {
                s.OkCount = Bytes.GetU16(body, 12);
                s.ErrCount = Bytes.GetU16(body, 14);
            }
            if (body.Length >= 20)
            {
                s.LastOkTickMs = Bytes.GetU32(body, 16);
            }
            return s;
        }

        public string ErrorText
        {
            get
            {
                if (Error == 0)
                {
                    return "无";
                }
                return "0x" + Error.ToString("X2");
            }
        }

        public override string ToString()
        {
            return string.Format("pos={0:F1}° speed={1}rpm I={2}mA V={3}mV T={4}°C err=0x{5:X2} online={6}",
                PosD10 / 10.0, SpeedRpm, CurrentMa, VoltageMv, TempC, Error, Online);
        }
    }

    /// <summary>ADC monitoring snapshot (host_adc_blob_t, 24 bytes).</summary>
    public sealed class AdcMonitorData
    {
        public const int WireSize = 24;

        public ushort RawSysCurrent;
        public ushort RawSysVoltage;
        public ushort[] RawEscCurrent = new ushort[2];
        public ushort SysVoltageDv;
        public short SysCurrentCa;
        public ushort[] EscCurrentMa = new ushort[2];
        public byte Alarm;
        public byte Reserved0;
        public ushort SampleCount;
        public uint LastUpdateMs;

        public static AdcMonitorData Parse(byte[] body)
        {
            if (body == null || body.Length < 4)
            {
                throw new HostException("ADC 数据长度不足: " + (body == null ? 0 : body.Length));
            }
            AdcMonitorData d = new AdcMonitorData();
            d.RawSysCurrent = Bytes.GetU16(body, 0);
            d.RawSysVoltage = Bytes.GetU16(body, 2);
            if (body.Length >= 8)
            {
                d.RawEscCurrent[0] = Bytes.GetU16(body, 4);
                d.RawEscCurrent[1] = Bytes.GetU16(body, 6);
            }
            if (body.Length >= 12)
            {
                d.SysVoltageDv = Bytes.GetU16(body, 8);
                d.SysCurrentCa = Bytes.GetI16(body, 10);
            }
            if (body.Length >= 16)
            {
                d.EscCurrentMa[0] = Bytes.GetU16(body, 12);
                d.EscCurrentMa[1] = Bytes.GetU16(body, 14);
            }
            if (body.Length >= 17)
            {
                d.Alarm = body[16];
            }
            if (body.Length >= 18)
            {
                d.Reserved0 = body[17];
            }
            if (body.Length >= 20)
            {
                d.SampleCount = Bytes.GetU16(body, 18);
            }
            if (body.Length >= 24)
            {
                d.LastUpdateMs = Bytes.GetU32(body, 20);
            }
            return d;
        }

        public string AlarmText
        {
            get
            {
                if (Alarm == 0)
                {
                    return "无";
                }
                string text = string.Empty;
                if ((Alarm & 0x01) != 0) text += "过压 ";
                if ((Alarm & 0x02) != 0) text += "欠压 ";
                if ((Alarm & 0x04) != 0) text += "系统过流 ";
                if ((Alarm & 0x08) != 0) text += "ESC1 过流 ";
                if ((Alarm & 0x10) != 0) text += "ESC2 过流 ";
                return text.Trim();
            }
        }

        public double SysVoltageVolt { get { return SysVoltageDv / 10.0; } }
        public double SysCurrentAmp { get { return SysCurrentCa / 100.0; } }
    }

    /// <summary>AM32 bridge state (AM32_STATUS response).</summary>
    public sealed class Am32BridgeStatus
    {
        public byte State;
        public byte LastError;

        public static Am32BridgeStatus Parse(byte[] body)
        {
            if (body == null || body.Length < 2)
            {
                throw new HostException("AM32 状态长度不足: " + (body == null ? 0 : body.Length));
            }
            Am32BridgeStatus s = new Am32BridgeStatus();
            s.State = body[0];
            s.LastError = body[1];
            return s;
        }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case 0: return "空闲 (idle)";
                    case 1: return "4way 桥接中 (active)";
                    case 2: return "错误 (error)";
                    default: return "状态 " + State;
                }
            }
        }

        public string ErrorText
        {
            get { return LastError == 0 ? "无" : "0x" + LastError.ToString("X2"); }
        }

        public override string ToString()
        {
            return StateText + " / 错误: " + ErrorText;
        }
    }
}
