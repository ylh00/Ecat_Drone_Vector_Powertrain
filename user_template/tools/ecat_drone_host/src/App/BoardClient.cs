/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * BoardClient.cs - request/response client for the USB CDC host protocol.
 *
 *   - one dedicated background receiver thread reads the transport, feeds a
 *     resynchronising FrameParser and dispatches completed frames,
 *   - every request is matched by CMD + SEQ, response payload[0] is the status,
 *   - timeout 300 ms, 2 retries (3 attempts) by default,
 *   - requests are serialised (the link is request/response; the UI thread and
 *     the telemetry poller can both call this class safely).
 */

using System;
using System.Collections.Generic;
using System.Threading;
using EcatDroneHost.Protocol;
using EcatDroneHost.Transport;

namespace EcatDroneHost.App
{
    /// <summary>Client for the board host protocol (thread safe).</summary>
    public sealed class BoardClient : IDisposable, IAm32BridgeTransport
    {
        /// <summary>Maximum number of 70 byte descriptors the firmware returns per LIST_PARAMS.</summary>
        public const int MaxDescriptorsPerRequest = 7;

        private readonly IByteTransport _transport;
        private readonly FrameParser _parser = new FrameParser();
        private readonly object _txGate = new object();
        private readonly object _rxGate = new object();

        private Thread _reader;
        private volatile bool _running;
        private PendingRequest _pending;
        private byte _seq;
        private int _timeoutMs = HostProto.DefaultTimeoutMs;
        private int _retries = HostProto.DefaultRetries;

        public BoardClient(IByteTransport transport)
        {
            if (transport == null)
            {
                throw new ArgumentNullException("transport");
            }
            _transport = transport;
        }

        /* ------------------------- events / statistics ------------------------- */

        public event EventHandler<TraceEventArgs> Trace;
        public event EventHandler<TextEventArgs> Log;

        public int RequestCount { get; private set; }
        public int TimeoutCount { get; private set; }
        public int RetryCount { get; private set; }
        public int StatusErrorCount { get; private set; }

        public int ParserCrcErrors
        {
            get { return _parser.CrcErrors; }
        }

        public int ParserResyncs
        {
            get { return _parser.Resyncs; }
        }

        public int ParserFramesOk
        {
            get { return _parser.FramesOk; }
        }

        public string TransportDescription
        {
            get { return _transport.Description; }
        }

        public bool IsConnected
        {
            get { return _running && _transport.IsOpen; }
        }

        /// <summary>Per request timeout (default 300 ms, from HostProto).</summary>
        public int TimeoutMs
        {
            get { return _timeoutMs; }
            set { _timeoutMs = value < 20 ? 20 : value; }
        }

        /// <summary>Number of retries after the first attempt (default 2).</summary>
        public int Retries
        {
            get { return _retries; }
            set { _retries = value < 0 ? 0 : value; }
        }

        /* ------------------------------ lifecycle ------------------------------ */

        public void Open()
        {
            if (IsConnected)
            {
                return;
            }
            _parser.Reset();
            _parser.ResetStatistics();
            RequestCount = 0;
            TimeoutCount = 0;
            RetryCount = 0;
            StatusErrorCount = 0;
            _transport.ReadTimeoutMs = 30;
            _transport.Open();
            _running = true;
            _reader = new Thread(ReaderLoop);
            _reader.IsBackground = true;
            _reader.Name = "ecat-host-rx";
            _reader.Start();
            RaiseLog("已打开 " + _transport.Description);
        }

        public void Close()
        {
            bool wasRunning = _running;
            _running = false;
            lock (_rxGate)
            {
                if (_pending != null)
                {
                    _pending.Done = true;
                    _pending.Aborted = true;
                    Monitor.PulseAll(_rxGate);
                }
            }
            try
            {
                _transport.Close();
            }
            catch (Exception)
            {
                /* ignore */
            }
            Thread reader = _reader;
            _reader = null;
            if (reader != null && reader.IsAlive && reader != Thread.CurrentThread)
            {
                try
                {
                    reader.Join(500);
                }
                catch (Exception)
                {
                    /* ignore */
                }
            }
            if (wasRunning)
            {
                RaiseLog("已关闭连接");
            }
        }

