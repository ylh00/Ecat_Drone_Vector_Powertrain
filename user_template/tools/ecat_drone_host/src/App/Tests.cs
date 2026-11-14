/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * Tests.cs - "--selftest" implementation.
 *
 * Runs without hardware: protocol codec, parsers, telemetry/parameter/AM32
 * decoding and the complete request/response stack against the in-process
 * firmware simulator (FakeFirmwareTransport).
 *
 * Exit code 0 = every check passed, 1 = at least one failure.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using EcatDroneHost.Protocol;
using EcatDroneHost.Transport;
using EcatDroneHost.Ui;

namespace EcatDroneHost.App
{
    internal sealed class TestFailure : Exception
    {
        public TestFailure(string message) : base(message) { }
    }

    /// <summary>Minimal assertion + reporting helper.</summary>
    internal sealed class TestReport
    {
        private readonly StringBuilder _text = new StringBuilder();
        private readonly List<string> _failures = new List<string>();

        public int Passed { get; private set; }
        public int Failed { get; private set; }

        public IList<string> Failures
        {
            get { return _failures; }
        }

        public string Text
        {
            get { return _text.ToString(); }
        }

        public void Section(string title)
        {
            _text.AppendLine();
            _text.AppendLine("------------------------------------------------------------------");
            _text.AppendLine("== " + title);
            _text.AppendLine("------------------------------------------------------------------");
        }

        public void Note(string text)
        {
            _text.AppendLine("       " + text);
        }

        public void Case(string name, Action body)
        {
            try
            {
                body();
                Passed++;
                _text.AppendLine("[PASS] " + name);
            }
            catch (TestFailure failure)
            {
                Failed++;
                _failures.Add(name + ": " + failure.Message);
                _text.AppendLine("[FAIL] " + name);
                _text.AppendLine("       " + failure.Message);
            }
            catch (Exception ex)
            {
                Failed++;
                _failures.Add(name + ": " + ex.GetType().Name + " " + ex.Message);
                _text.AppendLine("[FAIL] " + name);
                _text.AppendLine("       " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /* ------------------------------ assertions ----------------------------- */

        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new TestFailure(message);
            }
        }

        public static void Equal(object expected, object actual, string what)
        {
            bool equal;
            if (expected == null || actual == null)
            {
                equal = expected == null && actual == null;
            }
            else if (IsNumeric(expected) && IsNumeric(actual))
            {
                equal = Convert.ToDouble(expected) == Convert.ToDouble(actual);
            }
            else
            {
                equal = expected.Equals(actual);
            }
            if (!equal)
            {
                throw new TestFailure(string.Format("{0}: 期望 {1}，实际 {2}", what, expected, actual));
            }
        }

        public static void Near(double expected, double actual, double tolerance, string what)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new TestFailure(string.Format("{0}: 期望 {1:F4}±{2}，实际 {3:F4}",
                    what, expected, tolerance, actual));
            }
        }

        public static void Contains(string text, string part, string what)
        {
            if (text == null || text.IndexOf(part, StringComparison.Ordinal) < 0)
            {
                throw new TestFailure(string.Format("{0}: 未找到 \"{1}\"", what, part));
            }
        }

        public static void NotContains(string text, string part, string what)
        {
            if (text != null && text.IndexOf(part, StringComparison.Ordinal) >= 0)
            {
                throw new TestFailure(string.Format("{0}: 不应出现 \"{1}\"", what, part));
            }
        }

        public static void Size(byte[] data, int expected, string what)
        {
            if (data == null)
            {
                throw new TestFailure(what + ": 数据为空");
            }
            if (data.Length != expected)
            {
                throw new TestFailure(string.Format("{0}: 期望 {1} 字节，实际 {2} 字节",
                    what, expected, data.Length));
            }
        }

