/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * Am32Bridge.cs - high level AM32 configuration client on top of the board's
 *                 4-way byte pipe (HOST_CMD_AM32_BEGIN / XFER / END).
 *
 * The board is a transparent bit-banged bridge: one AM32_XFER carries exactly
 * one 4-way frame, the response contains the raw bytes the ESC answered.
 * Framing/CRC is done here (see Protocol/FourWay.cs).
 *
 * Read / write EEPROM workflows:
 *   ContainerBootloader : 0x04 READ_EEPROM / 0x05 PROG_EEPROM in the
 *                         [ESC][CMD][LEN][PARAM][CRC16-XMODEM] container.
 *   Interface4Way       : 0x3D DEVICE_READ_EEPROM / 0x3E DEVICE_WRITE_EEPROM in
 *                         the BLHeliSuite interface frame.
 *   RawBytes            : user supplied bytes (raw terminal).
 */

using System;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.App
{
    /// <summary>Transport capability the AM32 bridge needs (implemented by BoardClient).</summary>
    public interface IAm32BridgeTransport
    {
        void Am32Begin(int esc);
        void Am32End(int esc);
        byte[] Am32Xfer(int esc, byte[] data);
    }

    /// <summary>AM32 / BLHeli 4-way bridge session.</summary>
    public sealed class Am32Bridge : IDisposable
    {
        public const int EepromSize = Am32EepromMap.Size;

        private readonly IAm32BridgeTransport _transport;
        private int _esc;
        private bool _open;

        public Am32Bridge(IAm32BridgeTransport transport)
        {
            if (transport == null)
            {
                throw new ArgumentNullException("transport");
            }
            _transport = transport;
            Mode = FourWayFrameMode.ContainerBootloader;
            TimeoutRetries = 1;
        }

        public event EventHandler<TraceEventArgs> Trace;
        public event EventHandler<TextEventArgs> Log;

        /// <summary>Which ESC (0/1) the bridge is currently talking to.</summary>
        public int Esc
        {
            get { return _esc; }
        }

        public bool IsOpen
        {
            get { return _open; }
        }

        public FourWayFrameMode Mode { get; set; }

        /// <summary>Number of retries for a single 4-way exchange on protocol errors.</summary>
        public int TimeoutRetries { get; set; }

        /// <summary>Take the ESC line and start a session (HOST_CMD_AM32_BEGIN).</summary>
        public void Open(int esc)
        {
            if (esc < 0 || esc > 1)
            {
                throw new HostException("ESC 编号必须是 0 或 1");
            }
            if (_open)
            {
                Close();
            }
            _transport.Am32Begin(esc);
            _esc = esc;
            _open = true;
            RaiseLog(string.Format("AM32 桥已打开 (ESC{0}, 模式: {1})", esc + 1, ModeText(Mode)));
        }

        /// <summary>Release the line (HOST_CMD_AM32_END).</summary>
        public void Close()
        {
            if (!_open)
            {
                return;
            }
            _open = false;
            try
            {
                _transport.Am32End(_esc);
            }
            finally
            {
                RaiseLog("AM32 桥已关闭，DShot 输出恢复");
            }
        }

        public void Dispose()
        {
            Close();
        }

        public static string ModeText(FourWayFrameMode mode)
        {
            switch (mode)
            {
                case FourWayFrameMode.ContainerBootloader: return "引导命令容器 0x04/0x05";
                case FourWayFrameMode.Interface4Way: return "4way 接口帧 0x3D/0x3E";
                case FourWayFrameMode.RawBytes: return "原始字节";
                default: return mode.ToString();
            }
        }

        /* ------------------------------- exchange ------------------------------ */

        /// <summary>Send one raw frame / byte block and return the raw answer.</summary>
        public byte[] Exchange(byte[] frame)
        {
            if (!_open)
            {
                throw new HostException("AM32 桥未打开（请先点“开始桥接”）");
            }
            if (frame == null || frame.Length == 0)
            {
                throw new HostException("AM32 发送数据为空");
            }
            RaiseTrace(TraceDirection.Tx, "AM32", frame,
                string.Format("AM32 TX ({0} B): {1}", frame.Length, Bytes.ToHexString(frame)));
            byte[] reply = _transport.Am32Xfer(_esc, frame);
            RaiseTrace(TraceDirection.Rx, "AM32", reply,
                string.Format("AM32 RX ({0} B): {1}", reply.Length,
                    reply.Length == 0 ? "(无应答)" : Bytes.ToHexString(reply)));
            return reply;
        }

        /// <summary>Raw terminal: send arbitrary bytes, show the answer.</summary>
        public byte[] RawExchange(byte[] data)
        {
            return Exchange(data);
        }

        /* ------------------------------ commands ------------------------------- */

        /// <summary>TEST_ALIVE: throws when the ESC does not answer with ACK OK.</summary>
        public void TestAlive()
        {
            if (Mode == FourWayFrameMode.Interface4Way)
            {
                byte[] frame = FourWayCodec.BuildInterface(FourWayCommand.TestAlive, 0, null);
                FourWayReply reply = ParseInterfaceReply(Exchange(frame));
                RequireAck(reply, "TEST_ALIVE");
                RaiseLog(string.Format("AM32 ESC{0} 在线 (4way 接口应答正常)", _esc + 1));
                return;
            }

            byte[] container = FourWayCodec.BuildContainer((byte)_esc, FourWayCommand.TestAlive, null);
            byte[] answer = Exchange(container);
            byte ack = answer.Length > 0 ? answer[answer.Length - 1] : (byte)0xFF;
            if (!FourWayAck.IsSuccess(ack))
            {
                throw new HostException("AM32 TEST_ALIVE 失败: " + FourWayAck.Describe(ack) +
                                        "（应答 " + Bytes.ToHexString(answer) + "）");
            }
            RaiseLog(string.Format("AM32 ESC{0} 在线", _esc + 1));
        }

        /// <summary>Read the whole 192 byte EEPROM image.</summary>
        public byte[] ReadEeprom()
        {
            return ReadEeprom(0, EepromSize);
        }

        /// <summary>Read a range of the EEPROM image.</summary>
        public byte[] ReadEeprom(int start, int count)
        {
            if (start < 0 || count <= 0 || start + count > EepromSize)
            {
                throw new HostException("EEPROM 读取范围无效");
            }

            if (Mode == FourWayFrameMode.Interface4Way)
            {
                byte[] frame = FourWayCodec.BuildInterface(FourWayCommand.DeviceReadEeprom, start,
                    new byte[] { (byte)count });
                FourWayReply reply = ParseInterfaceReply(Exchange(frame));
                RequireAck(reply, "READ_EEPROM");
                if (reply.Data.Length < count)
                {
                    throw new HostException(string.Format(
                        "EEPROM 读取长度不足: 收到 {0}，期望 {1}", reply.Data.Length, count));
                }
                byte[] data = new byte[count];
                Array.Copy(reply.Data, 0, data, 0, count);
                return data;
            }

            if (Mode == FourWayFrameMode.RawBytes)
            {
                throw new HostException("原始字节模式下不提供自动 EEPROM 读取，请使用原始终端");
            }

            /* container mode: PARAM = [start, count] */
            byte[] container = FourWayCodec.BuildContainer((byte)_esc, FourWayCommand.ReadEeprom,
                new byte[] { (byte)start, (byte)count });
            byte[] answer = Exchange(container);
            return ExtractContainerRead(answer, count);
        }

        /// <summary>Write / verify the whole 192 byte EEPROM image.</summary>
        public void WriteEeprom(byte[] image, bool verify)
        {
            if (image == null || image.Length != EepromSize)
            {
                throw new HostException("AM32 EEPROM 镜像必须是 " + EepromSize + " 字节");
            }
            WriteEeprom(0, image);
            if (!verify)
            {
                return;
            }
            byte[] readBack = ReadEeprom();
            for (int i = 0; i < EepromSize; i++)
            {
                if (readBack[i] != image[i])
                {
                    throw new HostException(string.Format(
                        "EEPROM 写入校验失败: 偏移 {0} 写入 0x{1:X2} 读回 0x{2:X2}",
                        i, image[i], readBack[i]));
                }
            }
            RaiseLog("AM32 EEPROM 写入并校验成功");
        }

        /// <summary>Write a range of the EEPROM image.</summary>
        public void WriteEeprom(int start, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                throw new HostException("AM32 写入数据为空");
            }
            if (start < 0 || start + data.Length > EepromSize)
            {
                throw new HostException("EEPROM 写入范围无效");
            }

            if (Mode == FourWayFrameMode.Interface4Way)
            {
                byte[] frame = FourWayCodec.BuildInterface(FourWayCommand.DeviceWriteEeprom, start, data);
                FourWayReply reply = ParseInterfaceReply(Exchange(frame));
                RequireAck(reply, "PROG_EEPROM");
                RaiseLog("AM32 EEPROM 写入完成 (4way 接口)");
                return;
            }

            if (Mode == FourWayFrameMode.RawBytes)
            {
                throw new HostException("原始字节模式下不提供自动 EEPROM 写入，请使用原始终端");
            }

            byte[] param = new byte[2 + data.Length];
            param[0] = (byte)start;
            param[1] = (byte)data.Length;
            Array.Copy(data, 0, param, 2, data.Length);
            byte[] container = FourWayCodec.BuildContainer((byte)_esc, FourWayCommand.ProgEeprom, param);
            byte[] answer = Exchange(container);
            byte ack = answer.Length > 0 ? answer[answer.Length - 1] : (byte)0xFF;
            if (!FourWayAck.IsSuccess(ack))
            {
                throw new HostException("AM32 写 EEPROM 失败: " + FourWayAck.Describe(ack));
            }
            RaiseLog(string.Format("AM32 EEPROM 写入完成: {0} 字节 @ {1}", data.Length, start));
        }

        /// <summary>Leave the boot-loader (RUN) / close the interface (0x34).</summary>
        public void ExitBootloader()
        {
            if (Mode == FourWayFrameMode.Interface4Way)
            {
                byte[] frame = FourWayCodec.BuildInterface(FourWayCommand.InterfaceExit, 0, null);
                byte[] answer = Exchange(frame);
                FourWayReply reply;
                string error;
                if (FourWayCodec.TryParseInterfaceReply(answer, out reply, out error))
                {
                    RaiseLog("AM32 接口已退出: " + FourWayAck.Describe(reply.Ack));
                }
                return;
            }
            byte[] container = FourWayCodec.BuildContainer((byte)_esc, FourWayCommand.Run, null);
            Exchange(container);
            RaiseLog("AM32 已退出引导程序 (RUN)");
        }

        /* ------------------------------- helpers ------------------------------- */

        private FourWayReply ParseInterfaceReply(byte[] raw)
        {
            FourWayReply reply;
            string error;
            if (!FourWayCodec.TryParseInterfaceReply(raw, out reply, out error))
            {
                throw new HostException("AM32 4way 接口应答解析失败: " + error +
                                        " 原始数据: " + Bytes.ToHexString(raw));
            }
            if (!reply.CrcOk)
            {
                throw new HostException("AM32 4way 接口应答 CRC 错误");
            }
            return reply;
        }

        private static void RequireAck(FourWayReply reply, string operation)
        {
            if (!reply.AckOk)
            {
                throw new HostException(string.Format("AM32 {0} 失败: {1} (0x{2:X2})",
                    operation, FourWayAck.Describe(reply.Ack), reply.Ack));
            }
        }

        private static byte[] ExtractContainerRead(byte[] answer, int count)
        {
            if (answer == null || answer.Length < count)
            {
                throw new HostException(string.Format(
                    "AM32 读 EEPROM 应答过短: {0} 字节（期望至少 {1}）",
                    answer == null ? 0 : answer.Length, count));
            }
            byte[] data = new byte[count];
            Array.Copy(answer, 0, data, 0, count);
            return data;
        }

        private void RaiseTrace(TraceDirection direction, string channel, byte[] data, string text)
        {
            EventHandler<TraceEventArgs> handler = Trace;
            if (handler == null)
            {
                return;
            }
            try
            {
                handler(this, new TraceEventArgs(direction, channel, data, text));
            }
            catch (Exception)
            {
                /* ignore broken subscribers */
            }
        }

        private void RaiseLog(string text)
        {
            EventHandler<TextEventArgs> handler = Log;
            if (handler == null)
            {
                return;
            }
            try
            {
                handler(this, new TextEventArgs(text));
            }
            catch (Exception)
            {
                /* ignore */
            }
        }
    }
}