        public void Dispose()
        {
            Close();
            _transport.Dispose();
        }

        /* --------------------------- request / response ------------------------ */

        /// <summary>Send a request and return the response body (status byte stripped).</summary>
        public byte[] Request(HostCmd cmd, byte[] payload)
        {
            return Request(cmd, payload, _timeoutMs, _retries);
        }

        /// <summary>Send a request with an explicit timeout / retry count.</summary>
        public byte[] Request(HostCmd cmd, byte[] payload, int timeoutMs, int retries)
        {
            if (!IsConnected)
            {
                throw new HostException("设备未连接");
            }
            lock (_txGate)
            {
                byte seq = NextSequence();
                byte[] frame = FrameCodec.Encode(cmd, seq, payload);
                int attempts = retries + 1;
                if (attempts < 1)
                {
                    attempts = 1;
                }
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    PendingRequest pending = new PendingRequest();
                    pending.Seq = seq;
                    pending.Cmd = cmd;
                    lock (_rxGate)
                    {
                        _pending = pending;
                    }

                    RaiseTrace(TraceDirection.Tx, "协议", frame,
                        DescribeRequest(cmd, seq, payload));
                    try
                    {
                        _transport.Write(frame, 0, frame.Length);
                    }
                    catch (Exception)
                    {
                        lock (_rxGate)
                        {
                            _pending = null;
                        }
                        throw;
                    }
                    RequestCount++;

                    bool done;
                    bool aborted;
                    byte[] response;
                    lock (_rxGate)
                    {
                        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                        while (!pending.Done)
                        {
                            int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                            if (remaining <= 0)
                            {
                                break;
                            }
                            Monitor.Wait(_rxGate, remaining);
                        }
                        done = pending.Done;
                        aborted = pending.Aborted;
                        response = pending.Payload;
                        _pending = null;
                    }

                    if (aborted)
                    {
                        throw new HostException("连接已关闭");
                    }

                    if (done && response != null && response.Length >= 1)
                    {
                        HostStatus status = (HostStatus)response[0];
                        if (status != HostStatus.Ok)
                        {
                            StatusErrorCount++;
                            throw new HostStatusException(cmd, status);
                        }
                        byte[] body = new byte[response.Length - 1];
                        Array.Copy(response, 1, body, 0, body.Length);
                        return body;
                    }

                    if (done)
                    {
                        throw new HostException(string.Format(
                            "命令 0x{0:X2} 应答格式错误（无状态字节）", (byte)cmd));
                    }

                    TimeoutCount++;
                    if (attempt < attempts)
                    {
                        RetryCount++;
                        RaiseLog(string.Format("命令 0x{0:X2} 超时，重试 {1}/{2}",
                            (byte)cmd, attempt, attempts - 1));
                    }
                }
                throw new HostTimeoutException(cmd, timeoutMs, attempts);
            }
        }

