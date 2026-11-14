/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * FakeFirmwareTransport.cs - in-process firmware simulator.
 *
 * Speaks the same host protocol as the real board (frames in / frames out) and
 * implements the main commands, so that
 *   - --selftest can verify the whole protocol + UI logic layer without hardware,
 *   - the GUI can be started with "--simulate" for demo / layout work.
 *
 * It is NOT a substitute for hardware: parameter ranges come from the offline
 * catalog and the AM32 EEPROM starts from a synthetic template.
 */

using System;
using System.Collections.Generic;
using System.Threading;
using EcatDroneHost.App;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.Transport
{
    /// <summary>In process replacement for the USB CDC link (simulated board).</summary>
    public sealed class FakeFirmwareTransport : IByteTransport
    {
        private readonly object _lock = new object();
        private readonly Queue<byte> _rxQueue = new Queue<byte>();
        private readonly FrameParser _parser = new FrameParser();

        private bool _open;
        private int _readTimeoutMs = 30;

        /* simulated device state */
        private readonly int[] _paramValues = new int[ParamCatalog.Count];
        private readonly int[] _throttleUs = new int[2];
        private readonly int[] _servoTargetD10 = new int[2];
        private readonly bool[] _am32SessionOpen = new bool[2];
        private readonly FakeAm32Esc[] _am32 = new FakeAm32Esc[2];
        private ushort _loopCounter;
        private uint _ecatCycles;
        private int _requestCount;
        private int _ledRed;
        private int _ledGreen;
        private int _ledBlue;
        private int _ledPattern;
        private bool _armed;
        private int _servoPresent0 = 1;
        private int _servoPresent1 = 2;
        private int _servoScanBusyReplies = 1;
        private int _am32ProgBusyReplies = 1;
        private int _am32ProgSaveBusyReplies = 2;

        public FakeFirmwareTransport()
        {
            _am32[0] = new FakeAm32Esc();
            _am32[1] = new FakeAm32Esc();
            ResetState();
        }

        /* ------------------------- fault injection knobs ----------------------- */

        /// <summary>Drop this many responses (the command is still executed).</summary>
        public int DropNextResponses { get; set; }

        /// <summary>Corrupt the CRC of this many responses.</summary>
        public int CorruptNextResponses { get; set; }

        /// <summary>Artificial delay before an AM32_XFER answer (ms).</summary>
        public int Am32XferDelayMs { get; set; }

        /// <summary>Artificial delay before any answer (ms).</summary>
        public int ResponseDelayMs { get; set; }

        /// <summary>
        /// How many SERVO_SCAN requests answer ERR_BUSY before the bitmap is
        /// returned (the real firmware scans asynchronously, default 1).
        /// </summary>
        public int ServoScanBusyReplies
        {
            get { return _servoScanBusyReplies; }
            set { _servoScanBusyReplies = value < 0 ? 0 : value; }
        }

        /// <summary>
        /// How many AM32_PROG_WRITE requests answer ERR_BUSY before they are
        /// accepted (the firmware command queue may be full, default 1).
        /// </summary>
        public int Am32ProgBusyReplies
        {
            get { return _am32ProgBusyReplies; }
            set { _am32ProgBusyReplies = value < 0 ? 0 : value; }
        }

        /// <summary>
        /// How many AM32_PROG_SAVE requests answer ERR_BUSY while the last bytes
        /// are still being shifted out (default 2).
        /// </summary>
        public int Am32ProgSaveBusyReplies
        {
            get { return _am32ProgSaveBusyReplies; }
            set { _am32ProgSaveBusyReplies = value < 0 ? 0 : value; }
        }

        /// <summary>Total number of request frames processed (diagnostics).</summary>
        public int RequestsProcessed
        {
            get { lock (_lock) { return _requestCount; } }
        }

        /* ----------------------------- IByteTransport -------------------------- */

        public string Description
        {
            get { return "模拟设备 (FakeFirmwareTransport)"; }
        }

        public bool IsOpen
        {
            get { lock (_lock) { return _open; } }
        }

        public int ReadTimeoutMs
        {
            get { return _readTimeoutMs; }
            set { _readTimeoutMs = value < 1 ? 1 : value; }
        }

        public void Open()
        {
            lock (_lock)
            {
                _open = true;
                _rxQueue.Clear();
                _parser.Reset();
                _parser.ResetStatistics();
                ResetState();
                Monitor.PulseAll(_lock);
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                _open = false;
                _rxQueue.Clear();
                Monitor.PulseAll(_lock);
            }
        }

        public void DiscardInput()
        {
            lock (_lock)
            {
                _rxQueue.Clear();
            }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                if (!_open)
                {
                    throw new HostException("模拟设备未打开");
                }
                if (_rxQueue.Count == 0 && count > 0)
                {
                    Monitor.Wait(_lock, _readTimeoutMs);
                }
                int available = Math.Min(count, _rxQueue.Count);
                for (int i = 0; i < available; i++)
                {
                    buffer[offset + i] = _rxQueue.Dequeue();
                }
                return available;
            }
        }

        public void Write(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0)
            {
                return;
            }
            lock (_lock)
            {
                if (!_open)
                {
                    throw new HostException("模拟设备未打开");
                }
                _parser.Feed(data, offset, count);
                HostFrame request;
                while (_parser.TryRead(out request))
                {
                    HandleRequest(request);
                }
                Monitor.PulseAll(_lock);
            }
        }

        public void Dispose()
        {
            Close();
        }

        /* ------------------------------ simulation ----------------------------- */

        private void ResetState()
        {
            for (int i = 0; i < _paramValues.Length; i++)
            {
                _paramValues[i] = ParamCatalog.GetDefault(i);
            }
            _throttleUs[0] = 1000;
            _throttleUs[1] = 1000;
            _servoTargetD10[0] = 0;
            _servoTargetD10[1] = 0;
            _am32SessionOpen[0] = false;
            _am32SessionOpen[1] = false;
            _am32[0].Reset();
            _am32[1].Reset();
            _loopCounter = 0;
            _ecatCycles = 0;
            _requestCount = 0;
            _ledRed = 0;
            _ledGreen = 16;
            _ledBlue = 0;
            _ledPattern = 0;
            _armed = false;
            _servoScanBusyReplies = 1;
            _am32ProgBusyReplies = 1;
            _am32ProgSaveBusyReplies = 2;
            _servoPresent0 = 1;
            _servoPresent1 = 2;
        }

        /// <summary>host_servo_check_id(): the id must match PARAM_SERVO_IDn.</summary>
        private bool CheckServoId(int bus, int id)
        {
            if (bus < 0 || bus > 1)
            {
                return false;
            }
            int expected = _paramValues[(int)(bus == 0 ? ParamId.ServoId1 : ParamId.ServoId2)];
            return id == expected;
        }

        private void HandleRequest(HostFrame request)
        {
            _requestCount++;
            if (ResponseDelayMs > 0)
            {
                Thread.Sleep(ResponseDelayMs);
            }

            switch (request.Command)
            {
                case HostCmd.Ping:
                {
                    /* u32 magic 0x474E4950 ('PING') + u16 major + u16 minor + u16 patch */
                    byte[] body = new byte[10];
                    Bytes.PutU32(body, 0, 0x474E4950U);
                    Bytes.PutU16(body, 4, 1);
                    Bytes.PutU16(body, 6, 0);
                    Bytes.PutU16(body, 8, 0);
                    RespondOk(request, body);
                    break;
                }
                case HostCmd.GetVersion:
                {
                    byte[] body = new byte[22];
                    Bytes.PutU16(body, 0, 1);
                    Bytes.PutU16(body, 2, 0);
                    Bytes.PutU16(body, 4, 0);
                    byte[] date = System.Text.Encoding.ASCII.GetBytes("Nov 14 2026");
                    Array.Copy(date, 0, body, 6, Math.Min(date.Length, 16));
                    RespondOk(request, body);
                    break;
                }
                case HostCmd.GetTelemetry:
                    RespondOk(request, BuildTelemetry());
                    break;
                case HostCmd.GetEcatStatus:
                {
                    byte[] body = new byte[8];
                    body[0] = 8;    /* OP */
                    body[1] = 0x03; /* both ports link up */
                    Bytes.PutU16(body, 2, 0);
                    Bytes.PutU32(body, 4, _ecatCycles);
                    _ecatCycles++;
                    RespondOk(request, body);
                    break;
                }
                case HostCmd.ParamGetCount:
                {
                    byte[] body = new byte[2];
                    Bytes.PutU16(body, 0, ParamCatalog.Count);
                    RespondOk(request, body);
                    break;
                }
                case HostCmd.ListParams:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 3)
                    {
                        RespondStatus(request, HostStatus.ErrLength);
                        break;
                    }
                    int start = Bytes.GetU16(p, 0);
                    int count = p[2];
                    if (count > 7)
                    {
                        count = 7; /* firmware contract: max 7 descriptors per response */
                    }
                    if (start >= ParamCatalog.Count)
                    {
                        RespondOk(request, new byte[0]);
                        break;
                    }
                    if (start + count > ParamCatalog.Count)
                    {
                        count = ParamCatalog.Count - start;
                    }
                    if (count <= 0)
                    {
                        RespondOk(request, new byte[0]);
                        break;
                    }
                    byte[] blob = new byte[count * ParamDesc.WireSize];
                    for (int i = 0; i < count; i++)
                    {
                        byte[] one = BuildDescriptor(start + i);
                        Array.Copy(one, 0, blob, i * ParamDesc.WireSize, one.Length);
                    }
                    RespondOk(request, blob);
                    break;
                }
                case HostCmd.GetParamDesc:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 2)
                    {
                        RespondStatus(request, HostStatus.ErrLength);
                        break;
                    }
                    int id = Bytes.GetU16(p, 0);
                    if (id >= ParamCatalog.Count)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    RespondOk(request, BuildDescriptor(id));
                    break;
                }
                case HostCmd.GetParam:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 2)
                    {
                        RespondStatus(request, HostStatus.ErrLength);
                        break;
                    }
                    int id = Bytes.GetU16(p, 0);
                    if (id >= ParamCatalog.Count)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    byte[] body = new byte[4];
                    Bytes.PutU32(body, 0, (uint)_paramValues[id]);
                    RespondOk(request, body);
                    break;
                }
                case HostCmd.SetParam:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 6)
                    {
                        RespondStatus(request, HostStatus.ErrLength);
                        break;
                    }
                    int id = Bytes.GetU16(p, 0);
                    int value = Bytes.GetI32(p, 2);
                    if (id >= ParamCatalog.Count)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    if (value < ParamCatalog.GetMin(id) || value > ParamCatalog.GetMax(id))
                    {
                        RespondStatus(request, HostStatus.ErrParamRange);
                        break;
                    }
                    _paramValues[id] = value;
                    RespondOk(request, null);
                    break;
                }
                case HostCmd.ParamSave:
                    RespondOk(request, null);
                    break;
                case HostCmd.ParamReset:
                    for (int i = 0; i < _paramValues.Length; i++)
                    {
                        _paramValues[i] = ParamCatalog.GetDefault(i);
                    }
                    RespondOk(request, null);
                    break;
                case HostCmd.DshotTest:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 3 || p[0] > 1)
                    {
                        RespondStatus(request, p.Length < 3 ? HostStatus.ErrLength : HostStatus.ErrParamId);
                        break;
                    }
                    int throttleUs = Bytes.GetU16(p, 1);
                    /* 0 = stop, otherwise 1000..2000 us (USB_PROTOCOL.md section 2) */
                    if ((throttleUs != 0) && (throttleUs < 1000 || throttleUs > 2000))
                    {
                        RespondStatus(request, HostStatus.ErrParamRange);
                        break;
                    }
                    _throttleUs[p[0]] = throttleUs == 0 ? 1000 : throttleUs;
                    _armed = throttleUs != 0;
                    RespondOk(request, null);
                    break;
                }
                case HostCmd.DshotGetTlm:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 1 || p[0] > 1)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    int esc = p[0];
                    uint erpm = ThrottleToErpm(_throttleUs[esc]);
                    byte[] body = new byte[26];
                    Bytes.PutU32(body, 0, erpm);
                    Bytes.PutU16(body, 4, erpm == 0 ? (ushort)0 : (ushort)Math.Min(65535U, 60000000U / erpm));
                    body[6] = (byte)(erpm > 0 ? 1 : 0);
                    body[7] = 4; /* DSHOT_STATE_RX */
                    Bytes.PutU16(body, 8, 0);
                    Bytes.PutU32(body, 10, _loopCounter);
                    Bytes.PutU32(body, 14, 0);
                    Bytes.PutU32(body, 18, 0);
                    Bytes.PutU32(body, 22, _ecatCycles);
                    RespondOk(request, body);
                    break;
                }
                case HostCmd.DshotCmd:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 3 || (p[0] > 1 && p[0] != 0xFF))
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    int command = Bytes.GetU16(p, 1);
                    if (command > 2047)
                    {
                        RespondStatus(request, HostStatus.ErrParamRange);
                        break;
                    }
                    RespondOk(request, null);
                    break;
                }
                case HostCmd.ServoScan:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 3 || p[0] > 1 || p[1] > p[2] || p[2] > 253)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    int bus = p[0];
                    int idMin = p[1];
                    int idMax = p[2];
                    /* SERVO_SCAN is asynchronous on the firmware: the first request
                     * starts the scan and answers ERR_BUSY, the host retries. */
                    if (_servoScanBusyReplies > 0)
                    {
                        _servoScanBusyReplies--;
                        RespondStatus(request, HostStatus.ErrBusy);
                        break;
                    }
                    byte[] bitmap = new byte[32];
                    int present = bus == 0 ? _servoPresent0 : _servoPresent1;
                    if (present >= idMin && present <= idMax && present <= 253)
                    {
                        bitmap[present / 8] |= (byte)(1 << (present % 8));
                    }
                    RespondOk(request, bitmap);
                    break;
                }
                case HostCmd.ServoMoveTest:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 4 || p[0] > 1)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    if (!CheckServoId(p[0], p[1]))
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    _servoTargetD10[p[0]] = Bytes.GetI16(p, 2);
                    RespondOk(request, null);
                    break;
                }
                case HostCmd.ServoRead:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 2 || p[0] > 1)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    if (!CheckServoId(p[0], p[1]))
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    byte[] body = new byte[20];
                    Bytes.PutU16(body, 0, (ushort)(short)_servoTargetD10[p[0]]);
                    Bytes.PutU16(body, 2, 0);
                    Bytes.PutU16(body, 4, (ushort)120);
                    Bytes.PutU16(body, 6, 48000);
                    body[8] = 32;
                    body[9] = 0;
                    body[10] = 1;
                    body[11] = 1;
                    Bytes.PutU16(body, 12, 1000);
                    Bytes.PutU16(body, 14, 0);
                    Bytes.PutU32(body, 16, _ecatCycles);
                    RespondOk(request, body);
                    break;
                }
                case HostCmd.ServoSetId:
                {
                    /* S288 has no register access over the bus protocol */
                    RespondStatus(request, HostStatus.ErrUnsupported);
                    break;
                }
                case HostCmd.ServoRaw:
                {
                    RespondStatus(request, HostStatus.ErrUnsupported);
                    break;
                }
                case HostCmd.AdcGet:
                {
                    byte[] body = new byte[24];
                    Bytes.PutU16(body, 0, 1234);
                    Bytes.PutU16(body, 2, 2400);
                    Bytes.PutU16(body, 4, (ushort)(800 + _throttleUs[0] - 1000));
                    Bytes.PutU16(body, 6, (ushort)(810 + _throttleUs[1] - 1000));
                    Bytes.PutU16(body, 8, 480);   /* 48.0 V */
                    Bytes.PutU16(body, 10, (ushort)(short)(1500 + (_throttleUs[0] - 1000) * 2));
                    Bytes.PutU16(body, 12, (ushort)(500 + (_throttleUs[0] - 1000)));
                    Bytes.PutU16(body, 14, (ushort)(520 + (_throttleUs[1] - 1000)));
                    body[16] = 0;                    /* alarm */
                    body[17] = 0;                    /* reserved0 */
                    Bytes.PutU16(body, 18, _loopCounter);
                    Bytes.PutU32(body, 20, _ecatCycles);
                    RespondOk(request, body);
                    break;
                }
                case HostCmd.LedSet:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 3)
                    {
                        RespondStatus(request, HostStatus.ErrLength);
                        break;
                    }
                    _ledRed = p[0];
                    _ledGreen = p[1];
                    _ledBlue = p[2];
                    RespondOk(request, null);
                    break;
                }
                case HostCmd.LedTest:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 3)
                    {
                        RespondStatus(request, HostStatus.ErrLength);
                        break;
                    }
                    _ledPattern = p[0];
                    RespondOk(request, null);
                    break;
                }
                case HostCmd.Am32Status:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 1 || p[0] > 1)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    FakeAm32Esc esc = _am32[p[0]];
                    byte[] body = new byte[2];
                    body[0] = (byte)(_am32SessionOpen[p[0]] ? 1 : 0);
                    body[1] = esc.LastError;
                    RespondOk(request, body);
                    break;
                }
                case HostCmd.Am32ProgWrite:
                {
                    /* DShot programming mode: one EEPROM byte per request */
                    byte[] p = request.Payload;
                    if (p.Length < 3)
                    {
                        RespondStatus(request, HostStatus.ErrLength);
                        break;
                    }
                    int esc = p[0];
                    int address = p[1];
                    int value = p[2];
                    if (esc > 1)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    if (address > 191)
                    {
                        RespondStatus(request, HostStatus.ErrParamRange);
                        break;
                    }
                    if (_am32SessionOpen[esc])
                    {
                        /* the 4-way bridge owns the signal line */
                        RespondStatus(request, HostStatus.ErrNotReady);
                        break;
                    }
                    if (_am32ProgBusyReplies > 0)
                    {
                        _am32ProgBusyReplies--;
                        RespondStatus(request, HostStatus.ErrBusy);
                        break;
                    }
                    _am32[esc].Eeprom[address] = (byte)value;
                    RespondOk(request, null);
                    break;
                }
                case HostCmd.Am32ProgSave:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 1 || p[0] > 1)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    int esc = p[0];
                    if (_am32SessionOpen[esc])
                    {
                        RespondStatus(request, HostStatus.ErrNotReady);
                        break;
                    }
                    /* the firmware answers BUSY while the last bytes are shifting out */
                    if (_am32ProgSaveBusyReplies > 0)
                    {
                        _am32ProgSaveBusyReplies--;
                        RespondStatus(request, HostStatus.ErrBusy);
                        break;
                    }
                    RespondOk(request, null);
                    break;
                }
                case HostCmd.Am32Begin:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 1 || p[0] > 1)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    int esc = p[0];
                    _throttleUs[esc] = 1000;
                    _armed = _throttleUs[0] > 1000 || _throttleUs[1] > 1000;
                    _am32SessionOpen[esc] = true;
                    _am32[esc].SessionOpen = true;
                    RespondOk(request, null);
                    break;
                }
                case HostCmd.Am32End:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 1 || p[0] > 1)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    _am32SessionOpen[p[0]] = false;
                    _am32[p[0]].SessionOpen = false;
                    RespondOk(request, null);
                    break;
                }
                case HostCmd.Am32Xfer:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 2 || p[0] > 1)
                    {
                        RespondStatus(request, HostStatus.ErrParamId);
                        break;
                    }
                    int esc = p[0];
                    int length = p[1];
                    if (length > 0 && p.Length < 2 + length)
                    {
                        RespondStatus(request, HostStatus.ErrLength);
                        break;
                    }
                    if (!_am32SessionOpen[esc])
                    {
                        RespondStatus(request, HostStatus.ErrNotReady);
                        break;
                    }
                    byte[] input = new byte[length];
                    Array.Copy(p, 2, input, 0, length);
                    if (Am32XferDelayMs > 0)
                    {
                        Thread.Sleep(Am32XferDelayMs);
                    }
                    byte[] output = _am32[esc].Handle(input);
                    byte[] body = new byte[1 + output.Length];
                    body[0] = (byte)output.Length;
                    Array.Copy(output, 0, body, 1, output.Length);
                    RespondOk(request, body);
                    break;
                }
                case HostCmd.Bootloader:
                {
                    byte[] p = request.Payload;
                    if (p.Length < 4 || Bytes.GetU32(p, 0) != HostProto.BootloaderMagic)
                    {
                        RespondStatus(request, HostStatus.ErrParamRange);
                        break;
                    }
                    RespondOk(request, null);
                    break;
                }
                case HostCmd.Reset:
                    RespondOk(request, null);
                    break;
                default:
                    RespondStatus(request, HostStatus.ErrUnknownCmd);
                    break;
            }
        }

        private byte[] BuildTelemetry()
        {
            _loopCounter++;
            uint erpm0 = ThrottleToErpm(_throttleUs[0]);
            uint erpm1 = ThrottleToErpm(_throttleUs[1]);
            byte[] body = new byte[54];
            ushort statusWord = 0;
            statusWord |= 0x0001;                       /* ECAT_STS_OP */
            if (_armed) statusWord |= 0x0002;           /* output enabled */
            if (erpm0 > 0) statusWord |= 0x0008;        /* ESC1 telemetry ok */
            if (erpm1 > 0) statusWord |= 0x0010;        /* ESC2 telemetry ok */
            statusWord |= 0x0020 | 0x0040;              /* both servos online */
            Bytes.PutU16(body, 0, statusWord);
            Bytes.PutU16(body, 2, 0);                   /* no faults */
            Bytes.PutU32(body, 4, erpm0);
            Bytes.PutU32(body, 8, erpm1);
            Bytes.PutU16(body, 12, erpm0 == 0 ? (ushort)0 : (ushort)Math.Min(65535U, 60000000U / (erpm0 == 0 ? 1 : erpm0)));
            Bytes.PutU16(body, 14, erpm1 == 0 ? (ushort)0 : (ushort)Math.Min(65535U, 60000000U / (erpm1 == 0 ? 1 : erpm1)));
            Bytes.PutU16(body, 16, (ushort)(short)_servoTargetD10[0]);
            Bytes.PutU16(body, 18, (ushort)(short)_servoTargetD10[1]);
            Bytes.PutU16(body, 20, 0);
            Bytes.PutU16(body, 22, 0);
            Bytes.PutU16(body, 24, (ushort)120);
            Bytes.PutU16(body, 26, (ushort)120);
            body[28] = 32;
            body[29] = 32;
            body[30] = 0;
            body[31] = 0;
            Bytes.PutU16(body, 32, 480);                /* 48.0 V */
            Bytes.PutU16(body, 34, (ushort)(short)(1500 + (_throttleUs[0] - 1000) * 2));
            Bytes.PutU16(body, 36, (ushort)(500 + (_throttleUs[0] - 1000)));
            Bytes.PutU16(body, 38, (ushort)(520 + (_throttleUs[1] - 1000)));
            Bytes.PutU16(body, 40, _loopCounter);
            Bytes.PutU16(body, 42, (ushort)((_ledRed << 8) | _ledGreen));
            Bytes.PutU16(body, 44, (ushort)(0x0003U | (8U << 4))); /* bit0/1 link, bit4..7 AL=8 (OP) */
            Bytes.PutU32(body, 46, (uint)_requestCount);
            Bytes.PutU32(body, 50, (uint)_parser.CrcErrors);
            return body;
        }

        private static uint ThrottleToErpm(int throttleUs)
        {
            if (throttleUs <= 1000)
            {
                return 0;
            }
            return (uint)((throttleUs - 1000) * 30);
        }

        private byte[] BuildDescriptor(int id)
        {
            ParamInfo info = ParamCatalog.GetInfo(id);
            ParamDesc desc = new ParamDesc();
            desc.Id = (ushort)id;
            desc.Type = info != null ? info.Type : ParamType.I32;
            desc.Persist = true;
            desc.ReadOnly = false;
            /* the firmware uses lower case parameter names (param.c) */
            string name = info != null ? info.Name.ToLowerInvariant() : "param_" + id;
            desc.Len = (byte)Math.Min(255, name.Length);
            desc.Min = ParamCatalog.GetMin(id);
            desc.Max = ParamCatalog.GetMax(id);
            desc.Default = ParamCatalog.GetDefault(id);
            desc.Value = _paramValues[id];
            desc.Name = name;
            desc.Description = info != null ? info.DescriptionZh : string.Empty;
            return desc.ToBytes();
        }

        /* ------------------------------ responses ------------------------------ */

        private void RespondOk(HostFrame request, byte[] body)
        {
            Respond(request, HostStatus.Ok, body);
        }

        private void RespondStatus(HostFrame request, HostStatus status)
        {
            Respond(request, status, null);
        }

        private void Respond(HostFrame request, HostStatus status, byte[] body)
        {
            if (DropNextResponses > 0)
            {
                DropNextResponses--;
                return;
            }
            int bodyLength = body == null ? 0 : body.Length;
            byte[] payload = new byte[1 + bodyLength];
            payload[0] = (byte)status;
            if (bodyLength > 0)
            {
                Array.Copy(body, 0, payload, 1, bodyLength);
            }
            byte[] frame = FrameCodec.Encode(request.Cmd, request.Seq, payload);
            if (CorruptNextResponses > 0)
            {
                CorruptNextResponses--;
                frame[frame.Length - 1] ^= 0xFF;
            }
            for (int i = 0; i < frame.Length; i++)
            {
                _rxQueue.Enqueue(frame[i]);
            }
        }

        /* --------------------------- fake AM32 ESC ----------------------------- */

        private sealed class FakeAm32Esc
        {
            public byte[] Eeprom = Am32EepromMap.CreateSimulatedDefaults();
            public bool SessionOpen;
            public byte LastError;
            private int _address;
            private byte[] _buffer = new byte[0];

            public void Reset()
            {
                Eeprom = Am32EepromMap.CreateSimulatedDefaults();
                SessionOpen = false;
                LastError = 0;
                _address = 0;
                _buffer = new byte[0];
            }

            /// <summary>Handle one 4-way exchange, return the raw bytes sent back.</summary>
            public byte[] Handle(byte[] input)
            {
                if (input == null || input.Length == 0)
                {
                    LastError = 0x02;
                    return new byte[0];
                }
                /* BLHeliSuite interface frame? */
                if (input[0] == FourWayCommand.LocalEscape)
                {
                    return HandleInterface(input);
                }
                /* board container frame? [ESC][CMD][LEN][PARAM][CRC] */
                byte esc;
                byte cmd;
                string error;
                byte[] param = FourWayCodec.ParseContainer(input, out esc, out cmd, out error);
                if (param != null)
                {
                    return HandleContainer(cmd, param);
                }
                /* raw boot-loader frame? (used by the raw terminal in raw mode) */
                return HandleRawBootloader(input);
            }

            private byte[] HandleContainer(byte cmd, byte[] param)
            {
                switch (cmd)
                {
                    case FourWayCommand.TestAlive:
                        return AckOnly(FourWayAck.Ok);
                    case FourWayCommand.ReadEeprom:
                    {
                        int start = param.Length > 0 ? param[0] : 0;
                        int count = param.Length > 1 ? param[1] : Am32EepromMap.Size;
                        byte[] data = Slice(start, count);
                        byte[] reply = new byte[data.Length + 3];
                        Array.Copy(data, 0, reply, 0, data.Length);
                        ushort crc = Crc16.Arc(data, 0, data.Length);
                        reply[data.Length] = (byte)(crc & 0xFF);
                        reply[data.Length + 1] = (byte)((crc >> 8) & 0xFF);
                        reply[data.Length + 2] = FourWayAck.Ok;
                        return reply;
                    }
                    case FourWayCommand.ProgEeprom:
                    {
                        if (param.Length < 2)
                        {
                            LastError = FourWayAck.InvalidParam;
                            return AckOnly(FourWayAck.InvalidParam);
                        }
                        int start = param[0];
                        int count = param[1];
                        if (param.Length < 2 + count || start + count > Am32EepromMap.Size)
                        {
                            LastError = FourWayAck.InvalidParam;
                            return AckOnly(FourWayAck.InvalidParam);
                        }
                        Array.Copy(param, 2, Eeprom, start, count);
                        LastError = 0;
                        return AckOnly(FourWayAck.Ok);
                    }
                    case FourWayCommand.SetAddress:
                        if (param.Length >= 2)
                        {
                            _address = (param[0] << 8) | param[1];
                        }
                        return AckOnly(FourWayAck.Ok);
                    case FourWayCommand.SetBuffer:
                        if (param.Length >= 2)
                        {
                            int length = (param[0] << 8) | param[1];
                            _buffer = new byte[length];
                        }
                        return AckOnly(FourWayAck.Ok);
                    case FourWayCommand.Run:
                        return AckOnly(FourWayAck.Ok);
                    case FourWayCommand.KeepAlive:
                        return AckOnly(FourWayAck.Ok);
                    default:
                        LastError = FourWayAck.InvalidCmd;
                        return AckOnly(FourWayAck.InvalidCmd);
                }
            }

            private byte[] HandleInterface(byte[] input)
            {
                if (input.Length < 7)
                {
                    LastError = FourWayAck.InvalidCrc;
                    return new byte[0];
                }
                int paramLen = input[4];
                if (input.Length < 7 + paramLen)
                {
                    LastError = FourWayAck.InvalidCrc;
                    return new byte[0];
                }
                ushort expectedCrc = (ushort)((input[5 + paramLen] << 8) | input[6 + paramLen]);
                ushort actualCrc = Crc16.Xmodem(input, 0, 5 + paramLen);
                if (expectedCrc != actualCrc)
                {
                    LastError = FourWayAck.InvalidCrc;
                    return BuildInterfaceReply(input[1], (input[2] << 8) | input[3], new byte[0], FourWayAck.InvalidCrc);
                }
                byte cmd = input[1];
                int address = (input[2] << 8) | input[3];
                byte[] param = new byte[paramLen];
                Array.Copy(input, 5, param, 0, paramLen);
                switch (cmd)
                {
                    case FourWayCommand.TestAlive:
                        return BuildInterfaceReply(cmd, address, new byte[0], FourWayAck.Ok);
                    case FourWayCommand.ProtocolGetVersion:
                        return BuildInterfaceReply(cmd, address, new byte[] { 108 }, FourWayAck.Ok);
                    case FourWayCommand.InterfaceGetVersion:
                        return BuildInterfaceReply(cmd, address, new byte[] { 20, 0 }, FourWayAck.Ok);
                    case FourWayCommand.DeviceReadEeprom:
                    {
                        int count = param.Length > 0 ? param[0] : 192;
                        if (count == 0)
                        {
                            count = 256;
                        }
                        byte[] data = Slice(address, count);
                        return BuildInterfaceReply(cmd, address, data, FourWayAck.Ok);
                    }
                    case FourWayCommand.DeviceWriteEeprom:
                    {
                        if (address + param.Length > Am32EepromMap.Size)
                        {
                            return BuildInterfaceReply(cmd, address, new byte[0], FourWayAck.InvalidParam);
                        }
                        Array.Copy(param, 0, Eeprom, address, param.Length);
                        LastError = 0;
                        return BuildInterfaceReply(cmd, address, new byte[0], FourWayAck.Ok);
                    }
                    case FourWayCommand.InterfaceExit:
                    case FourWayCommand.DeviceReset:
                        SessionOpen = false;
                        return BuildInterfaceReply(cmd, address, new byte[0], FourWayAck.Ok);
                    default:
                        LastError = FourWayAck.InvalidCmd;
                        return BuildInterfaceReply(cmd, address, new byte[0], FourWayAck.InvalidCmd);
                }
            }

            private byte[] HandleRawBootloader(byte[] input)
            {
                /* [CMD][LEN][PARAM..][CRC16-ARC Lo][Hi] */
                if (input.Length < 4)
                {
                    return new byte[0];
                }
                int paramLen = input[1];
                if (input.Length < paramLen + 4)
                {
                    return new byte[0];
                }
                ushort expected = (ushort)(input[2 + paramLen] | (input[3 + paramLen] << 8));
                ushort actual = Crc16.Arc(input, 0, 2 + paramLen);
                byte cmd = input[0];
                byte[] param = new byte[paramLen];
                Array.Copy(input, 2, param, 0, paramLen);
                if (expected != actual)
                {
                    LastError = FourWayAck.InvalidCrc;
                    return AckOnly(FourWayAck.InvalidCrc);
                }
                switch (cmd)
                {
                    case FourWayCommand.ReadEeprom:
                        if (param.Length < 1)
                        {
                            return AckOnly(FourWayAck.InvalidParam);
                        }
                        byte[] data = Slice(_address, param[0]);
                        byte[] reply = new byte[data.Length + 3];
                        Array.Copy(data, 0, reply, 0, data.Length);
                        ushort crc = Crc16.Arc(data, 0, data.Length);
                        reply[data.Length] = (byte)(crc & 0xFF);
                        reply[data.Length + 1] = (byte)((crc >> 8) & 0xFF);
                        reply[data.Length + 2] = FourWayAck.Ok;
                        return reply;
                    case FourWayCommand.ProgEeprom:
                        if (param.Length >= 1)
                        {
                            Array.Copy(_buffer, 0, Eeprom, _address, Math.Min(_buffer.Length, Am32EepromMap.Size - _address));
                            LastError = 0;
                            return AckOnly(FourWayAck.Ok);
                        }
                        return AckOnly(FourWayAck.InvalidParam);
                    case FourWayCommand.SetAddress:
                        if (param.Length >= 2)
                        {
                            _address = (param[0] << 8) | param[1];
                        }
                        return AckOnly(FourWayAck.Ok);
                    default:
                        LastError = FourWayAck.InvalidCmd;
                        return AckOnly(FourWayAck.InvalidCmd);
                }
            }

            private byte[] Slice(int start, int count)
            {
                if (start < 0)
                {
                    start = 0;
                }
                if (start >= Am32EepromMap.Size)
                {
                    return new byte[0];
                }
                if (count <= 0 || start + count > Am32EepromMap.Size)
                {
                    count = Am32EepromMap.Size - start;
                }
                byte[] data = new byte[count];
                Array.Copy(Eeprom, start, data, 0, count);
                return data;
            }

            private static byte[] AckOnly(byte ack)
            {
                return new byte[] { ack };
            }

            private static byte[] BuildInterfaceReply(byte cmd, int address, byte[] data, byte ack)
            {
                int dataLen = data == null ? 0 : data.Length;
                /* 0x2E + cmd + addrH + addrL + len + data + ack + crc16 = 8 + dataLen */
                byte[] reply = new byte[8 + dataLen];
                reply[0] = FourWayCommand.RemoteEscape;
                reply[1] = cmd;
                reply[2] = (byte)((address >> 8) & 0xFF);
                reply[3] = (byte)(address & 0xFF);
                reply[4] = (byte)dataLen;
                if (dataLen > 0)
                {
                    Array.Copy(data, 0, reply, 5, dataLen);
                }
                reply[5 + dataLen] = ack;
                ushort crc = Crc16.Xmodem(reply, 0, 6 + dataLen);
                reply[6 + dataLen] = (byte)((crc >> 8) & 0xFF);
                reply[7 + dataLen] = (byte)(crc & 0xFF);
                return reply;
            }
        }
    }
}
