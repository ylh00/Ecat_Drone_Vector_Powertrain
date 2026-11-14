/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * Am32Eeprom.cs - AM32 ESC EEPROM map (192 bytes) and decode helpers.
 *
 * Field offsets / names come from the AM32 firmware source (Inc/eeprom.h), see
 * .work/refs/DSHOT_AM32_NOTES.md section 7 and .work/refs/am32/am32_eeprom.h.
 * Every field is a uint8_t.  The "tune[128]" block (offset 48..175) and the CAN
 * reserved bytes (184..191) are not editable as scalars.
 *
 * The default values of a factory fresh AM32 ESC are NOT documented upstream
 * (the notes mark them as "未找到"); the simulator therefore starts from a
 * clearly marked synthetic template and a real ESC read always wins.
 */

using System;
using System.Collections.Generic;
using System.Text;

namespace EcatDroneHost.Protocol
{
    /// <summary>How a byte field should be presented / edited.</summary>
    public enum Am32FieldKind
    {
        /// <summary>plain 0..255 value</summary>
        Byte = 0,
        /// <summary>0 = off, 1 = on</summary>
        Boolean = 1,
        /// <summary>named values from Am32Field.Names</summary>
        Enum = 2,
        /// <summary>multi byte reserved block, read only in the grid</summary>
        Range = 3
    }

    /// <summary>One field of the AM32 EEPROM image.</summary>
    public sealed class Am32Field
    {
        public int Offset;
        public int Length = 1;
        public string Tag = string.Empty;
        public string NameZh = string.Empty;
        public string Note = string.Empty;
        public Am32FieldKind Kind = Am32FieldKind.Byte;
        public string[] Names;
        public double DisplayScale = 1.0;
        public string DisplayUnit = string.Empty;

        public bool IsEditable
        {
            get { return Kind != Am32FieldKind.Range; }
        }