        /// <summary>Like Request but returns false instead of throwing on timeout / status error.</summary>
        public bool TryRequest(HostCmd cmd, byte[] payload, int timeoutMs, int retries,
                               out byte[] body, out string error)
        {
            body = null;
            error = null;
            try
            {
                body = Request(cmd, payload, timeoutMs, retries);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private byte NextSequence()
        {
            _seq++;
            if (_seq == 0)
            {
                _seq = 1;
            }
            return _seq;
        }

        /* ------------------------- background receiver ------------------------- */

        private void ReaderLoop()
        {
            byte[] buffer = new byte[1024];
            while (_running)
            {
                int count;
                try
                {
                    count = _transport.Read(buffer, 0, buffer.Length);
                }
                catch (Exception ex)
                {
                    if (_running)
                    {
                        RaiseLog("串口读取异常: " + ex.Message);
                    }
                    break;
                }
                if (count <= 0)
                {
                    continue;
                }
                _parser.Feed(buffer, 0, count);
                HostFrame frame;
                while (_parser.TryRead(out frame))
                {
                    Dispatch(frame);
                }
            }
        }

        private void Dispatch(HostFrame frame)
        {
            byte[] raw = FrameCodec.Encode(frame.Cmd, frame.Seq, frame.Payload);
            string statusText = string.Empty;
            HostStatus status;
            if (frame.TryGetStatus(out status))
            {
                statusText = " status=" + HostStatusText.Describe(status) +
                             " (0x" + ((byte)status).ToString("X2") + ")";
            }
            RaiseTrace(TraceDirection.Rx, "协议", raw,
                string.Format("应答 CMD=0x{0:X2} SEQ={1} LEN={2}{3}",
                    frame.Cmd, frame.Seq, frame.Payload.Length, statusText));

            bool matched = false;
            lock (_rxGate)
            {
                if (_pending != null && _pending.Seq == frame.Seq && (byte)_pending.Cmd == frame.Cmd)
                {
                    _pending.Payload = frame.Payload;
                    _pending.Done = true;
                    matched = true;
                    Monitor.PulseAll(_rxGate);
                }
            }
            if (!matched)
            {
                RaiseLog(string.Format("收到未匹配的应答 CMD=0x{0:X2} SEQ={1}（已忽略）",
                    frame.Cmd, frame.Seq));
            }
        }

        /* --------------------------- high level API ---------------------------- */

        /// <summary>PING: verifies the device magic and reports the firmware version.</summary>
        public string Ping()
        {
            byte[] body = Request(HostCmd.Ping, null);
            if (body.Length >= 10)
            {
                uint magic = Bytes.GetU32(body, 0);
                ushort major = Bytes.GetU16(body, 4);
                ushort minor = Bytes.GetU16(body, 6);
                ushort patch = Bytes.GetU16(body, 8);
                string magicText = Printable(System.Text.Encoding.ASCII.GetString(body, 0, 4));
                if (magic != 0x474E4950U)
                {
                    throw new HostException(string.Format(
                        "PING 应答 magic 错误: 0x{0:X8}（期望 0x474E4950 \"PING\"）", magic));
                }
                return string.Format("PING OK magic={0} 固件 v{1}.{2}.{3}", magicText, major, minor, patch);
            }
            if (body.Length >= 4)
            {
                return "PING OK magic=" + Printable(System.Text.Encoding.ASCII.GetString(body, 0, 4));
            }
            return "PING OK (" + body.Length + " 字节)";
        }

        public VersionInfo GetVersion()
        {
            return VersionInfo.Parse(Request(HostCmd.GetVersion, null));
        }

        public TelemetryData GetTelemetry()
        {
            byte[] body = Request(HostCmd.GetTelemetry, null);
            if (body.Length != TelemetryData.WireSize)
            {
                RaiseLog(string.Format("遥测长度 {0} 字节（期望 {1}），已按可用字段解析",
                    body.Length, TelemetryData.WireSize));
            }
            return TelemetryData.Parse(body);
        }

        public EcatStatus GetEcatStatus()
        {
            return EcatStatus.Parse(Request(HostCmd.GetEcatStatus, null));
        }

        public int GetParamCount()
        {
            byte[] body = Request(HostCmd.ParamGetCount, null);
            if (body.Length < 2)
            {
                throw new HostException("参数数量应答长度不足");
            }
            return Bytes.GetU16(body, 0);
        }

        /// <summary>Read all parameter descriptors, chunked to honour the 512 byte payload limit.</summary>
        public ParamDesc[] ListParams()
        {
            return ListParams(GetParamCount());
        }

        /// <summary>Read <paramref name="count"/> descriptors starting at 0.</summary>
        public ParamDesc[] ListParams(int count)
        {
            return ListParams(0, count);
        }

        public ParamDesc[] ListParams(int start, int count)
        {
            List<ParamDesc> list = new List<ParamDesc>(count);
            int chunk = MaxDescriptorsPerRequest;
            while (list.Count < count)
            {
                int remaining = count - list.Count;
                int n = Math.Min(chunk, remaining);
                byte[] payload = new byte[3];
                Bytes.PutU16(payload, 0, start + list.Count);
                payload[2] = (byte)n;
                byte[] body = Request(HostCmd.ListParams, payload);
                list.AddRange(ParamDesc.FromList(body, n));
            }
            return list.ToArray();
        }

        public ParamDesc GetParamDesc(int id)
        {
            byte[] payload = new byte[2];
            Bytes.PutU16(payload, 0, id);
            return ParamDesc.FromBody(Request(HostCmd.GetParamDesc, payload));
        }

        public int GetParam(int id)
        {
            byte[] payload = new byte[2];
            Bytes.PutU16(payload, 0, id);
            byte[] body = Request(HostCmd.GetParam, payload);
            if (body.Length < 4)
            {
                throw new HostException("参数值应答长度不足");
            }
            return Bytes.GetI32(body, 0);
        }

        public void SetParam(int id, int value)
        {
            byte[] payload = new byte[6];
            Bytes.PutU16(payload, 0, id);
            Bytes.PutU32(payload, 2, (uint)value);
            Request(HostCmd.SetParam, payload);
        }

        public void ParamSave()
        {
            try
            {
                Request(HostCmd.ParamSave, null, 2000, 1);
            }
            catch (HostStatusException ex)
            {
                /* flash erase/program may fail once (weak sector) - retry once */
                if (ex.Status != HostStatus.ErrFlash)
                {
                    throw;
                }
                RaiseLog("参数保存返回 ERR_FLASH，重试一次");
                Request(HostCmd.ParamSave, null, 2500, 0);
            }
        }

        public void ParamReset()
        {
            Request(HostCmd.ParamReset, null, 1500, 1);
        }

        /// <summary>Throttle test: 0 = stop, or 1000..2000 µs (bench lease refreshed).</summary>
        public void DshotTest(int esc, int throttleUs)
        {
            byte[] payload = new byte[3];
            payload[0] = (byte)esc;
            Bytes.PutU16(payload, 1, throttleUs);
            Request(HostCmd.DshotTest, payload);
        }

        public DshotTelemetryData DshotGetTelemetry(int esc)
        {
            byte[] payload = new byte[1];
            payload[0] = (byte)esc;
            return DshotTelemetryData.Parse(Request(HostCmd.DshotGetTlm, payload));
        }

        /// <summary>Send a DShot command; esc = 0, 1 or 0xFF (broadcast to both).</summary>
        public void DshotCommand(int esc, int command)
        {
            byte[] payload = new byte[3];
            payload[0] = (byte)esc;
            Bytes.PutU16(payload, 1, command);
            Request(HostCmd.DshotCmd, payload);
        }

        /// <summary>Broadcast a DShot command to both ESCs.</summary>
        public void DshotCommandAll(int command)
        {
            DshotCommand(0xFF, command);
        }

        /// <summary>
        /// Scan a servo bus.  SERVO_SCAN is asynchronous: the firmware starts the
        /// scan and answers ERR_BUSY until it is finished (or ERR_TIMEOUT after
        /// ~10 s), so the identical request is retried here.
        /// Returns the responding ids.
        /// </summary>
        public int[] ServoScan(int bus, int idMin, int idMax)
        {
            byte[] payload = new byte[3];
            payload[0] = (byte)bus;
            payload[1] = (byte)idMin;
            payload[2] = (byte)idMax;

            DateTime deadline = DateTime.UtcNow.AddSeconds(12);
            int busyCount = 0;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    byte[] body = Request(HostCmd.ServoScan, payload, 2000, 0);
                    return ParseServoBitmap(body, idMin, idMax);
                }
                catch (HostStatusException ex)
                {
                    if (ex.Status == HostStatus.ErrBusy)
                    {
                        busyCount++;
                        Thread.Sleep(150);
                        continue;
                    }
                    if (ex.Status == HostStatus.ErrTimeout)
                    {
                        throw new HostException("舵机扫描超时（约 10 秒内无应答）——请检查舵机供电、接线与波特率参数");
                    }
                    throw;
                }
            }
            throw new HostException("舵机扫描超时（上位机等待超过 12 秒，ERR_BUSY 次数 " + busyCount + "）");
        }