        public static TException Throws<TException>(Action action, string what) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                throw new TestFailure(string.Format("{0}: 期望 {1}，实际抛出 {2}: {3}",
                    what, typeof(TException).Name, ex.GetType().Name, ex.Message));
            }
            throw new TestFailure(string.Format("{0}: 期望抛出 {1}，但调用成功", what, typeof(TException).Name));
        }

        private static bool IsNumeric(object value)
        {
            return value is byte || value is sbyte || value is short || value is ushort ||
                   value is int || value is uint || value is long || value is ulong ||
                   value is float || value is double || value is decimal;
        }
    }

    /// <summary>"--selftest" entry point.</summary>
    public static class SelfTest
    {
        public static int Run(string[] args)
        {
            TestReport report = new TestReport();
            DateTime started = DateTime.Now;

            report.Section("1. CRC16 校验算法");
            TestCrc(report);

            report.Section("2. 帧编解码与流解析（含重同步）");
            TestFraming(report);

            report.Section("3. 参数描述符 / 参数表");
            TestParameters(report);

            report.Section("4. 遥测与状态位解析");
            TestTelemetry(report);

            report.Section("5. AM32 EEPROM 布局与 4way 帧");
            TestAm32(report);

            report.Section("6. 模拟固件端到端（协议栈 / UI 逻辑层）");
            TestFirmwareSimulation(report);

            report.Section("7. 超时与重试");
            TestRetryAndTimeout(report);

            report.Section("8. 日志与界面");
            TestLogAndUi(report);

            /* ------------------------------ summary ---------------------------- */
            StringBuilder summary = new StringBuilder();
            summary.AppendLine();
            summary.AppendLine("==================================================================");
            summary.AppendLine("自检结果: 通过 " + report.Passed + " 项，失败 " + report.Failed + " 项");
            summary.AppendLine("耗时: " + (int)(DateTime.Now - started).TotalMilliseconds + " ms");
            summary.Append("结论: ");
            summary.AppendLine(report.Failed == 0 ? "全部通过 (SELFTEST PASS)" : "存在失败 (SELFTEST FAIL)");
            summary.AppendLine("==================================================================");
            if (report.Failed > 0)
            {
                summary.AppendLine("失败明细:");
                for (int i = 0; i < report.Failures.Count; i++)
                {
                    summary.AppendLine("  - " + report.Failures[i]);
                }
            }

            string fullText = report.Text + summary;
            Console.Write(fullText);
            Console.Out.Flush();

            /* always persist a report next to the executable so the result can be
             * verified even when the winexe has no attached console */
            try
            {
                string logPath = GetOption(args, "--selftest-log");
                if (string.IsNullOrEmpty(logPath))
                {
                    logPath = Path.Combine(GetExecutableDirectory(), "selftest.log");
                }
                File.WriteAllText(logPath, fullText, new UTF8Encoding(true));
                Console.WriteLine("自检报告已保存: " + logPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("无法保存自检报告: " + ex.Message);
            }

            return report.Failed == 0 ? 0 : 1;
        }

        private static string GetExecutableDirectory()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string location = assembly.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    string dir = Path.GetDirectoryName(location);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        return dir;
                    }
                }
            }
            catch (Exception)
            {
                /* ignore */
            }
            return Environment.CurrentDirectory;
        }

        private static string GetOption(string[] args, string name)
        {
            if (args == null)
            {
                return null;
            }
            string prefix = name + "=";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != null && args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(prefix.Length);
                }
            }
            return null;
        }

        /* ===================================================================== */
        /* 1. CRC16                                                              */
        /* ===================================================================== */

        private static void TestCrc(TestReport report)
        {
            byte[] check = Encoding.ASCII.GetBytes("123456789");

            report.Case("CRC16-CCITT-FALSE 标准校验向量 \"123456789\" = 0x29B1", delegate
            {
                TestReport.Equal(0x29B1, Crc16.CcittFalse(check, 0, check.Length), "CCITT-FALSE");
            });

            report.Case("CRC16-CCITT-FALSE 空数据 = 0xFFFF", delegate
            {
                TestReport.Equal(0xFFFF, Crc16.CcittFalse(new byte[0], 0, 0), "空数据 CRC");
            });

            report.Case("CRC16-CCITT-FALSE 分段累加一致", delegate
            {
                ushort whole = Crc16.CcittFalse(check, 0, check.Length);
                ushort part = Crc16.CcittFalseUpdate(0xFFFF, check, 0, 4);
                part = Crc16.CcittFalseUpdate(part, check, 4, check.Length - 4);
                TestReport.Equal(whole, part, "分段 CRC");
            });

            report.Case("CRC16-XMODEM 标准校验向量 = 0x31C3", delegate
            {
                TestReport.Equal(0x31C3, Crc16.Xmodem(check, 0, check.Length), "XMODEM");
            });

            report.Case("CRC16-ARC 标准校验向量 = 0xBB3D", delegate
            {
                TestReport.Equal(0xBB3D, Crc16.Arc(check, 0, check.Length), "ARC");
            });

            report.Note("CRC16-CCITT-FALSE: poly 0x1021 / init 0xFFFF，用于板卡协议帧");
            report.Note("CRC16-XMODEM:      poly 0x1021 / init 0x0000，用于 AM32 4way 帧");
            report.Note("CRC16-ARC:         poly 0xA001 / init 0x0000，用于 BLHeli 引导层帧");
        }

        /* ===================================================================== */
        /* 2. framing                                                            */
        /* ===================================================================== */

        private static void TestFraming(TestReport report)
        {
            report.Case("请求帧字节布局（A5 5A CMD SEQ LEN LEN PAYLOAD CRC）", delegate
            {
                byte[] frame = FrameCodec.Encode(HostCmd.Ping, 7, new byte[] { 0x11, 0x22 });
                TestReport.Size(frame, HostProto.Overhead + 2, "帧长度");
                TestReport.Equal((byte)0xA5, frame[0], "SYNC0");
                TestReport.Equal((byte)0x5A, frame[1], "SYNC1");
                TestReport.Equal((byte)0x01, frame[2], "CMD");
                TestReport.Equal((byte)7, frame[3], "SEQ");
                TestReport.Equal((byte)2, frame[4], "LEN_LO");
                TestReport.Equal((byte)0, frame[5], "LEN_HI");
                ushort crc = (ushort)(frame[frame.Length - 2] | (frame[frame.Length - 1] << 8));
                TestReport.Equal(Crc16.CcittFalse(frame, 2, 4 + 2), crc, "帧尾 CRC");
            });

            report.Case("帧编码 / 解码往返（含空载荷）", delegate
            {
                for (int payloadLength = 0; payloadLength <= 8; payloadLength += 4)
                {
                    byte[] payload = new byte[payloadLength];
                    for (int i = 0; i < payload.Length; i++)
                    {
                        payload[i] = (byte)(0xA0 + i);
                    }
                    byte[] frame = FrameCodec.Encode(HostCmd.GetTelemetry, 0x33, payload);
                    HostFrame decoded;
                    int consumed;
                    TestReport.True(FrameCodec.TryDecode(frame, 0, frame.Length, out decoded, out consumed),
                        "TryDecode 应成功 (len=" + payloadLength + ")");
                    TestReport.Equal(frame.Length, consumed, "消费字节数");
                    TestReport.Equal((byte)0x03, decoded.Cmd, "CMD");
                    TestReport.Equal((byte)0x33, decoded.Seq, "SEQ");
                    TestReport.Equal(payload.Length, decoded.Payload.Length, "PAYLOAD 长度");
                }
            });

            report.Case("单帧最大载荷 512 字节通过，513 字节被拒绝", delegate
            {
                byte[] maximum = FrameCodec.Encode(HostCmd.Am32Xfer, 1, new byte[HostProto.MaxPayload]);
                TestReport.Equal(HostProto.Overhead + HostProto.MaxPayload, maximum.Length, "最大帧长度");
                TestReport.Throws<ArgumentOutOfRangeException>(delegate
                {
                    FrameCodec.Encode(HostCmd.Am32Xfer, 1, new byte[HostProto.MaxPayload + 1]);
                }, "超长载荷");
            });

            report.Case("流解析器处理拆包 / 粘包", delegate
            {
                FrameParser parser = new FrameParser();
                byte[] first = FrameCodec.Encode(HostCmd.Ping, 1, new byte[] { 0x11 });
                byte[] second = FrameCodec.Encode(HostCmd.GetVersion, 2, new byte[] { 1, 2, 3 });

                parser.Feed(first, 0, 3);
                HostFrame frame;
                TestReport.True(!parser.TryRead(out frame), "不完整帧不应产出");

                parser.Feed(first, 3, first.Length - 3);
                parser.Feed(second, 0, second.Length);
                TestReport.True(parser.TryRead(out frame), "粘包第 1 帧");
                TestReport.Equal((byte)0x01, frame.Cmd, "第 1 帧 CMD");
                TestReport.True(parser.TryRead(out frame), "粘包第 2 帧");
                TestReport.Equal((byte)0x02, frame.Cmd, "第 2 帧 CMD");
                TestReport.True(!parser.TryRead(out frame), "不应有多余帧");
                TestReport.Equal(0, parser.Buffered, "缓冲应清空");
                TestReport.Equal(2, parser.FramesOk, "FramesOk");
            });

            report.Case("流解析器重同步（前导噪声 + CRC 错误帧）", delegate
            {
                FrameParser parser = new FrameParser();
                byte[] noise = new byte[] { 0x00, 0xFF, 0x5A, 0xA5 };
                byte[] corrupt = FrameCodec.Encode(HostCmd.GetVersion, 10, new byte[] { 1 });
                corrupt[corrupt.Length - 1] ^= 0xFF;
                byte[] good = FrameCodec.Encode(HostCmd.Ping, 9, new byte[] { 0xAB });

                parser.Feed(noise, 0, noise.Length);
                parser.Feed(corrupt, 0, corrupt.Length);
                parser.Feed(good, 0, good.Length);

                HostFrame frame;
                TestReport.True(parser.TryRead(out frame), "应能恢复并解析出好帧");
                TestReport.Equal((byte)9, frame.Seq, "恢复后的 SEQ");
                TestReport.True(parser.CrcErrors >= 1, "应记录至少 1 次 CRC 错误");
                TestReport.True(parser.Resyncs >= 1, "应记录至少 1 次重同步");
                TestReport.True(!parser.TryRead(out frame), "不应有多余帧");
            });

            report.Case("长度字段非法时丢弃并继续扫描", delegate
            {
                FrameParser parser = new FrameParser();
                byte[] bogus = new byte[] { 0xA5, 0x5A, 0x01, 0x02, 0xFF, 0xFF, 0x00 };
                byte[] good = FrameCodec.Encode(HostCmd.Ping, 5, null);
                parser.Feed(bogus, 0, bogus.Length);
                parser.Feed(good, 0, good.Length);
                HostFrame frame;
                TestReport.True(parser.TryRead(out frame), "应跳过非法长度帧");
                TestReport.Equal((byte)0x01, frame.Cmd, "解析出的 CMD");
                TestReport.Equal((byte)5, frame.Seq, "解析出的 SEQ");
            });
        }

        /* ===================================================================== */
        /* 3. parameters                                                         */
        /* ===================================================================== */

        private static void TestParameters(TestReport report)
        {
            report.Case("参数描述符 70 字节序列化 / 反序列化", delegate
            {
                ParamDesc desc = new ParamDesc();
                desc.Id = 12;
                desc.Type = ParamType.I16;
                desc.Persist = true;
                desc.ReadOnly = false;
                desc.Len = 4;
                desc.Min = -100;
                desc.Max = 100000;
                desc.Default = 1000;
                desc.Value = 1234;
                desc.Name = "DSHOT_DIRECTION1";
                desc.Description = "ESC1 转向";

                byte[] data = desc.ToBytes();
                TestReport.Size(data, ParamDesc.WireSize, "描述符长度");
                TestReport.Equal(70, ParamDesc.WireSize, "HOST_PARAM_DESC_SIZE");

                ParamDesc decoded = ParamDesc.FromBytes(data, 0, data.Length);
                TestReport.Equal(desc.Id, decoded.Id, "id");
                TestReport.Equal(desc.Type, decoded.Type, "type");
                TestReport.Equal(desc.Min, decoded.Min, "min");
                TestReport.Equal(desc.Max, decoded.Max, "max");
                TestReport.Equal(desc.Default, decoded.Default, "def");
                TestReport.Equal(desc.Value, decoded.Value, "value");
                TestReport.Equal(desc.Name, decoded.Name, "name");
                TestReport.Equal(desc.Description, decoded.Description, "desc");
                TestReport.Equal(ParamDesc.WireSize, data.Length, "HOST_PARAM_DESC_SIZE 常量");
            });

            report.Case("参数描述符中文 UTF-8 截断安全且可还原", delegate
            {
                ParamDesc desc = new ParamDesc();
                desc.Id = 1;
                desc.Type = ParamType.U8;
                desc.Name = "SERVO_ID1";
                desc.Description = "舵机一号标识编号用于总线寻址";  /* > 31 UTF-8 bytes */
                byte[] data = desc.ToBytes();
                ParamDesc decoded = ParamDesc.FromBytes(data, 0, data.Length);
                TestReport.NotContains(decoded.Description, "\uFFFD", "UTF-8 截断不应产生替换字符");
                TestReport.True(decoded.Description.Length > 0, "截断后描述不应为空");
                TestReport.True(Encoding.UTF8.GetByteCount(decoded.Description) <= ParamDesc.DescLength - 1,
                    "描述字节数应不超过 31");
            });

            report.Case("LIST_PARAMS 描述符数组解析（70 字节/项）", delegate
            {
                ParamDesc a = new ParamDesc();
                a.Id = 3;
                a.Name = "A_PARAM";
                a.Description = "甲";
                ParamDesc b = new ParamDesc();
                b.Id = 4;
                b.Name = "B_PARAM";
                b.Description = "乙";
                byte[] blob = new byte[ParamDesc.WireSize * 2];
                Array.Copy(a.ToBytes(), 0, blob, 0, ParamDesc.WireSize);
                Array.Copy(b.ToBytes(), 0, blob, ParamDesc.WireSize, ParamDesc.WireSize);
                ParamDesc[] list = ParamDesc.FromList(blob, 2);
                TestReport.Equal(2, list.Length, "描述符数量");
                TestReport.Equal("A_PARAM", list[0].Name, "第 1 项 name");
                TestReport.Equal("B_PARAM", list[1].Name, "第 2 项 name");
                TestReport.Equal(3, (int)list[0].Id, "第 1 项 id");
            });

            report.Case("旧固件 68 字节描述符容错解析", delegate
            {
                ParamDesc desc = new ParamDesc();
                desc.Id = 6;
                desc.Name = "LEGACY";
                desc.Description = "old";
                byte[] full = desc.ToBytes();
                byte[] shortened = new byte[68];
                Array.Copy(full, 0, shortened, 0, 68);
                ParamDesc[] list = ParamDesc.FromList(shortened, 1);
                TestReport.Equal("LEGACY", list[0].Name, "68 字节描述符 name");
            });

            report.Case("参数表 (ParamCatalog) 与 param.h 一致：60 项、id 连续", delegate
            {
                TestReport.Equal(60, ParamCatalog.Count, "参数总数");
                TestReport.Equal(60, (int)ParamId.Count, "ParamId.Count");
                for (int i = 0; i < ParamCatalog.Count; i++)
                {
                    ParamInfo info = ParamCatalog.All[i];
                    TestReport.Equal((ushort)i, info.Id, "参数 id 顺序 @" + i);
                    TestReport.True(info.Name.Length > 0, "参数名不能为空 @" + i);
                    TestReport.True(info.Min <= info.Default && info.Default <= info.Max,
                        "默认值应在范围内: " + info.Name);
                    TestReport.True(info.Group.Length > 0, "分组不能为空: " + info.Name);
                }
                TestReport.Equal(9, (int)ParamId.DshotRate, "DSHOT_RATE id = 9");
                TestReport.Equal(20, (int)ParamId.ServoBaud, "SERVO_BAUD id = 20");
                TestReport.Equal(55, (int)ParamId.Am32EnablePassthrough, "AM32_ENABLE_PASSTHROUGH id = 55");
                TestReport.Equal(56, (int)ParamId.ServoKp1, "SERVO_KP1 id = 56");
                TestReport.Equal(57, (int)ParamId.ServoKp2, "SERVO_KP2 id = 57");
                TestReport.Equal(58, (int)ParamId.ServoKd1, "SERVO_KD1 id = 58");
                TestReport.Equal(59, (int)ParamId.ServoKd2, "SERVO_KD2 id = 59");
                /* firmware defaults from src/param.c */
                TestReport.Equal(6000000, ParamCatalog.GetDefault((int)ParamId.ServoBaud), "SERVO_BAUD 默认 6 Mbps");
                TestReport.Equal(25600000, ParamCatalog.GetDefault((int)ParamId.ServoKp1), "SERVO_KP1 默认值");
                TestReport.Equal(128000000, ParamCatalog.GetDefault((int)ParamId.ServoKd1), "SERVO_KD1 默认值");
                TestReport.Equal("ECAT", ParamCatalog.GuessGroup("ECAT_NODE_ALIAS"), "分组推断 ECAT");
                TestReport.Equal("DSHOT", ParamCatalog.GuessGroup("DSHOT_RATE"), "分组推断 DSHOT");
                TestReport.Equal("SERVO", ParamCatalog.GuessGroup("servo_baud"), "分组推断 SERVO（小写）");
                TestReport.Equal("AM32", ParamCatalog.GuessGroup("AM32_4WAY_BAUD"), "分组推断 AM32");
            });

            report.Case("参数显示格式（缩放 / 单位）", delegate
            {
                ParamInfo angle = ParamCatalog.Find((ushort)ParamId.ServoAngleMax1);
                TestReport.Equal("180.0 °", angle.FormatValue(1800), "角度显示");
                ParamInfo gain = ParamCatalog.Find((ushort)ParamId.ServoAngleGain1);
                TestReport.Equal("1.00 x", gain.FormatValue(100), "增益显示");
                ParamInfo volt = ParamCatalog.Find((ushort)ParamId.AdcVoltMaxDv);
                TestReport.Equal("52.0 V", volt.FormatValue(520), "电压显示");
            });
        }

        /* ===================================================================== */
        /* 4. telemetry                                                          */
        /* ===================================================================== */

        private static void TestTelemetry(TestReport report)
        {
            report.Case("54 字节遥测全字段解析（偏移与 header 一致）", delegate
            {
                byte[] blob = new byte[TelemetryData.WireSize];
                TestReport.Equal(54, TelemetryData.WireSize, "HOST_TELEMETRY_SIZE");
                Bytes.PutU16(blob, 0, 0x0003);          /* OP + output enabled */
                Bytes.PutU16(blob, 2, 0x0101);          /* ESC1_NO_TLM + OVER_VOLTAGE */
                Bytes.PutU32(blob, 4, 12000);           /* ESC1 eRPM */
                Bytes.PutU32(blob, 8, 0);               /* ESC2 stopped */
                Bytes.PutU16(blob, 12, 5000);
                Bytes.PutU16(blob, 14, 0);
                Bytes.PutU16(blob, 16, 1234);           /* 123.4 deg */
                Bytes.PutU16(blob, 18, -567);
                Bytes.PutU16(blob, 20, 100);
                Bytes.PutU16(blob, 22, -200);
                Bytes.PutU16(blob, 24, 300);
                Bytes.PutU16(blob, 26, 400);
                blob[28] = 45;
                blob[29] = 46;
                blob[30] = 0;
                blob[31] = 7;
                Bytes.PutU16(blob, 32, 480);            /* 48.0 V */
                Bytes.PutU16(blob, 34, 1500); /* 15.00 A */
                Bytes.PutU16(blob, 36, 500);
                Bytes.PutU16(blob, 38, 600);
                Bytes.PutU16(blob, 40, 999);
                Bytes.PutU16(blob, 42, 0x1234);
                Bytes.PutU16(blob, 44, (ushort)(0x0003 | (8 << 4))); /* link0/1 + AL=8 in bit4..7 */
                Bytes.PutU32(blob, 46, 123456);
                Bytes.PutU32(blob, 50, 7);

                TelemetryData t = TelemetryData.Parse(blob);
                TestReport.True(t.Complete, "应标记为完整包");
                TestReport.Equal(54, t.RawLength, "RawLength");
                TestReport.Equal(0x0003, (int)t.StatusWord, "status_word");
                TestReport.Equal(0x0101, (int)t.FaultCode, "fault_code");
                TestReport.Equal(12000.0, t.GetErpm(0), "ESC1 eRPM 原始值");
                TestReport.Equal(0.0, t.GetErpm(1), "ESC2 停止");
                TestReport.Near(12000.0 / 7.0, t.GetMechanicalRpm(0, 7), 1e-6, "机械转速");
                TestReport.Equal(123.4, t.ServoPosDeg(0), "舵机1 角度");
                TestReport.Near(-56.7, t.ServoPosDeg(1), 1e-6, "舵机2 角度");
                TestReport.Equal((short)-200, t.ServoSpeedRpm[1], "舵机2 转速");
                TestReport.Equal((short)400, t.ServoCurrentMa[1], "舵机2 电流");
                TestReport.Equal((byte)46, t.ServoTempC[1], "舵机2 温度");
                TestReport.Equal((byte)7, t.ServoError[1], "舵机2 错误码");
                TestReport.Equal(48.0, t.SysVoltageVolt, "系统电压");
                TestReport.Equal(15.0, t.SysCurrentAmp, "系统电流");
                TestReport.Equal(500, (int)t.EscCurrentMa[0], "ESC1 电流");
                TestReport.Equal(999, (int)t.LoopCounter, "loop_counter");
                TestReport.Equal(0x1234, (int)t.LedStatus, "led_status");
                TestReport.Equal(8, t.EcatAlState, "AL 状态");
                TestReport.True(t.EcatPort0Link && t.EcatPort1Link, "双端口 link");
                TestReport.Equal(123456U, t.HostRxFrames, "host_rx_frames");
                TestReport.Equal(7U, t.HostCrcErrors, "host_crc_errors");
                TestReport.Equal("OP (正常运行)", TelemetryData.DescribeEcatAlState(t.EcatAlState), "AL 文本");

                List<string> faults = t.GetFaultTexts();
                TestReport.Contains(string.Join(",", faults.ToArray()), "ESC1 无遥测", "故障文本");
                TestReport.Contains(string.Join(",", faults.ToArray()), "系统过压", "故障文本");
                List<string> status = t.GetStatusTexts();
                TestReport.Contains(string.Join(",", status.ToArray()), "输出使能", "状态文本");
            });

            report.Case("遥测短包容错（旧固件按 52 字节发送）", delegate
            {
                byte[] blob = new byte[52];
                Bytes.PutU16(blob, 0, 0x0001);
                Bytes.PutU32(blob, 4, 3000);
                TelemetryData t = TelemetryData.Parse(blob);
                TestReport.True(!t.Complete, "短包不应标记为完整");
                TestReport.Equal(3000.0, t.GetErpm(0), "eRPM 仍应解析");
                TestReport.Equal(0U, t.HostCrcErrors, "缺失字段补 0");
            });

            report.Case("GET_ECAT_STATUS / 版本信息解析", delegate
            {
                byte[] status = new byte[] { 8, 0x03, 0x00, 0x08, 0x01, 0x02, 0x03, 0x04 };
                EcatStatus ecat = EcatStatus.Parse(status);
                TestReport.Equal(8, (int)ecat.AlState, "AL");
                TestReport.True(ecat.Port0Link && ecat.Port1Link, "link");
                TestReport.Equal(0x0800, (int)ecat.Fault, "fault");
                TestReport.Equal(0x04030201U, ecat.Cycles, "cycles");

                byte[] version = new byte[22];
                Bytes.PutU16(version, 0, 1);
                Bytes.PutU16(version, 2, 2);
                Bytes.PutU16(version, 4, 3);
                byte[] date = Encoding.ASCII.GetBytes("Nov 14 2026");
                Array.Copy(date, 0, version, 6, date.Length);
                VersionInfo info = VersionInfo.Parse(version);
                TestReport.Equal("v1.2.3 (Nov 14 2026)", info.ToString(), "版本字符串");
            });

            report.Case("DShot 遥测 / 舵机状态 / ADC 数据解析", delegate
            {
                byte[] dshot = new byte[26];
                Bytes.PutU32(dshot, 0, 6400);
                Bytes.PutU16(dshot, 4, 9375);
                dshot[6] = 1;
                dshot[7] = 4;
                Bytes.PutU16(dshot, 8, 2);
                Bytes.PutU32(dshot, 10, 1000);
                Bytes.PutU32(dshot, 14, 3);
                Bytes.PutU32(dshot, 18, 4);
                Bytes.PutU32(dshot, 22, 5);
                DshotTelemetryData tlm = DshotTelemetryData.Parse(dshot);
                TestReport.Equal(6400U, tlm.Erpm, "eRPM");
                TestReport.Equal(9375, (int)tlm.PeriodUs, "period");
                TestReport.True(tlm.Valid, "valid");
                TestReport.Equal("接收", tlm.StateName, "状态文本");
                TestReport.Equal(1000U, tlm.OkCount, "ok_count");

                byte[] servo = new byte[20];
                Bytes.PutU16(servo, 0, 1800);
                Bytes.PutU16(servo, 2, 200);
                Bytes.PutU16(servo, 4, 350);
                Bytes.PutU16(servo, 6, 24000);
                servo[8] = 40;
                servo[9] = 0;
                servo[10] = 1;
                servo[11] = 1;
                ServoStatusData st = ServoStatusData.Parse(servo);
                TestReport.Equal(180.0, st.PosD10 / 10.0, "角度");
                TestReport.Equal(24000, (int)st.VoltageMv, "电压");
                TestReport.Equal(40, (int)st.TempC, "温度");
                TestReport.True(st.Online == 1, "在线");

                byte[] adc = new byte[24];   /* host_adc_blob_t */
                Bytes.PutU16(adc, 0, 111);
                Bytes.PutU16(adc, 2, 222);
                Bytes.PutU16(adc, 8, 480);
                Bytes.PutU16(adc, 10, 1234);
                adc[16] = 0x02;              /* alarm */
                adc[17] = 0;                 /* reserved0 */
                Bytes.PutU16(adc, 18, 77);   /* sample_count */
                Bytes.PutU32(adc, 20, 88);   /* last_update_ms */
                AdcMonitorData data = AdcMonitorData.Parse(adc);
                TestReport.Equal(24, AdcMonitorData.WireSize, "ADC blob 长度");
                TestReport.Equal(48.0, data.SysVoltageVolt, "ADC 电压");
                TestReport.Equal(77, (int)data.SampleCount, "ADC 采样计数");
                TestReport.Equal(88U, data.LastUpdateMs, "ADC 更新时间");
                TestReport.Contains(data.AlarmText, "欠压", "ADC 报警文本");
            });
        }

        /* ===================================================================== */
        /* 5. AM32                                                               */
        /* ===================================================================== */

        private static void TestAm32(TestReport report)
        {
            report.Case("AM32 EEPROM 字段表覆盖 0..191 且关键偏移正确", delegate
            {
                bool[] covered = new bool[Am32EepromMap.Size];
                for (int i = 0; i < Am32EepromMap.Fields.Length; i++)
                {
                    Am32Field field = Am32EepromMap.Fields[i];
                    for (int j = 0; j < field.Length; j++)
                    {
                        int offset = field.Offset + j;
                        TestReport.True(offset < Am32EepromMap.Size, "字段越界: " + field.Tag);
                        covered[offset] = true;
                    }
                }
                for (int i = 0; i < covered.Length; i++)
                {
                    TestReport.True(covered[i], "EEPROM 偏移 " + i + " 未被字段表覆盖");
                }
                TestReport.Equal(3, Am32EepromMap.FindByTag("version.major").Offset, "version.major @3");
                TestReport.Equal(17, Am32EepromMap.FindByTag("dir_reversed").Offset, "dir_reversed @17");
                TestReport.Equal(27, Am32EepromMap.FindByTag("motor_poles").Offset, "motor_poles @27");
                TestReport.Equal(46, Am32EepromMap.FindByTag("input_type").Offset, "input_type @46");
                TestReport.Equal(183, Am32EepromMap.FindByTag("can.term_enable").Offset, "can.term_enable @183");
                TestReport.Equal(192, Am32EepromMap.Size, "EEPROM 大小");
            });

            report.Case("AM32 EEPROM 解码 / 编辑 / 十六进制视图", delegate
            {
                byte[] image = Am32EepromMap.CreateSimulatedDefaults();
                TestReport.Size(image, 192, "默认镜像");
                List<Am32Setting> settings = Am32EepromMap.Decode(image);
                TestReport.True(settings.Count > 40, "解码字段数量");
                Am32Setting direction = FindSetting(settings, "dir_reversed");
                TestReport.True(direction != null, "应包含 dir_reversed");
                TestReport.Equal(0, direction.Value, "默认转向");
                TestReport.Contains(direction.Text, "关闭", "布尔字段文本");

                Am32EepromMap.SetByte(image, Am32EepromMap.FindByTag("dir_reversed"), 1);
                settings = Am32EepromMap.Decode(image);
                TestReport.Equal(1, FindSetting(settings, "dir_reversed").Value, "修改后转向");
                TestReport.Throws<HostException>(delegate
                {
                    Am32EepromMap.SetByte(image, Am32EepromMap.FindByTag("dir_reversed"), 300);
                }, "越界值");

                string dump = Am32EepromMap.ToHexDump(image, 16);
                TestReport.Contains(dump, "000  ", "十六进制首行");
                TestReport.Contains(dump, "016  ", "十六进制第二行（含偏移 17）");
                TestReport.True(dump.Split('\n').Length >= 12, "应输出 12 行");
            });

            report.Case("4way 容器帧编解码 + CRC 校验", delegate
            {
                byte[] frame = FourWayCodec.BuildContainer(1, FourWayCommand.ReadEeprom,
                    new byte[] { 0, 192 });
                TestReport.Size(frame, 2 + 5, "容器帧长度");
                TestReport.Equal((byte)1, frame[0], "ESC");
                TestReport.Equal(FourWayCommand.ReadEeprom, frame[1], "CMD");
                TestReport.Equal((byte)2, frame[2], "LEN");
                ushort expected = Crc16.Xmodem(frame, 0, 5);
                ushort actual = (ushort)((frame[5] << 8) | frame[6]);
                TestReport.Equal(expected, actual, "XMODEM CRC");

                byte esc;
                byte cmd;
                string error;
                byte[] param = FourWayCodec.ParseContainer(frame, out esc, out cmd, out error);
                TestReport.True(param != null, "解析应成功");
                TestReport.Equal((byte)1, esc, "解析 ESC");
                TestReport.Equal(FourWayCommand.ReadEeprom, cmd, "解析 CMD");
                TestReport.Equal(2, param.Length, "解析参数长度");

                frame[frame.Length - 1] ^= 0x5A;
                TestReport.True(FourWayCodec.ParseContainer(frame, out esc, out cmd, out error) == null,
                    "坏 CRC 应被拒绝");
                TestReport.True(error != null && error.Length > 0, "应给出错误说明");
            });

            report.Case("4way 接口帧（BLHeliSuite）编解码 + 应答解析", delegate
            {
                byte[] frame = FourWayCodec.BuildInterface(FourWayCommand.DeviceReadEeprom, 0x0010,
                    new byte[] { 5 });
                TestReport.Equal((byte)0x2F, frame[0], "本地转义");
                TestReport.Equal((byte)0x3D, frame[1], "CMD");
                TestReport.Equal((byte)0x00, frame[2], "ADDR_H");
                TestReport.Equal((byte)0x10, frame[3], "ADDR_L");
                TestReport.Equal((byte)1, frame[4], "LEN");
                TestReport.Equal((byte)5, frame[5], "PARAM");

                /* build a synthetic reply [2E][3D][00][10][02][AA][BB][00][CRC_Hi][CRC_Lo] */
                byte[] reply = new byte[] { 0x2E, 0x3D, 0x00, 0x10, 0x02, 0xAA, 0xBB, 0x00, 0x00, 0x00 };
                ushort crc = Crc16.Xmodem(reply, 0, 8);
                reply[8] = (byte)((crc >> 8) & 0xFF);
                reply[9] = (byte)(crc & 0xFF);
                FourWayReply parsed;
                string error;
                TestReport.True(FourWayCodec.TryParseInterfaceReply(reply, out parsed, out error),
                    "应答解析: " + error);
                TestReport.True(parsed.CrcOk, "应答 CRC 应正确");
                TestReport.Equal(0x10, parsed.Address, "应答地址");
                TestReport.Equal(2, parsed.Data.Length, "应答数据长度");
                TestReport.Equal((byte)0xAA, parsed.Data[0], "应答数据 0");
                TestReport.True(parsed.AckOk, "ACK OK");

                reply[9] ^= 0xFF;
                TestReport.True(FourWayCodec.TryParseInterfaceReply(reply, out parsed, out error), "解析仍应成功");
                TestReport.True(!parsed.CrcOk, "损坏的 CRC 应被检测");
            });

            report.Case("BLHeli 引导层原始帧（CRC16-ARC）", delegate
            {
                byte[] frame = FourWayCodec.BuildBootloaderFrame(FourWayCommand.SetAddress,
                    new byte[] { 0x00, 0x17 });
                TestReport.Size(frame, 2 + 2 + 2, "引导帧长度");
                ushort expected = Crc16.Arc(frame, 0, 4);
                ushort actual = (ushort)(frame[4] | (frame[5] << 8));
                TestReport.Equal(expected, actual, "ARC CRC 低字节在前");
            });
        }

        private static Am32Setting FindSetting(List<Am32Setting> settings, string tag)
        {
            for (int i = 0; i < settings.Count; i++)
            {
                if (string.Equals(settings[i].Field.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    return settings[i];
                }
            }
            return null;
        }

        /* ===================================================================== */
        /* 6. fake firmware end-to-end                                           */
        /* ===================================================================== */

        private static void TestFirmwareSimulation(TestReport report)
        {
            report.Case("模拟设备：打开 / PING / 版本", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.Open();
                    TestReport.True(client.IsConnected, "客户端应处于连接状态");
                    string ping = client.Ping();
                    TestReport.Contains(ping, "PING OK", "PING 结果");
                    TestReport.Contains(ping, "PING", "PING magic 0x474E4950");
                    TestReport.Contains(ping, "v1.0.0", "PING 固件版本");
                    VersionInfo version = client.GetVersion();
                    TestReport.Equal((ushort)1, version.Major, "主版本");
                    TestReport.Equal((ushort)0, version.Minor, "次版本");
                    TestReport.Contains(version.BuildDate, "2026", "编译日期");
                    client.Close();
                    TestReport.True(!client.IsConnected, "关闭后应断开");
                }
            });

            report.Case("模拟设备：遥测 / EtherCAT 状态", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.Open();
                    client.DshotTest(0, 1500); /* arm ESC1 at 50% */
                    TelemetryData telemetry = client.GetTelemetry();
                    TestReport.True(telemetry.Complete, "遥测应为 54 字节完整包");
                    TestReport.Equal(15000.0, telemetry.GetErpm(0), "ESC1 eRPM");
                    TestReport.Equal(0.0, telemetry.GetErpm(1), "ESC2 停止");
                    TestReport.True(telemetry.StatusBit(0), "OP 位");
                    TestReport.True(telemetry.StatusBit(1), "输出使能位");
                    TestReport.Near(48.0, telemetry.SysVoltageVolt, 1e-6, "系统电压");
                    TestReport.Equal(8, telemetry.EcatAlState, "遥测 AL 状态（bit4..7）");
                    TestReport.True(telemetry.EcatPort0Link && telemetry.EcatPort1Link, "遥测双端口 link");
                    EcatStatus ecat = client.GetEcatStatus();
                    TestReport.Equal(8, (int)ecat.AlState, "AL 状态");
                    TestReport.True(ecat.Port0Link, "端口0 link");
                    client.DshotTest(0, 1000); /* stop */
                }
            });

            report.Case("模拟设备：参数分块读取（60 项 = 9 次请求，名字小写）", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.Open();
                    int count = client.GetParamCount();
                    TestReport.Equal(60, count, "参数数量");
                    int before = client.RequestCount;
                    ParamDesc[] list = client.ListParams(count);
                    TestReport.Equal(60, list.Length, "读取到的描述符数量");
                    TestReport.Equal((count + 6) / 7, client.RequestCount - before, "LIST_PARAMS 请求次数");
                    TestReport.Equal(9, (int)list[9].Id, "第 10 项 id = DSHOT_RATE");
                    TestReport.Equal("dshot_rate", list[9].Name, "第 10 项名称（固件小写）");
                    TestReport.Equal(600, list[9].Default, "DSHOT_RATE 默认值");
                    TestReport.Equal(150, list[9].Min, "DSHOT_RATE 最小值");
                    TestReport.True(list[9].Description.Length > 0, "描述不应为空");
                    ParamDesc single = client.GetParamDesc(9);
                    TestReport.Equal("dshot_rate", single.Name, "单条描述符名称");
                    TestReport.Equal("servo_kp1", list[56].Name, "第 57 项 = 新增 SERVO_KP1");
                    TestReport.Equal(25600000, list[56].Default, "SERVO_KP1 默认值");
                    TestReport.Equal("servo_kd2", list[59].Name, "第 60 项 = SERVO_KD2");
                    client.Close();
                }
            });

            report.Case("模拟设备：参数读写 / 越界 / 保存 / 复位", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.Open();
                    TestReport.Equal(600, client.GetParam(9), "DSHOT_RATE 初始值");
                    client.SetParam(9, 300);
                    TestReport.Equal(300, client.GetParam(9), "写入后读回");
                    HostStatusException rangeError = TestReport.Throws<HostStatusException>(delegate
                    {
                        client.SetParam(9, 5000);
                    }, "越界写入");
                    TestReport.Equal(HostStatus.ErrParamRange, rangeError.Status, "越界状态码");
                    TestReport.Throws<HostStatusException>(delegate
                    {
                        client.SetParam(60000, 1);
                    }, "非法 id");
                    client.ParamSave();
                    client.ParamReset();
                    TestReport.Equal(600, client.GetParam(9), "复位后恢复默认值");
                }
            });

            report.Case("模拟设备：DShot 解锁 / 停止 / 命令", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.Open();
                    client.DshotTest(1, 1200);
                    DshotTelemetryData tlm = client.DshotGetTelemetry(1);
                    TestReport.Equal(6000U, tlm.Erpm, "ESC2 eRPM");
                    TestReport.True(tlm.Valid, "遥测有效");
                    HostStatusException range = TestReport.Throws<HostStatusException>(delegate
                    {
                        client.DshotTest(0, 2100);
                    }, "油门越界");
                    TestReport.Equal(HostStatus.ErrParamRange, range.Status, "油门越界状态码");
                    client.DshotCommand(1, 12); /* SAVE_SETTINGS */
                    client.DshotCommandAll(12); /* esc = 0xFF broadcasts to both ESCs */
                    client.DshotTest(1, 0);     /* 0 = stop (USB_PROTOCOL.md) */
                    TestReport.Equal(0U, client.DshotGetTelemetry(1).Erpm, "停止后 eRPM = 0");
                }
            });

            report.Case("模拟设备：舵机扫描（异步 BUSY 重试）/ 角度 / 状态 / 改 ID 不支持", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.Open();
                    /* the simulator answers ERR_BUSY once, the client must retry */
                    int[] bus0 = client.ServoScan(0, 0, 20);
                    TestReport.Equal(1, bus0.Length, "总线0 应扫描到 1 个舵机");
                    TestReport.Equal(1, bus0[0], "总线0 舵机 ID = 1");
                    int[] bus1 = client.ServoScan(1, 0, 20);
                    TestReport.Equal(2, bus1[0], "总线1 舵机 ID = 2");
                    client.ServoMoveTest(0, 1, 450);
                    ServoStatusData status = client.ServoRead(0, 1);
                    TestReport.Equal(45.0, status.PosD10 / 10.0, "舵机角度");
                    TestReport.True(status.Online == 1, "舵机在线");
                    /* wrong id: the firmware only accepts PARAM_SERVO_IDn */
                    HostStatusException wrongId = TestReport.Throws<HostStatusException>(delegate
                    {
                        client.ServoRead(0, 9);
                    }, "ID 与参数不匹配");
                    TestReport.Equal(HostStatus.ErrParamId, wrongId.Status, "错误 ID 状态码");
                    /* SERVO_SET_ID is ERR_UNSUPPORTED for the S288 bus */
                    HostStatusException unsupported = TestReport.Throws<HostStatusException>(delegate
                    {
                        client.ServoSetId(0, 1, 5);
                    }, "SERVO_SET_ID");
                    TestReport.Equal(HostStatus.ErrUnsupported, unsupported.Status, "SET_ID 状态码");
                }
            });

            report.Case("模拟设备：ADC / LED", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.Open();
                    AdcMonitorData adc = client.AdcGet();
                    TestReport.Near(48.0, adc.SysVoltageVolt, 1e-6, "系统电压");
                    TestReport.Equal("无", adc.AlarmText, "无报警");
                    client.LedSet(10, 20, 30);
                    client.LedTest(2, 500);
                }
            });

            report.Case("模拟设备：AM32 容器模式读取 / 修改 / 写入 / 校验 EEPROM", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.Open();
                    using (Am32Bridge bridge = new Am32Bridge(client))
                    {
                        bridge.Open(0);
                        TestReport.Equal(FourWayFrameMode.ContainerBootloader, bridge.Mode, "默认模式");
                        bridge.TestAlive();
                        byte[] image = bridge.ReadEeprom();
                        TestReport.Size(image, Am32EepromMap.Size, "EEPROM 长度");
                        TestReport.Equal((byte)2, image[1], "EEPROM 布局版本");
                        TestReport.Equal((byte)2, image[3], "固件主版本");
                        TestReport.Equal((byte)0, image[17], "初始转向");

                        Am32EepromMap.SetByte(image, Am32EepromMap.FindByTag("dir_reversed"), 1);
                        Am32EepromMap.SetByte(image, Am32EepromMap.FindByTag("motor_poles"), 12);
                        bridge.WriteEeprom(image, true);

                        byte[] readBack = bridge.ReadEeprom();
                        TestReport.Equal((byte)1, readBack[17], "写入后转向");
                        TestReport.Equal((byte)12, readBack[27], "写入后极数");
                        bridge.ExitBootloader();
                        bridge.Close();
                        TestReport.True(!bridge.IsOpen, "关闭后桥未占用");
                    }

                    /* ---- DShot programming mode write path (0x54 / 0x55) -------- */
                    /* the simulator answers ERR_BUSY a few times; the client paces
                     * and retries the identical request */
                    transport.Am32ProgBusyReplies = 3;
                    byte[] progImage = Am32EepromMap.CreateSimulatedDefaults();
                    progImage[17] = 1;   /* reversed */
                    progImage[27] = 14;  /* motor poles */
                    for (int address = 0; address < Am32EepromMap.Size; address++)
                    {
                        client.Am32ProgWrite(0, address, progImage[address], 40);
                    }
                    client.Am32ProgSave(0);

                    /* the 4-way read-back is the authoritative verification path */
                    using (Am32Bridge bridge = new Am32Bridge(client))
                    {
                        bridge.Open(0);
                        byte[] progReadBack = bridge.ReadEeprom();
                        bridge.Close();
                        TestReport.Size(progReadBack, Am32EepromMap.Size, "编程模式回读长度");
                        for (int i = 0; i < Am32EepromMap.Size; i++)
                        {
                            if (progReadBack[i] != progImage[i])
                            {
                                throw new TestFailure(string.Format(
                                    "编程模式回读不一致 @{0}: 期望 0x{1:X2} 实际 0x{2:X2}",
                                    i, progImage[i], progReadBack[i]));
                            }
                        }
                        TestReport.Equal((byte)1, progReadBack[17], "编程模式写入的转向");
                    }

                    /* while the 4-way bridge owns the line the programming mode is refused */
                    transport.Am32ProgBusyReplies = 0;
                    using (Am32Bridge bridge = new Am32Bridge(client))
                    {
                        bridge.Open(1);
                        HostStatusException notReady = TestReport.Throws<HostStatusException>(delegate
                        {
                            client.Am32ProgWrite(1, 17, 0, 2);
                        }, "桥接占用时写入");
                        TestReport.Equal(HostStatus.ErrNotReady, notReady.Status, "占用时状态码");
                        bridge.Close();
                    }
                    TestReport.Throws<HostException>(delegate
                    {
                        client.Am32ProgWrite(0, 192, 0, 1);
                    }, "地址越界");
                }
            });

            report.Case("模拟设备：AM32 4way 接口模式读取 / 写入 EEPROM", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.Open();
                    using (Am32Bridge bridge = new Am32Bridge(client))
                    {
                        bridge.Open(1);
                        bridge.Mode = FourWayFrameMode.Interface4Way;
                        bridge.TestAlive();
                        byte[] image = bridge.ReadEeprom();
                        TestReport.Size(image, Am32EepromMap.Size, "EEPROM 长度");
                        image[17] = 1;
                        bridge.WriteEeprom(17, new byte[] { 0x01 });
                        byte[] readBack = bridge.ReadEeprom();
                        TestReport.Equal((byte)1, readBack[17], "接口模式写入生效");
                        bridge.Close();
                    }
                }
            });

            report.Case("模拟设备：AM32 原始字节终端", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.Open();
                    using (Am32Bridge bridge = new Am32Bridge(client))
                    {
                        bridge.Open(0);
                        bridge.Mode = FourWayFrameMode.RawBytes;
                        byte[] frame = FourWayCodec.BuildBootloaderFrame(FourWayCommand.SetAddress,
                            new byte[] { 0x00, 0x00 });
                        byte[] reply = bridge.RawExchange(frame);
                        TestReport.Size(reply, 1, "原始应答长度");
                        TestReport.Equal((byte)0, reply[0], "SET_ADDRESS ACK");
                        bridge.Close();
                    }
                }
            });

            report.Case("模拟设备：AM32 未 BEGIN 时返回 ERR_NOT_READY", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.Open();
                    HostStatusException notReady = TestReport.Throws<HostStatusException>(delegate
                    {
                        client.Am32Xfer(0, new byte[] { 0x00, 0x30, 0x00, 0x00, 0x00 });
                    }, "未开始桥接");
                    TestReport.Equal(HostStatus.ErrNotReady, notReady.Status, "状态码");
                }
            });
        }

        /* ===================================================================== */
        /* 7. retries / timeout                                                  */
        /* ===================================================================== */

        private static void TestRetryAndTimeout(TestReport report)
        {
            report.Case("应答丢失后自动重试并成功", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.TimeoutMs = 150;
                    client.Retries = 2;
                    client.Open();
                    int retriesBefore = client.RetryCount;
                    transport.DropNextResponses = 1;
                    VersionInfo version = client.GetVersion();
                    TestReport.Equal((ushort)1, version.Major, "重试后应拿到版本");
                    TestReport.True(client.RetryCount > retriesBefore, "应记录重试次数");
                }
            });

            report.Case("应答 CRC 损坏后解析器丢弃并重试成功", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.TimeoutMs = 150;
                    client.Retries = 2;
                    client.Open();
                    int crcBefore = client.ParserCrcErrors;
                    transport.CorruptNextResponses = 1;
                    TelemetryData telemetry = client.GetTelemetry();
                    TestReport.True(telemetry.Complete, "重试后应拿到遥测");
                    TestReport.True(client.ParserCrcErrors > crcBefore, "应检测到 CRC 错误");
                }
            });

            report.Case("无重试时超时抛出 HostTimeoutException", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    client.TimeoutMs = 100;
                    client.Retries = 0;
                    client.Open();
                    transport.DropNextResponses = 1;
                    TestReport.Throws<HostTimeoutException>(delegate
                    {
                        client.GetVersion();
                    }, "超时");
                }
            });

            report.Case("断开连接后请求被拒绝", delegate
            {
                using (FakeFirmwareTransport transport = new FakeFirmwareTransport())
                using (BoardClient client = new BoardClient(transport))
                {
                    TestReport.Throws<HostException>(delegate
                    {
                        client.Ping();
                    }, "未连接时请求");
                }
            });
        }

        /* ===================================================================== */
        /* 8. log + UI                                                           */
        /* ===================================================================== */

        private static void TestLogAndUi(TestReport report)
        {
            report.Case("日志缓冲区：时间戳 / 十六进制 / 保存文件", delegate
            {
                LogBuffer log = new LogBuffer();
                log.Info("协议", "连接成功");
                log.Warning("参数", "参数越界: 5000");
                log.AddTrace("协议", TraceDirection.Tx,
                    new byte[] { 0xA5, 0x5A, 0x01, 0x01 }, "请求 CMD=0x01");
                TestReport.Equal(3, log.Count, "日志条数");
                string text = log.FormatAll(false);
                TestReport.Contains(text, "连接成功", "普通日志");
                TestReport.Contains(text, "警告", "警告级别");
                string hexText = log.FormatAll(true);
                TestReport.Contains(hexText, "A5 5A 01 01", "十六进制");
                TestReport.Contains(hexText, "请求 CMD=0x01", "跟踪文本");

                string path = Path.Combine(Path.GetTempPath(), "ecat_host_selftest_log.txt");
                int lines = log.SaveToFile(path, true);
                TestReport.Equal(3, lines, "保存条数");
                string saved = File.ReadAllText(path, Encoding.UTF8);
                TestReport.Contains(saved, "连接成功", "落盘内容");
                File.Delete(path);
                log.Clear();
                TestReport.Equal(0, log.Count, "清空后条数");
            });

            report.Case("界面主窗口可构建（无硬件冒烟测试）", delegate
            {
                try
                {
                    using (EcatDroneHost.Ui.MainForm form = new EcatDroneHost.Ui.MainForm(true))
                    {
                        form.CreateControl();
                        TestReport.True(form.Controls.Count > 0, "主窗口应包含控件");
                    }
                }
                catch (Exception ex)
                {
                    throw new TestFailure("界面构建异常: " + ex.GetType().Name + ": " + ex.Message);
                }
            });
        }
    }

    /// <summary>
    /// "--uitest": shows the real main window against the in-process simulated
    /// device, pumps the message loop through every tab and reports whether the
    /// telemetry pipeline / tab handlers raised any exception. Intended for CI
    /// style verification on a machine with a desktop.
    /// </summary>
    public static class UiRuntimeTest
    {
        public static int Run()
        {
            StringBuilder output = new StringBuilder();
            int errors = 0;
            output.AppendLine("ecat_drone_host 界面运行检查（模拟设备）");
            output.AppendLine("==================================================================");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                errors++;
                output.AppendLine("[界面异常] " + e.Exception.GetType().Name + ": " + e.Exception.Message);
            };

            EcatDroneHost.Ui.MainForm form = null;
            try
            {
                form = new EcatDroneHost.Ui.MainForm(true);
                form.Show();
                Pump(form, 900);
                output.AppendLine("连接状态: " + (form.IsConnected ? "已连接（模拟设备）" : "未连接"));
                output.AppendLine("页签数量: " + form.TabCount);
                for (int i = 0; i < form.TabCount; i++)
                {
                    form.SelectTab(i);
                    Pump(form, 240);
                    output.AppendLine("  [OK] 页签 " + (i + 1) + "/" + form.TabCount + " : " + form.TabTitle(i));
                }
                Pump(form, 400);
                output.AppendLine("遥测更新次数: " + form.TelemetryUpdatesShown);
                if (form.LastTelemetry != null)
                {
                    output.AppendLine("最近遥测: " + form.LastTelemetry.ToSummary());
                }
                if (!form.IsConnected)
                {
                    errors++;
                    output.AppendLine("[FAIL] 模拟设备未能自动连接");
                }
                if (form.TelemetryUpdatesShown <= 0)
                {
                    errors++;
                    output.AppendLine("[FAIL] 20 Hz 遥测没有更新到界面");
                }
                if (form.TabCount != 8)
                {
                    errors++;
                    output.AppendLine("[FAIL] 页签数量应为 8，实际 " + form.TabCount);
                }
            }
            catch (Exception ex)
            {
                errors++;
                output.AppendLine("[异常] " + ex.GetType().Name + ": " + ex.Message);
                output.AppendLine(ex.StackTrace);
            }
            finally
            {
                if (form != null)
                {
                    try
                    {
                        form.Close();
                        form.Dispose();
                    }
                    catch (Exception)
                    {
                        /* ignore */
                    }
                }
            }

            output.AppendLine("==================================================================");
            output.AppendLine("界面运行检查: " + (errors == 0 ? "PASS" : "FAIL，错误 " + errors + " 个"));
            Console.Write(output.ToString());
            Console.Out.Flush();
            try
            {
                string path = Path.Combine(GetExecutableDirectory(), "uitest.log");
                File.WriteAllText(path, output.ToString(), new UTF8Encoding(true));
                Console.WriteLine("界面检查报告已保存: " + path);
            }
            catch (Exception ex)
            {
                Console.WriteLine("无法保存界面检查报告: " + ex.Message);
            }
            return errors == 0 ? 0 : 1;
        }

        private static void Pump(MainForm form, int milliseconds)
        {
            DateTime end = DateTime.Now.AddMilliseconds(milliseconds);
            while (DateTime.Now < end)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(15);
            }
        }

        private static string GetExecutableDirectory()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location))
                {
                    string dir = Path.GetDirectoryName(location);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        return dir;
                    }
                }
            }
            catch (Exception)
            {
                /* ignore */
            }
            return Environment.CurrentDirectory;
        }
    }
}