        /// <summary>Human readable value text for one byte.</summary>
        public string FormatValue(int value)
        {
            if (Kind == Am32FieldKind.Boolean)
            {
                return value == 0 ? "关闭 (0)" : "开启 (" + value + ")";
            }
            if (Kind == Am32FieldKind.Enum && Names != null && value >= 0 && value < Names.Length)
            {
                return Names[value] + " (" + value + ")";
            }
            if (Math.Abs(DisplayScale - 1.0) > 1e-9)
            {
                return (value * DisplayScale).ToString("F1") + DisplayUnit + " (" + value + ")";
            }
            return value + (DisplayUnit.Length > 0 ? " " + DisplayUnit : string.Empty);
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1} ({2})", Offset, Tag, NameZh);
        }
    }

    /// <summary>One decoded EEPROM value (used by the AM32 settings grid).</summary>
    public sealed class Am32Setting
    {
        public Am32Field Field;
        public int Value;
        public string Text;
        public string OffsetText
        {
            get
            {
                if (Field.Length > 1)
                {
                    return Field.Offset + ".." + (Field.Offset + Field.Length - 1);
                }
                return Field.Offset.ToString();
            }
        }
    }

    /// <summary>Static AM32 EEPROM description + helpers.</summary>
    public static class Am32EepromMap
    {
        public const int Size = 192;

        private static readonly Am32Field[] _fields = BuildFields();
        private static readonly Dictionary<int, Am32Field> _byOffset = BuildIndex();

        public static Am32Field[] Fields
        {
            get { return _fields; }
        }

        public static Am32Field FindByOffset(int offset)
        {
            Am32Field field;
            return _byOffset.TryGetValue(offset, out field) ? field : null;
        }

        public static Am32Field FindByTag(string tag)
        {
            if (tag == null)
            {
                return null;
            }
            for (int i = 0; i < _fields.Length; i++)
            {
                if (string.Equals(_fields[i].Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    return _fields[i];
                }
            }
            return null;
        }

        private static Dictionary<int, Am32Field> BuildIndex()
        {
            Dictionary<int, Am32Field> map = new Dictionary<int, Am32Field>(_fields.Length);
            for (int i = 0; i < _fields.Length; i++)
            {
                map[_fields[i].Offset] = _fields[i];
            }
            return map;
        }

        private static Am32Field F(int offset, string tag, string nameZh, string note)
        {
            Am32Field f = new Am32Field();
            f.Offset = offset;
            f.Tag = tag;
            f.NameZh = nameZh;
            f.Note = note;
            return f;
        }

        private static Am32Field Bool(int offset, string tag, string nameZh, string note)
        {
            Am32Field f = F(offset, tag, nameZh, note);
            f.Kind = Am32FieldKind.Boolean;
            return f;
        }

        private static Am32Field Enum(int offset, string tag, string nameZh, string note, params string[] names)
        {
            Am32Field f = F(offset, tag, nameZh, note);
            f.Kind = Am32FieldKind.Enum;
            f.Names = names;
            return f;
        }

        private static Am32Field Range(int offset, int length, string tag, string nameZh, string note)
        {
            Am32Field f = F(offset, tag, nameZh, note);
            f.Length = length;
            f.Kind = Am32FieldKind.Range;
            return f;
        }

        private static Am32Field[] BuildFields()
        {
            List<Am32Field> list = new List<Am32Field>();

            list.Add(F(0, "reserved_0", "保留字节 0", "未使用"));
            list.Add(F(1, "eeprom_version", "EEPROM 布局版本", "AM32 参数布局版本"));
            list.Add(F(2, "reserved_1", "保留字节 1", "未使用"));
            list.Add(F(3, "version.major", "固件主版本", "AM32 固件版本号（主）"));
            list.Add(F(4, "version.minor", "固件次版本", "AM32 固件版本号（次）"));
            list.Add(F(5, "max_ramp", "加速度上限", "0.1%/ms ~ 25%/ms（每 ms 允许的油门变化）"));
            Am32Field minDuty = F(6, "minimum_duty_cycle", "最小占空比", "0.2% ~ 51%，避免电机低速失步");
            minDuty.DisplayScale = 0.5;
            minDuty.DisplayUnit = "%";
            list.Add(minDuty);
            list.Add(Bool(7, "disable_stick_calibration", "禁用摇杆校准", "1 = 不做上电油门行程校准"));
            Am32Field absCut = F(8, "absolute_voltage_cutoff", "绝对电压截止", "1..100，0.5 V 步进；0 = 关闭");
            absCut.DisplayScale = 0.5;
            absCut.DisplayUnit = "V";
            list.Add(absCut);
            list.Add(F(9, "current_P", "电流环 P 增益", "0..255"));
            list.Add(F(10, "current_I", "电流环 I 增益", "0..255"));
            list.Add(F(11, "current_D", "电流环 D 增益", "0..255"));
            list.Add(F(12, "active_brake_power", "主动刹车强度", "1..5（占空比 %）"));
            list.Add(F(13, "brake_on_zero_throttle", "零油门刹车行为", "电机仍在旋转时的刹车行为"));
            list.Add(Enum(14, "demag_comp", "去磁补偿", "换相去磁后的功率削减",
                "关闭", "仅滑行", "1/32", "1/16", "1/8"));
            list.Add(Range(15, 1, "reserved_eeprom_3[0]", "保留字节 15", "未使用"));
            list.Add(Range(16, 1, "reserved_eeprom_3[1]", "保留字节 16", "未使用"));
            list.Add(Bool(17, "dir_reversed", "转向反转", "1 = 电机反转"));
            list.Add(Bool(18, "bi_direction", "双向 (3D) 模式", "1 = 支持正反转 3D 油门"));
            list.Add(Bool(19, "use_sine_start", "正弦启动", "1 = 使用正弦波启动"));
            list.Add(Bool(20, "comp_pwm", "互补 PWM", "1 = 互补 PWM（同步整流，效率更高）"));
            list.Add(Bool(21, "variable_pwm", "可变 PWM 频率", "1 = 根据转速调整 PWM 频率"));
            list.Add(Bool(22, "stuck_rotor_protection", "堵转保护", "1 = 检测到堵转时切断输出"));
            list.Add(F(23, "advance_level", "进角档位", "0..255（AM32 进角表索引）"));
            list.Add(F(24, "pwm_frequency", "PWM 频率档位", "0..255（AM32 PWM 频率表索引）"));
            list.Add(F(25, "startup_power", "启动功率", "启动阶段的最大功率"));
            list.Add(F(26, "motor_kv", "电机 KV", "电机 KV 值，用于某些 AM32 算法"));
            list.Add(F(27, "motor_poles", "电机极数", "电机磁极数（注意与极对数的区别）"));
            list.Add(Bool(28, "brake_on_stop", "停转后刹车", "1 = 电机停转后保持刹车"));
            list.Add(Bool(29, "stall_protection", "失速保护", "1 = 失速时切断输出"));
            list.Add(F(30, "beep_volume", "蜂鸣音量", "0..255"));
            list.Add(F(31, "telemetry_on_interval", "遥测发送间隔", "AM32 主动遥测间隔档位"));
            list.Add(F(32, "servo.low_threshold", "舵机模式低阈值", "舵机输入模式下的低油门阈值"));
            list.Add(F(33, "servo.high_threshold", "舵机模式高阈值", "舵机输入模式下的高油门阈值"));
            list.Add(F(34, "servo.neutral", "舵机模式中位", "舵机输入模式下的中位值"));
            list.Add(F(35, "servo.dead_band", "舵机模式死区", "舵机输入模式下的死区"));
            list.Add(Bool(36, "low_voltage_cut_off", "低压截止使能", "1 = 启用低压截止"));
            list.Add(F(37, "low_cell_volt_cutoff", "单节截止电压", "0.5 V 步进（锂电单节）"));
            list.Add(Bool(38, "rc_car_reverse", "车模倒车模式", "1 = 车模倒车逻辑"));
            list.Add(Bool(39, "use_hall_sensors", "使用霍尔传感器", "1 = 有霍尔传感器（AM32 2.x 起未用）"));
            list.Add(F(40, "sine_mode_changeover_thottle_level", "正弦→方波切换油门", "切换阈值"));
            list.Add(F(41, "drag_brake_strength", "拖拽刹车强度", "0..255"));
            list.Add(F(42, "driving_brake_strength", "行驶刹车强度", "0..255"));
            list.Add(F(43, "limits.temperature", "温度限值", "过温限值"));
            list.Add(F(44, "limits.current", "电流限值", "过流限值"));
            list.Add(F(45, "sine_mode_power", "正弦模式功率", "0..255"));
            list.Add(F(46, "input_type", "输入类型", "输入协议（DShot / 舵机 / 串口等，AM32 内部枚举）"));
            list.Add(Bool(47, "auto_advance", "自动进角", "1 = 自动进角"));
            list.Add(Range(48, 128, "tune[128]", "调参保留区", "128 字节，AM32 固件内部使用，不要修改"));
            list.Add(F(176, "can.can_node", "CAN 节点号", "CAN 总线节点地址"));
            list.Add(F(177, "can.esc_index", "CAN ESC 索引", "CAN 总线上的 ESC 序号"));
            list.Add(Bool(178, "can.require_arming", "CAN 需要解锁", "1 = 需要解锁后才能启动"));
            list.Add(F(179, "can.telem_rate", "CAN 遥测速率", "AM32 CAN 遥测发送速率档位"));
            list.Add(Bool(180, "can.require_zero_throttle", "CAN 需要零油门", "1 = 启动前要求零油门"));
            list.Add(F(181, "can.filter_hz", "CAN 滤波频率", "CAN 输入滤波"));
            list.Add(F(182, "can.debug_rate", "CAN 调试速率", "CAN 调试输出速率"));
            list.Add(Bool(183, "can.term_enable", "CAN 终端电阻", "1 = 启用片上 CAN 终端电阻"));
            list.Add(Range(184, 8, "can.reserved[8]", "CAN 保留区", "8 字节保留"));

            return list.ToArray();
        }

        /// <summary>Decode all fields of a 192 byte EEPROM image.</summary>
        public static List<Am32Setting> Decode(byte[] data)
        {
            if (data == null || data.Length < Size)
            {
                throw new HostException("AM32 EEPROM 数据长度不足: " + (data == null ? 0 : data.Length));
            }
            List<Am32Setting> list = new List<Am32Setting>(_fields.Length);
            for (int i = 0; i < _fields.Length; i++)
            {
                Am32Field field = _fields[i];
                if (field.Kind == Am32FieldKind.Range)
                {
                    continue;
                }
                Am32Setting setting = new Am32Setting();
                setting.Field = field;
                setting.Value = data[field.Offset];
                setting.Text = field.FormatValue(setting.Value);
                list.Add(setting);
            }
            return list;
        }

        /// <summary>Set one byte value in an image (bounds / range checked).</summary>
        public static void SetByte(byte[] data, Am32Field field, int value)
        {
            if (data == null || data.Length < Size)
            {
                throw new HostException("AM32 EEPROM 数据长度不足");
            }
            if (field == null || field.Kind == Am32FieldKind.Range)
            {
                throw new HostException("该字段不可编辑");
            }
            if (value < 0 || value > 255)
            {
                throw new HostException("AM32 参数值必须在 0..255 之间");
            }
            data[field.Offset] = (byte)value;
        }

        /// <summary>Format the EEPROM as 16 bytes/row hex + ASCII dump.</summary>
        public static string ToHexDump(byte[] data, int bytesPerRow)
        {
            if (data == null)
            {
                return string.Empty;
            }
            if (bytesPerRow <= 0)
            {
                bytesPerRow = 16;
            }
            StringBuilder sb = new StringBuilder();
            for (int row = 0; row < data.Length; row += bytesPerRow)
            {
                sb.Append(row.ToString("D3")).Append("  ");
                int count = Math.Min(bytesPerRow, data.Length - row);
                for (int i = 0; i < bytesPerRow; i++)
                {
                    if (i < count)
                    {
                        sb.Append(data[row + i].ToString("X2")).Append(' ');
                    }
                    else
                    {
                        sb.Append("   ");
                    }
                }
                sb.Append(" |");
                for (int i = 0; i < count; i++)
                {
                    byte b = data[row + i];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                sb.Append('|');
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// Synthetic "factory like" AM32 2.x image used by the in-process simulator.
        /// A real ESC must be read before changing anything on hardware!
        /// </summary>
        public static byte[] CreateSimulatedDefaults()
        {
            byte[] data = new byte[Size];
            data[0] = 0;
            data[1] = 2;     /* eeprom layout version 2 */
            data[2] = 0;
            data[3] = 2;     /* firmware 2.x */
            data[4] = 18;
            data[5] = 25;    /* max_ramp */
            data[6] = 10;    /* minimum duty */
            data[7] = 0;
            data[8] = 0;     /* absolute voltage cutoff off */
            data[12] = 3;    /* active brake power */
            data[13] = 1;
            data[14] = 2;    /* demag 1/32 */
            data[17] = 0;    /* normal direction */
            data[18] = 0;    /* one direction */
            data[19] = 1;    /* sine start */
            data[20] = 1;    /* comp pwm */
            data[21] = 0;
            data[22] = 1;    /* stuck rotor protection */
            data[23] = 5;    /* advance level */
            data[24] = 0;
            data[25] = 0;
            data[26] = 0;
            data[27] = 14;   /* 14 poles = 7 pole pairs */
            data[28] = 0;
            data[29] = 1;    /* stall protection */
            data[30] = 3;    /* beep volume */
            data[31] = 10;
            data[32] = 100;
            data[33] = 200;
            data[34] = 150;
            data[35] = 5;
            data[36] = 1;
            data[37] = 33;   /* low cell cutoff 3.3 V (0.5 V steps -> 16.5? just synthetic) */
            data[39] = 0;
            data[41] = 10;
            data[42] = 10;
            data[43] = 100;
            data[44] = 60;
            data[45] = 20;
            data[46] = 0;    /* input type: dshot */
            data[47] = 1;
            for (int i = 48; i < 176; i++)
            {
                data[i] = 0xFF; /* erased flash pattern */
            }
            data[176] = 1;
            data[177] = 0;
            data[178] = 1;
            data[179] = 5;
            data[180] = 1;
            data[181] = 10;
            data[182] = 0;
            data[183] = 0;
            for (int i = 184; i < 192; i++)
            {
                data[i] = 0xFF;
            }
            return data;
        }
    }
}