        private static int[] ParseServoBitmap(byte[] body, int idMin, int idMax)
        {
            List<int> ids = new List<int>();
            for (int i = 0; i < body.Length && i < 32; i++)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((body[i] & (1 << bit)) != 0)
                    {
                        int id = i * 8 + bit;
                        if (id >= idMin && id <= idMax && id <= 253)
                        {
                            ids.Add(id);
                        }
                    }
                }
            }
            return ids.ToArray();
        }

        public void ServoMoveTest(int bus, int id, int angleD10)
        {
            byte[] payload = new byte[4];
            payload[0] = (byte)bus;
            payload[1] = (byte)id;
            Bytes.PutU16(payload, 2, (ushort)(short)angleD10);
            Request(HostCmd.ServoMoveTest, payload);
        }

        public ServoStatusData ServoRead(int bus, int id)
        {
            byte[] payload = new byte[2];
            payload[0] = (byte)bus;
            payload[1] = (byte)id;
            return ServoStatusData.Parse(Request(HostCmd.ServoRead, payload));
        }

        public void ServoSetId(int bus, int oldId, int newId)
        {
            byte[] payload = new byte[3];
            payload[0] = (byte)bus;
            payload[1] = (byte)oldId;
            payload[2] = (byte)newId;
            Request(HostCmd.ServoSetId, payload);
        }

        public AdcMonitorData AdcGet()
        {
            return AdcMonitorData.Parse(Request(HostCmd.AdcGet, null));
        }

        public void LedSet(int red, int green, int blue)
        {
            byte[] payload = new byte[3];
            payload[0] = (byte)red;
            payload[1] = (byte)green;
            payload[2] = (byte)blue;
            Request(HostCmd.LedSet, payload);
        }

        public void LedTest(int pattern, int durationMs)
        {
            byte[] payload = new byte[3];
            payload[0] = (byte)pattern;
            Bytes.PutU16(payload, 1, durationMs);
            Request(HostCmd.LedTest, payload);
        }

        public Am32BridgeStatus Am32Status(int esc)
        {
            byte[] payload = new byte[1];
            payload[0] = (byte)esc;
            return Am32BridgeStatus.Parse(Request(HostCmd.Am32Status, payload));
        }

        /* --------------- AM32 DShot programming mode (0x54 / 0x55) -------------- */

        /// <summary>
        /// Write one AM32 EEPROM byte through the firmware's DShot programming
        /// mode.  Each byte takes ~9 ms on the ESC signal line.  The firmware
        /// answers ERR_BUSY while its command queue is full; the request is
        /// retried after ~12 ms with the same esc/address/value.
        /// </summary>
        public void Am32ProgWrite(int esc, int address, int value, int busyRetries)
        {
            if (esc < 0 || esc > 1)
            {
                throw new HostException("ESC 编号必须是 0 或 1");
            }
            if (address < 0 || address > 191)
            {
                throw new HostException("AM32 EEPROM 地址必须是 0..191");
            }
            if (value < 0 || value > 255)
            {
                throw new HostException("AM32 参数值必须是 0..255");
            }
            byte[] payload = new byte[3];
            payload[0] = (byte)esc;
            payload[1] = (byte)address;
            payload[2] = (byte)value;

            int attempts = busyRetries < 0 ? 0 : busyRetries;
            for (int i = 0; i <= attempts; i++)
            {
                try
                {
                    Request(HostCmd.Am32ProgWrite, payload, 400, 0);
                    return;
                }
                catch (HostStatusException ex)
                {
                    if (ex.Status != HostStatus.ErrBusy || i == attempts)
                    {
                        throw;
                    }
                    /* command queue full: retry the identical request after 5..10 ms */
                    Thread.Sleep(8);
                }
            }
        }

        /// <summary>
        /// Commit the written AM32 EEPROM bytes to ESC flash (0x55).
        /// The firmware answers ERR_BUSY until the last queued bytes have been
        /// shifted out (~1.8 s worst case for 192 bytes), so poll with a bound.
        /// </summary>
        public void Am32ProgSave(int esc)
        {
            if (esc < 0 || esc > 1)
            {
                throw new HostException("ESC 编号必须是 0 或 1");
            }
            byte[] payload = new byte[1];
            payload[0] = (byte)esc;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(3000);
            while (true)
            {
                try
                {
                    Request(HostCmd.Am32ProgSave, payload, 500, 0);
                    return;
                }
                catch (HostStatusException ex)
                {
                    if (ex.Status != HostStatus.ErrBusy)
                    {
                        throw;
                    }
                    if (DateTime.UtcNow > deadline)
                    {
                        throw new HostException("AM32 保存超时：0x55 持续返回 ERR_BUSY（仍有字节在发送）");
                    }
                    Thread.Sleep(30);
                }
            }
        }

        /* -------------------- IAm32BridgeTransport implementation -------------- */

        public void Am32Begin(int esc)
        {
            byte[] payload = new byte[1];
            payload[0] = (byte)esc;
            Request(HostCmd.Am32Begin, payload, 1000, 1);
        }

        public void Am32End(int esc)
        {
            byte[] payload = new byte[1];
            payload[0] = (byte)esc;
            Request(HostCmd.Am32End, payload, 1000, 1);
        }

        public byte[] Am32Xfer(int esc, byte[] data)
        {
            if (data == null)
            {
                data = new byte[0];
            }
            if (data.Length > 255)
            {
                throw new HostException("AM32 单次传输超过 255 字节");
            }
            byte[] payload = new byte[2 + data.Length];
            payload[0] = (byte)esc;
            payload[1] = (byte)data.Length;
            Array.Copy(data, 0, payload, 2, data.Length);
            byte[] body = Request(HostCmd.Am32Xfer, payload, 1500, 0);
            if (body.Length < 1)
            {
                throw new HostException("AM32 应答缺少长度字段");
            }
            int outLength = body[0];
            if (body.Length < 1 + outLength)
            {
                throw new HostException(string.Format(
                    "AM32 应答长度 {0} 少于声明值 {1}", body.Length - 1, outLength));
            }
            byte[] result = new byte[outLength];
            Array.Copy(body, 1, result, 0, outLength);
            return result;
        }

        /* --------------------------- misc commands ----------------------------- */

        public bool EnterBootloader(out string error)
        {
            byte[] payload = new byte[4];
            Bytes.PutU32(payload, 0, HostProto.BootloaderMagic);
            byte[] body;
            return TryRequest(HostCmd.Bootloader, payload, 500, 0, out body, out error);
        }

        public bool Reset(out string error)
        {
            byte[] body;
            return TryRequest(HostCmd.Reset, null, 500, 0, out body, out error);
        }

        /* ------------------------------ helpers -------------------------------- */

        private string DescribeRequest(HostCmd cmd, byte seq, byte[] payload)
        {
            int length = payload == null ? 0 : payload.Length;
            return string.Format("请求 CMD=0x{0:X2} SEQ={1} LEN={2}", (byte)cmd, seq, length);
        }

        private static string Printable(string text)
        {
            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] < 32 || chars[i] > 126)
                {
                    chars[i] = '.';
                }
            }
            return new string(chars);
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
                /* a broken log subscriber must not kill the link */
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

        private sealed class PendingRequest
        {
            public byte Seq;
            public HostCmd Cmd;
            public byte[] Payload;
            public bool Done;
            public bool Aborted;
        }
    }
}
