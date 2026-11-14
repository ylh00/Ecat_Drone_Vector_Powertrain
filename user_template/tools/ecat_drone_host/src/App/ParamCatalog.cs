/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * ParamCatalog.cs - offline copy of the firmware parameter table (inc/param.h):
 *   id, name, type, range, unit, Chinese label and description, grouping.
 *
 * The real device always wins: values / ranges / descriptions are read from the
 * device through LIST_PARAMS.  This catalog is used for
 *   - nicer Chinese labels and unit formatting in the parameter grid,
 *   - grouping the parameter grid by module (ECAT / DSHOT / SERVO / ADC / ...),
 *   - the in-process firmware simulator used by --selftest / 模拟设备 mode.
 *
 * Keep in sync with inc/param.h.  The id ordering below MUST match the
 * param_id_t enumeration exactly (verified by --selftest).
 */

using System;
using System.Collections.Generic;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.App
{
    /// <summary>Parameter ids exactly as in param_id_t (inc/param.h).</summary>
    public enum ParamId
    {
        EcatNodeAlias = 0,
        EcatDcMode = 1,
        EcatSync0PeriodUs = 2,
        EcatSync1PeriodUs = 3,
        EcatDcShiftNs = 4,
        EcatWatchdogMs = 5,
        EcatSdoTimeoutMs = 6,
        EcatEnableCoe = 7,
        EcatLedInvert = 8,

        DshotRate = 9,
        DshotUpdateHz = 10,
        DshotPolePairs = 11,
        DshotDirection1 = 12,
        DshotDirection2 = 13,
        Dshot3dMode1 = 14,
        Dshot3dMode2 = 15,
        DshotTlmTimeoutUs = 16,
        DshotMinThrottle = 17,
        DshotMaxThrottle = 18,
        DshotStartUs = 19,

        ServoBaud = 20,
        ServoId1 = 21,
        ServoId2 = 22,
        ServoDirTxLevel1 = 23,
        ServoDirTxLevel2 = 24,
        ServoAngleMin1 = 25,
        ServoAngleMin2 = 26,
        ServoAngleMax1 = 27,
        ServoAngleMax2 = 28,
        ServoAngleGain1 = 29,
        ServoAngleGain2 = 30,
        ServoAngleOfs1 = 31,
        ServoAngleOfs2 = 32,
        ServoPollHz = 33,
        ServoTorqueLimit1 = 34,
        ServoTorqueLimit2 = 35,
        ServoEnableOnBoot = 36,

        AdcSysVScale = 37,
        AdcSysVOffsetMv = 38,
        AdcSysIScale = 39,
        AdcSysIOffsetMa = 40,
        AdcEsc1IScale = 41,
        AdcEsc2IScale = 42,
        AdcEscIOffsetMa = 43,
        AdcVoltMinDv = 44,
        AdcVoltMaxDv = 45,
        AdcCurrMaxCa = 46,
        AdcEscCurrMaxMa = 47,
        AdcEnableProtection = 48,

        LedBrightness = 49,
        LedStateMask = 50,

        UsbProtoTimeoutMs = 51,
        UsbAutoTelemetryHz = 52,

        Am32NumRetries = 53,
        Am32Baud = 54,
        Am32EnablePassthrough = 55,

        ServoKp1 = 56,
        ServoKp2 = 57,
        ServoKd1 = 58,
        ServoKd2 = 59,

        Count = 60
    }

    /// <summary>Offline description of one firmware parameter.</summary>
    public sealed class ParamInfo
    {
        public ushort Id;
        public string Name = string.Empty;
        public string NameZh = string.Empty;
        public string DescriptionZh = string.Empty;
        public ParamType Type;
        public int Min;
        public int Max;
        public int Default;
        public bool Persist = true;
        public bool ReadOnly;
        public string Unit = string.Empty;
        public string DisplayUnit = string.Empty;
        public double Scale = 1.0;
        public bool HexDisplay;
        public string Group = string.Empty;
        public string[] Options;

        /// <summary>Format the raw wire value for display, e.g. "125 -> 12.5 °".</summary>
        public string FormatValue(int raw)
        {
            if (Options != null && raw >= 0 && raw < Options.Length)
            {
                return Options[raw] + " (" + raw + ")";
            }
            if (HexDisplay)
            {
                return "0x" + ((uint)raw).ToString("X");
            }
            if (Math.Abs(Scale - 1.0) > 1e-9)
            {
                int decimals = (int)Math.Ceiling(-Math.Log10(Scale));
                if (decimals < 0)
                {
                    decimals = 0;
                }
                if (decimals > 4)
                {
                    decimals = 4;
                }
                double scaled = raw * Scale;
                return scaled.ToString("F" + decimals) +
                       (DisplayUnit.Length > 0 ? " " + DisplayUnit : string.Empty);
            }
            return raw + (DisplayUnit.Length > 0 ? " " + DisplayUnit : string.Empty);
        }

        public override string ToString()
        {
            return Name + " (#" + Id + ", " + Group + ")";
        }
    }

    /// <summary>Static copy of the firmware parameter table.</summary>
    public static class ParamCatalog
    {
        public static readonly string[] GroupOrder = new string[]
        {
            "ECAT", "DSHOT", "SERVO", "ADC", "LED", "USB", "AM32", "其他"
        };

        private static readonly ParamInfo[] _all = Build();
        private static readonly Dictionary<ushort, ParamInfo> _byId = BuildIndex();

        public static ParamInfo[] All
        {
            get { return _all; }
        }

        public static int Count
        {
            get { return _all.Length; }
        }

        public static ParamInfo Find(ushort id)
        {
            ParamInfo info;
            return _byId.TryGetValue(id, out info) ? info : null;
        }

        /// <summary>Default value of a parameter id (int.MinValue when unknown).</summary>
        public static int GetDefault(int id)
        {
            ParamInfo info = Find((ushort)id);
            return info != null ? info.Default : 0;
        }

        /// <summary>Minimum value of a parameter id.</summary>
        public static int GetMin(int id)
        {
            ParamInfo info = Find((ushort)id);
            return info != null ? info.Min : int.MinValue;
        }

        /// <summary>Maximum value of a parameter id.</summary>
        public static int GetMax(int id)
        {
            ParamInfo info = Find((ushort)id);
            return info != null ? info.Max : int.MaxValue;
        }

        /// <summary>Parameter info by numeric id (null when unknown).</summary>
        public static ParamInfo GetInfo(int id)
        {
            return Find((ushort)id);
        }

        public static ParamInfo FindByName(string name)
        {
            if (name == null)
            {
                return null;
            }
            for (int i = 0; i < _all.Length; i++)
            {
                if (string.Equals(_all[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return _all[i];
                }
            }
            return null;
        }

        /// <summary>Group name derived from a device supplied parameter name.</summary>
        public static string GuessGroup(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "其他";
            }
            string upper = name.ToUpperInvariant();
            if (upper.StartsWith("ECAT"))
            {
                return "ECAT";
            }
            if (upper.StartsWith("DSHOT"))
            {
                return "DSHOT";
            }
            if (upper.StartsWith("SERVO"))
            {
                return "SERVO";
            }
            if (upper.StartsWith("ADC"))
            {
                return "ADC";
            }
            if (upper.StartsWith("LED"))
            {
                return "LED";
            }
            if (upper.StartsWith("USB"))
            {
                return "USB";
            }
            if (upper.StartsWith("AM32"))
            {
                return "AM32";
            }
            return "其他";
        }

        private static Dictionary<ushort, ParamInfo> BuildIndex()
        {
            Dictionary<ushort, ParamInfo> map = new Dictionary<ushort, ParamInfo>(_all.Length);
            for (int i = 0; i < _all.Length; i++)
            {
                map[_all[i].Id] = _all[i];
            }
            return map;
        }

        private static ParamInfo P(ParamId id, string name, ParamType type, int min, int max,
                                   int def, string nameZh, string descZh)
        {
            ParamInfo info = new ParamInfo();
            info.Id = (ushort)id;
            info.Name = name;
            info.Type = type;
            info.Min = min;
            info.Max = max;
            info.Default = def;
            info.NameZh = nameZh;
            info.DescriptionZh = descZh;
            info.Group = GuessGroup(name);
            return info;
        }

        private static ParamInfo[] Build()
        {
            List<ParamInfo> list = new List<ParamInfo>();

            /* ---- EtherCAT ---- */
            list.Add(P(ParamId.EcatNodeAlias, "ECAT_NODE_ALIAS", ParamType.U16, 0, 65535, 0,
                "EtherCAT 节点别名", "站别名 (station alias)，0 = 未使用"));
            list.Add(P(ParamId.EcatDcMode, "ECAT_DC_MODE", ParamType.U8, 0, 3, 0,
                "EtherCAT 分布式时钟模式", "0=关闭, 1=SYNC0, 2=SYNC1, 3=DC SYNC0"));
            list.Add(P(ParamId.EcatSync0PeriodUs, "ECAT_SYNC0_PERIOD_US", ParamType.U32, 125, 10000000, 1000,
                "SYNC0 周期", "SYNC0 同步周期，单位 µs（125..10000000）"));
            list.Add(P(ParamId.EcatSync1PeriodUs, "ECAT_SYNC1_PERIOD_US", ParamType.U32, 125, 10000000, 1000,
                "SYNC1 周期", "SYNC1 同步周期，单位 µs（125..10000000）"));
            list.Add(P(ParamId.EcatDcShiftNs, "ECAT_DC_SHIFT_NS", ParamType.I32, -1000000, 1000000, 0,
                "DC 相位偏移", "分布式时钟相位偏移，单位 ns（-1000000..1000000）"));
            list.Add(P(ParamId.EcatWatchdogMs, "ECAT_WATCHDOG_MS", ParamType.U16, 10, 1000, 100,
                "主站数据看门狗", "过程数据看门狗超时，单位 ms（10..1000）"));
            list.Add(P(ParamId.EcatSdoTimeoutMs, "ECAT_SDO_TIMEOUT_MS", ParamType.U16, 100, 60000, 1000,
                "SDO 超时", "SDO 访问超时，单位 ms（100..60000）"));
            list.Add(P(ParamId.EcatEnableCoe, "ECAT_ENABLE_COE", ParamType.U8, 0, 1, 1,
                "使能 CoE (SDO/PDO)", "0=关闭, 1=使能；关闭后只能使用过程数据"));
            list.Add(P(ParamId.EcatLedInvert, "ECAT_LED_INVERT", ParamType.U8, 0, 1, 0,
                "状态灯取反", "0=正常, 1=RGB 状态灯电平取反"));

            /* ---- DShot ---- */
            list.Add(P(ParamId.DshotRate, "DSHOT_RATE", ParamType.U16, 150, 1200, 600,
                "DShot 速率", "仅允许 150 / 300 / 600 / 1200 kbit/s；双向回传建议 600"));
            list.Add(P(ParamId.DshotUpdateHz, "DSHOT_UPDATE_HZ", ParamType.U16, 200, 8000, 1000,
                "DShot 更新频率", "每秒发送的命令帧数（200..8000 Hz）"));
            list.Add(P(ParamId.DshotPolePairs, "DSHOT_POLE_PAIRS", ParamType.U16, 1, 32, 7,
                "电机极对数", "eRPM 换算机械转速用（1..32）"));
            list.Add(P(ParamId.DshotDirection1, "DSHOT_DIRECTION1", ParamType.U8, 0, 1, 0,
                "ESC1 转向", "0=正转, 1=反转"));
            list.Add(P(ParamId.DshotDirection2, "DSHOT_DIRECTION2", ParamType.U8, 0, 1, 0,
                "ESC2 转向", "0=正转, 1=反转"));
            list.Add(P(ParamId.Dshot3dMode1, "DSHOT_3D_MODE1", ParamType.U8, 0, 1, 0,
                "ESC1 3D 模式", "0=普通, 1=3D 双向模式"));
            list.Add(P(ParamId.Dshot3dMode2, "DSHOT_3D_MODE2", ParamType.U8, 0, 1, 0,
                "ESC2 3D 模式", "0=普通, 1=3D 双向模式"));
            list.Add(P(ParamId.DshotTlmTimeoutUs, "DSHOT_TLM_TIMEOUT_US", ParamType.U16, 20, 200, 70,
                "遥测等待超时", "等待 ESC 回传的最长时间，单位 µs（20..200）"));
            list.Add(P(ParamId.DshotMinThrottle, "DSHOT_MIN_THROTTLE", ParamType.U16, 0, 200, 48,
                "最小油门", "DShot 原始油门下限（0..200）"));
            list.Add(P(ParamId.DshotMaxThrottle, "DSHOT_MAX_THROTTLE", ParamType.U16, 1800, 2047, 2047,
                "最大油门", "DShot 原始油门上限（1800..2047）"));
            list.Add(P(ParamId.DshotStartUs, "DSHOT_START_US", ParamType.U16, 800, 1200, 1000,
                "解锁油门", "解锁/怠速时发送的油门，单位 µs（800..1200）"));

            /* ---- Servo ---- */
            list.Add(P(ParamId.ServoBaud, "SERVO_BAUD", ParamType.U32, 9600, 6000000, 6000000,
                "舵机波特率", "S288 总线波特率（9600..6000000），J288/S288 固定 6 Mbit/s"));
            list.Add(P(ParamId.ServoId1, "SERVO_ID1", ParamType.U8, 0, 253, 1,
                "舵机1 ID", "总线舵机 1 的 ID（0..253）"));
            list.Add(P(ParamId.ServoId2, "SERVO_ID2", ParamType.U8, 0, 253, 2,
                "舵机2 ID", "总线舵机 2 的 ID（0..253）"));
            list.Add(P(ParamId.ServoDirTxLevel1, "SERVO_DIR_TX_LEVEL1", ParamType.U8, 0, 1, 1,
                "舵机1 方向切换电平", "SPDT 开关发送态电平：0=低, 1=高"));
            list.Add(P(ParamId.ServoDirTxLevel2, "SERVO_DIR_TX_LEVEL2", ParamType.U8, 0, 1, 1,
                "舵机2 方向切换电平", "SPDT 开关发送态电平：0=低, 1=高"));
            list.Add(P(ParamId.ServoAngleMin1, "SERVO_ANGLE_MIN1", ParamType.I16, -36000, 36000, -1800,
                "舵机1 最小角度", "角度下限，单位 0.1°（-36000..36000）"));
            list.Add(P(ParamId.ServoAngleMin2, "SERVO_ANGLE_MIN2", ParamType.I16, -36000, 36000, -1800,
                "舵机2 最小角度", "角度下限，单位 0.1°（-36000..36000）"));
            list.Add(P(ParamId.ServoAngleMax1, "SERVO_ANGLE_MAX1", ParamType.I16, -36000, 36000, 1800,
                "舵机1 最大角度", "角度上限，单位 0.1°（-36000..36000）"));
            list.Add(P(ParamId.ServoAngleMax2, "SERVO_ANGLE_MAX2", ParamType.I16, -36000, 36000, 1800,
                "舵机2 最大角度", "角度上限，单位 0.1°（-36000..36000）"));
            list.Add(P(ParamId.ServoAngleGain1, "SERVO_ANGLE_GAIN1", ParamType.I16, 10, 10000, 100,
                "舵机1 角度增益", "100 = 1.00x（10..10000）"));
            list.Add(P(ParamId.ServoAngleGain2, "SERVO_ANGLE_GAIN2", ParamType.I16, 10, 10000, 100,
                "舵机2 角度增益", "100 = 1.00x（10..10000）"));
            list.Add(P(ParamId.ServoAngleOfs1, "SERVO_ANGLE_OFS1", ParamType.I16, -18000, 18000, 0,
                "舵机1 角度零点偏移", "角度补偿，单位 0.1°（-18000..18000）"));
            list.Add(P(ParamId.ServoAngleOfs2, "SERVO_ANGLE_OFS2", ParamType.I16, -18000, 18000, 0,
                "舵机2 角度零点偏移", "角度补偿，单位 0.1°（-18000..18000）"));
            list.Add(P(ParamId.ServoPollHz, "SERVO_POLL_HZ", ParamType.U16, 10, 1000, 100,
                "舵机轮询频率", "状态读取频率，单位 Hz（10..1000）"));
            list.Add(P(ParamId.ServoTorqueLimit1, "SERVO_TORQUE_LIMIT1", ParamType.U8, 0, 100, 100,
                "舵机1 扭矩限制", "输出扭矩限制百分比（0..100）"));
            list.Add(P(ParamId.ServoTorqueLimit2, "SERVO_TORQUE_LIMIT2", ParamType.U8, 0, 100, 100,
                "舵机2 扭矩限制", "输出扭矩限制百分比（0..100）"));
            list.Add(P(ParamId.ServoEnableOnBoot, "SERVO_ENABLE_ON_BOOT", ParamType.U8, 0, 1, 0,
                "上电即使能舵机", "0=上电不使能, 1=上电即使能（注意安全）"));

            /* ---- ADC ---- */
            list.Add(P(ParamId.AdcSysVScale, "ADC_SYS_V_SCALE", ParamType.I32, 1, 1000000, 10000,
                "系统电压换算系数", "每 LSB 对应电压，单位 µV（例如 10000 = 10.0 mV/LSB）"));
            list.Add(P(ParamId.AdcSysVOffsetMv, "ADC_SYS_V_OFFSET_MV", ParamType.I32, -100000, 100000, 0,
                "系统电压零偏", "电压零点偏移，单位 mV"));
            list.Add(P(ParamId.AdcSysIScale, "ADC_SYS_I_SCALE", ParamType.I32, 1, 1000000, 1000,
                "系统电流换算系数", "每 LSB 对应电流，单位 µA/LSB"));
            list.Add(P(ParamId.AdcSysIOffsetMa, "ADC_SYS_I_OFFSET_MA", ParamType.I32, -100000, 100000, 0,
                "系统电流零偏", "电流零点偏移，单位 mA"));
            list.Add(P(ParamId.AdcEsc1IScale, "ADC_ESC1_I_SCALE", ParamType.I32, 1, 1000000, 1000,
                "ESC1 电流换算系数", "每 LSB 对应电流，单位 µA/LSB"));
            list.Add(P(ParamId.AdcEsc2IScale, "ADC_ESC2_I_SCALE", ParamType.I32, 1, 1000000, 1000,
                "ESC2 电流换算系数", "每 LSB 对应电流，单位 µA/LSB"));
            list.Add(P(ParamId.AdcEscIOffsetMa, "ADC_ESC_I_OFFSET_MA", ParamType.I32, -100000, 100000, 0,
                "ESC 电流零偏", "ESC 电流零点偏移，单位 mA"));
            list.Add(P(ParamId.AdcVoltMinDv, "ADC_VOLT_MIN_DV", ParamType.U16, 0, 6000, 180,
                "欠压保护阈值", "系统欠压跳闸值，单位 0.1 V（例如 180 = 18.0 V）"));
            list.Add(P(ParamId.AdcVoltMaxDv, "ADC_VOLT_MAX_DV", ParamType.U16, 0, 6000, 520,
                "过压保护阈值", "系统过压跳闸值，单位 0.1 V（例如 520 = 52.0 V）"));
            list.Add(P(ParamId.AdcCurrMaxCa, "ADC_CURR_MAX_CA", ParamType.I16, 0, 30000, 3000,
                "系统过流阈值", "系统过流跳闸值，单位 0.01 A（例如 3000 = 30.00 A）"));
            list.Add(P(ParamId.AdcEscCurrMaxMa, "ADC_ESC_CURR_MAX_MA", ParamType.U16, 0, 65535, 2000,
                "ESC 过流阈值", "单路 ESC 过流跳闸值，单位 mA"));
            list.Add(P(ParamId.AdcEnableProtection, "ADC_ENABLE_PROTECTION", ParamType.U8, 0, 1, 1,
                "使能 ADC 保护", "0=只监测不动作, 1=越限时置故障位"));

            /* ---- LED ---- */
            list.Add(P(ParamId.LedBrightness, "LED_BRIGHTNESS", ParamType.U16, 0, 255, 128,
                "RGB 亮度", "状态灯亮度（0..255）"));
            list.Add(P(ParamId.LedStateMask, "LED_STATE_MASK", ParamType.U32, -2147483648, 2147483647, 0x0000FFFF,
                "状态灯动画掩码", "选择显示哪些状态动画，按位掩码（十六进制）"));
            ParamInfo led = FindById(list, ParamId.LedStateMask);
            led.HexDisplay = true;

            /* ---- USB ---- */
            list.Add(P(ParamId.UsbProtoTimeoutMs, "USB_PROTO_TIMEOUT_MS", ParamType.U16, 100, 10000, 1000,
                "协议超时", "USB 主机协议超时，单位 ms（100..10000）"));
            list.Add(P(ParamId.UsbAutoTelemetryHz, "USB_AUTO_TELEMETRY_HZ", ParamType.U16, 0, 1000, 0,
                "自动遥测频率", "0 = 仅按请求发送；>0 时设备主动推送遥测，单位 Hz"));

            /* ---- AM32 ---- */
            list.Add(P(ParamId.Am32NumRetries, "AM32_4WAY_NUM_RETRIES", ParamType.U8, 0, 10, 2,
                "4way 重试次数", "AM32 4-way 通信失败重试次数（0..10）"));
            list.Add(P(ParamId.Am32Baud, "AM32_4WAY_BAUD", ParamType.U32, 9600, 1000000, 19200,
                "4way 波特率", "AM32 4-way 位操作波特率（9600..1000000），标准 19200"));
            list.Add(P(ParamId.Am32EnablePassthrough, "AM32_ENABLE_PASSTHROUGH", ParamType.U8, 0, 1, 1,
                "允许 PC 透传 AM32", "0=禁止, 1=允许上位机通过板子配置 AM32"));

            /* ---- S288 position / speed loop gains (appended, ids 56..59) ---- */
            list.Add(P(ParamId.ServoKp1, "SERVO_KP1", ParamType.I32, 0, 100000000, 25600000,
                "舵机1 位置环 Kp", "位置环增益：1280000 = 1.0（默认 25600000 = 20.0）"));
            list.Add(P(ParamId.ServoKp2, "SERVO_KP2", ParamType.I32, 0, 100000000, 25600000,
                "舵机2 位置环 Kp", "位置环增益：1280000 = 1.0（默认 25600000 = 20.0）"));
            list.Add(P(ParamId.ServoKd1, "SERVO_KD1", ParamType.I32, 0, 1000000000, 128000000,
                "舵机1 速度环 Kd", "速度环增益：128000000 = 1.0（默认 128000000 = 1.0）"));
            list.Add(P(ParamId.ServoKd2, "SERVO_KD2", ParamType.I32, 0, 1000000000, 128000000,
                "舵机2 速度环 Kd", "速度环增益：128000000 = 1.0（默认 128000000 = 1.0）"));

            /* Units / scaling / annotations */
            ApplyFormatting(list);
            return list.ToArray();
        }

        private static ParamInfo FindById(List<ParamInfo> list, ParamId id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == (ushort)id)
                {
                    return list[i];
                }
            }
            throw new InvalidOperationException("参数表缺少 id " + (ushort)id);
        }

        private static void ApplyFormatting(List<ParamInfo> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                ParamInfo p = list[i];
                switch ((ParamId)p.Id)
                {
                    case ParamId.EcatNodeAlias:
                        p.HexDisplay = false;
                        break;
                    case ParamId.EcatSync0PeriodUs:
                    case ParamId.EcatSync1PeriodUs:
                        p.DisplayUnit = "µs";
                        break;
                    case ParamId.EcatDcShiftNs:
                        p.DisplayUnit = "ns";
                        break;
                    case ParamId.EcatWatchdogMs:
                    case ParamId.EcatSdoTimeoutMs:
                    case ParamId.UsbProtoTimeoutMs:
                        p.DisplayUnit = "ms";
                        break;
                    case ParamId.DshotUpdateHz:
                    case ParamId.ServoPollHz:
                    case ParamId.UsbAutoTelemetryHz:
                        p.DisplayUnit = "Hz";
                        break;
                    case ParamId.ServoAngleMin1:
                    case ParamId.ServoAngleMin2:
                    case ParamId.ServoAngleMax1:
                    case ParamId.ServoAngleMax2:
                    case ParamId.ServoAngleOfs1:
                    case ParamId.ServoAngleOfs2:
                        p.Scale = 0.1;
                        p.DisplayUnit = "°";
                        break;
                    case ParamId.ServoAngleGain1:
                    case ParamId.ServoAngleGain2:
                        p.Scale = 0.01;
                        p.DisplayUnit = "x";
                        break;
                    case ParamId.AdcSysVScale:
                    case ParamId.AdcEsc1IScale:
                    case ParamId.AdcEsc2IScale:
                    case ParamId.AdcSysIScale:
                        p.DisplayUnit = "µ/LSB";
                        break;
                    case ParamId.AdcSysVOffsetMv:
                    case ParamId.AdcSysIOffsetMa:
                    case ParamId.AdcEscIOffsetMa:
                        p.DisplayUnit = "mV/mA";
                        break;
                    case ParamId.AdcVoltMinDv:
                    case ParamId.AdcVoltMaxDv:
                        p.Scale = 0.1;
                        p.DisplayUnit = "V";
                        break;
                    case ParamId.AdcCurrMaxCa:
                        p.Scale = 0.01;
                        p.DisplayUnit = "A";
                        break;
                    case ParamId.AdcEscCurrMaxMa:
                        p.DisplayUnit = "mA";
                        break;
                    case ParamId.Am32Baud:
                        p.DisplayUnit = "bit/s";
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
