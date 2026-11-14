/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * MainForm.cs - main window: status strip, tab control and the application core
 * (connection management, 20 Hz telemetry poller, parameter cache, log wiring).
 */

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using EcatDroneHost.App;
using EcatDroneHost.Protocol;
using EcatDroneHost.Transport;

namespace EcatDroneHost.Ui
{
    public sealed class MainForm : Form, IMainShell
    {
        private const int DefaultTelemetryHz = 20;

        /* ---- application core ---- */
        private readonly LogBuffer _log = new LogBuffer();
        private readonly bool _startSimulated;
        private BoardClient _client;
        private Am32Bridge _bridge;
        private ParamDesc[] _params = new ParamDesc[0];
        private Thread _pollThread;
        private volatile bool _polling;
        private volatile int _telemetryHz = DefaultTelemetryHz;
        private volatile bool _closing;
        private DateTime _lastPollErrorLog = DateTime.MinValue;
        private TelemetryData _lastTelemetry;
        private int _telemetryUpdatesShown;

        private string _portText = "-";
        private string _versionText = "-";
        private string _buildDateText = "-";
        private string _pingText = "未执行";

        /* ---- UI ---- */
        private StatusLight _statusLight;
        private Label _summaryLabel;
        private Label _versionLabel;
        private Label _statusLabel;
        private CheckBox _autoTelemetryCheck;
        private NumericUpDown _telemetryHzNumeric;
        private TabControl _tabs;
        private TabPage _connectionPage;
        private TabPage _telemetryPage;
        private TabPage _paramPage;
        private TabPage _dshotPage;
        private TabPage _servoPage;
        private TabPage _am32Page;
        private TabPage _ecatPage;
        private TabPage _logPage;

        public MainForm(bool simulate)
        {
            _startSimulated = simulate;
            Theme.Apply(this);
            Text = "ecat_drone_powertrain 上位机 v1.0 — EtherCAT 从站调参工具";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1320, 880);
            MinimumSize = new Size(1180, 760);
            KeyPreview = true;

            BuildUi();

            _log.Info("程序", "ecat_drone_host 启动（协议 CRC16-CCITT-FALSE，超时 300 ms，重试 2 次）");
            if (_startSimulated)
            {
                _log.Info("程序", "以模拟设备模式启动（--simulate）");
            }
            Shown += OnFormShown;
            FormClosing += OnFormClosing;
        }

        /* ===================================================================== */
        /* UI construction                                                       */
        /* ===================================================================== */

        private void BuildUi()
        {
            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Top;
            topPanel.Height = 64;
            topPanel.BackColor = Color.White;
            topPanel.Padding = new Padding(12, 6, 12, 4);

            TableLayoutPanel topLayout = new TableLayoutPanel();
            topLayout.Dock = DockStyle.Fill;
            topLayout.ColumnCount = 3;
            topLayout.RowCount = 1;
            topLayout.BackColor = Color.Transparent;
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360F));
            topLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Panel titlePanel = new Panel();
            titlePanel.Dock = DockStyle.Fill;
            Label title = new Label();
            title.Text = "ecat_drone_powertrain 上位机";
            title.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            title.ForeColor = Theme.Accent;
            title.AutoSize = true;
            title.Location = new Point(0, 0);
            Label subtitle = new Label();
            subtitle.Text = "HPM5E3Y EtherCAT 从站 · 双 AM32 电调 (BDShot) · 双 S288 总线舵机 · 参数 / 遥测 / 电机 / 舵机 / AM32 调参";
            subtitle.Font = Theme.Small;
            subtitle.ForeColor = Theme.SubText;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(2, 30);
            titlePanel.Controls.Add(title);
            titlePanel.Controls.Add(subtitle);
            UiKit.Place(topLayout, titlePanel, 0, 0);

            Panel statusPanel = new Panel();
            statusPanel.Dock = DockStyle.Fill;
            _statusLight = new StatusLight();
            _statusLight.Dock = DockStyle.Top;
            _statusLight.Height = 26;
            _statusLight.Caption = "未连接";
            _summaryLabel = new Label();
            _summaryLabel.Text = "遥测: 无数据";
            _summaryLabel.Font = Theme.Body;
            _summaryLabel.ForeColor = Theme.SubText;
            _summaryLabel.Dock = DockStyle.Top;
            _summaryLabel.Height = 22;
            statusPanel.Controls.Add(_summaryLabel);
            statusPanel.Controls.Add(_statusLight);
            UiKit.Place(topLayout, statusPanel, 1, 0);

            Panel controlPanel = new Panel();
            controlPanel.Dock = DockStyle.Fill;
            _versionLabel = new Label();
            _versionLabel.Text = "设备版本: -";
            _versionLabel.Font = Theme.Body;
            _versionLabel.ForeColor = Theme.Text;
            _versionLabel.Dock = DockStyle.Top;
            _versionLabel.Height = 22;
            _versionLabel.TextAlign = ContentAlignment.MiddleRight;
            _autoTelemetryCheck = UiKit.NewCheck("自动遥测", true);
            _autoTelemetryCheck.CheckedChanged += OnAutoTelemetryChanged;
            _telemetryHzNumeric = UiKit.NewNumeric(1, 100, DefaultTelemetryHz);
            _telemetryHzNumeric.Width = 64;
            _telemetryHzNumeric.ValueChanged += OnTelemetryHzChanged;
            Panel telemetryRow = new Panel();
            telemetryRow.Dock = DockStyle.Top;
            telemetryRow.Height = 28;
            Label hzLabel = UiKit.NewKeyLabel("频率(Hz)");
            hzLabel.Dock = DockStyle.Right;
            hzLabel.Width = 68;
            hzLabel.TextAlign = ContentAlignment.MiddleRight;
            _telemetryHzNumeric.Dock = DockStyle.Right;
            _autoTelemetryCheck.Dock = DockStyle.Right;
            _autoTelemetryCheck.Width = 90;
            _autoTelemetryCheck.TextAlign = ContentAlignment.MiddleRight;
            telemetryRow.Controls.Add(hzLabel);
            telemetryRow.Controls.Add(_telemetryHzNumeric);
            telemetryRow.Controls.Add(_autoTelemetryCheck);
            controlPanel.Controls.Add(telemetryRow);
            controlPanel.Controls.Add(_versionLabel);
            UiKit.Place(topLayout, controlPanel, 2, 0);

            topPanel.Controls.Add(topLayout);

            Panel separator = new Panel();
            separator.Dock = DockStyle.Top;
            separator.Height = 1;
            separator.BackColor = Theme.Border;

            Panel bottomPanel = new Panel();
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = 26;
            bottomPanel.BackColor = Color.White;
            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.Text = "就绪";
            _statusLabel.Font = Theme.Body;
            _statusLabel.ForeColor = Theme.SubText;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Padding = new Padding(12, 0, 0, 0);
            Label protocolLabel = new Label();
            protocolLabel.Dock = DockStyle.Right;
            protocolLabel.Width = 430;
            protocolLabel.Text = "协议: A5 5A | CMD | SEQ | LEN | PAYLOAD | CRC16-CCITT-FALSE";
            protocolLabel.Font = Theme.Small;
            protocolLabel.ForeColor = Theme.SubText;
            protocolLabel.TextAlign = ContentAlignment.MiddleRight;
            protocolLabel.Padding = new Padding(0, 0, 12, 0);
            bottomPanel.Controls.Add(_statusLabel);
            bottomPanel.Controls.Add(protocolLabel);

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            _tabs.Padding = new Point(14, 5);

            Controls.Add(_tabs);
            Controls.Add(bottomPanel);
            Controls.Add(separator);
            Controls.Add(topPanel);

            AddPage("连接", new ConnectionTab(this));
            AddPage("遥测", new TelemetryTab(this));
            AddPage("参数", new ParamTab(this));
            AddPage("无刷电机(DSHOT)", new DshotTab(this));
            AddPage("总线舵机", new ServoTab(this));
            AddPage("AM32调参", new Am32Tab(this));
            AddPage("EtherCAT", new EcatTab(this));
            AddPage("日志", new LogTab(this));

            _connectionPage = _tabs.TabPages[0];
            _telemetryPage = _tabs.TabPages[1];
            _paramPage = _tabs.TabPages[2];
            _dshotPage = _tabs.TabPages[3];
            _servoPage = _tabs.TabPages[4];
            _am32Page = _tabs.TabPages[5];
            _ecatPage = _tabs.TabPages[6];
            _logPage = _tabs.TabPages[7];
        }

        private void AddPage(string title, Control content)
        {
            TabPage page = new TabPage(title);
            page.BackColor = Theme.Background;
            page.Padding = new Padding(0);
            content.Dock = DockStyle.Fill;
            page.Controls.Add(content);
            _tabs.TabPages.Add(page);
        }

        /* ===================================================================== */
        /* IMainShell                                                            */
        /* ===================================================================== */

        public BoardClient Client
        {
            get { return _client; }
        }

        public LogBuffer Log
        {
            get { return _log; }
        }

        public Am32Bridge Am32Bridge
        {
            get { return _bridge; }
        }

        public bool IsConnected
        {
            get { return _client != null && _client.IsConnected; }
        }

        public ParamDesc[] Parameters
        {
            get { return _params; }
        }

        public int PolePairs
        {
            get
            {
                ParamDesc desc = FindParameter((int)ParamId.DshotPolePairs);
                if (desc != null && desc.Value > 0)
                {
                    return desc.Value;
                }
                return 7;
            }
        }

        public ParamDesc FindParameter(int id)
        {
            ParamDesc[] list = _params;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].Id == (ushort)id)
                {
                    return list[i];
                }
            }
            return null;
        }

        public void LogInfo(string category, string text)
        {
            _log.Info(category, text);
        }

        public void LogWarning(string category, string text)
        {
            _log.Warning(category, text);
        }

        public void LogError(string category, string text)
        {
            _log.Error(category, text);
        }

        public void ShowStatus(string text, bool isError)
        {
            if (IsDisposed || _statusLabel == null)
            {
                return;
            }
            if (_statusLabel.InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)delegate { ShowStatus(text, isError); });
                }
                catch (Exception)
                {
                    /* closing */
                }
                return;
            }
            _statusLabel.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            _statusLabel.ForeColor = isError ? Theme.Error : Theme.SubText;
        }

        /* ===================================================================== */
        /* events raised to the tabs (always on the UI thread)                   */
        /* ===================================================================== */

        public event EventHandler ConnectionChanged;
        public event EventHandler ParametersChanged;
        public event EventHandler<TelemetryEventArgs> TelemetryReceived;
        public event EventHandler<EcatStatusEventArgs> EcatStatusReceived;

        private void RaiseConnectionChanged()
        {
            EventHandler handler = ConnectionChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RaiseParametersChanged()
        {
            EventHandler handler = ParametersChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RaiseTelemetry(TelemetryData telemetry)
        {
            EventHandler<TelemetryEventArgs> handler = TelemetryReceived;
            if (handler != null)
            {
                handler(this, new TelemetryEventArgs(telemetry));
            }
        }

        private void RaiseEcatStatus(EcatStatus status)
        {
            EventHandler<EcatStatusEventArgs> handler = EcatStatusReceived;
            if (handler != null)
            {
                handler(this, new EcatStatusEventArgs(status));
            }
        }

        /* ===================================================================== */
        /* connection management (used by ConnectionTab)                         */
        /* ===================================================================== */

        internal bool SimulateMode
        {
            get { return _bridge != null && _client != null && _client.TransportDescription.StartsWith("模拟", StringComparison.Ordinal); }
        }

        internal string ActivePortText
        {
            get { return _portText; }
        }

        internal string DeviceVersionText
        {
            get { return _versionText; }
        }

        internal string DeviceBuildDateText
        {
            get { return _buildDateText; }
        }

        internal string DevicePingText
        {
            get { return _pingText; }
        }

        internal string[] GetPortNames()
        {
            return SerialTransport.GetPortNames();
        }

        /* ---- helpers used by the automated UI runtime check (--uitest) ---- */

        internal TelemetryData LastTelemetry
        {
            get { return _lastTelemetry; }
        }

        internal int TelemetryUpdatesShown
        {
            get { return _telemetryUpdatesShown; }
        }

        internal int TabCount
        {
            get { return _tabs.TabPages.Count; }
        }

        internal string TabTitle(int index)
        {
            if (index < 0 || index >= _tabs.TabPages.Count)
            {
                return string.Empty;
            }
            return _tabs.TabPages[index].Text;
        }

        internal void SelectTab(int index)
        {
            if (index >= 0 && index < _tabs.TabPages.Count)
            {
                _tabs.SelectedIndex = index;
            }
        }

        internal bool ConnectDevice(string portName, bool simulate, out string error)
        {
            error = null;
            if (IsConnected)
            {
                error = "已经连接，请先断开";
                return false;
            }

            IByteTransport transport;
            if (simulate)
            {
                transport = new FakeFirmwareTransport();
            }
            else
            {
                if (string.IsNullOrEmpty(portName))
                {
                    error = "未选择串口";
                    return false;
                }
                transport = new SerialTransport(portName, 115200);
            }

            BoardClient client = null;
            try
            {
                client = new BoardClient(transport);
                client.Trace += OnProtocolTrace;
                client.Log += OnProtocolLog;
                client.TimeoutMs = HostProto.DefaultTimeoutMs;
                client.Retries = HostProto.DefaultRetries;
                client.Open();
                _client = client;
                _portText = client.TransportDescription;
                _versionText = "-";
                _buildDateText = "-";
                _pingText = "未执行";

                _bridge = new Am32Bridge(client);
                _bridge.Trace += OnAm32Trace;
                _bridge.Log += OnAm32Log;

                _pingText = client.Ping();
                _log.Info("连接", _pingText);
                try
                {
                    VersionInfo version = client.GetVersion();
                    _versionText = "v" + version.Major + "." + version.Minor + "." + version.Patch;
                    _buildDateText = version.BuildDate;
                    _log.Info("连接", "固件版本 " + version);
                }
                catch (Exception ex)
                {
                    _versionText = "读取失败";
                    _log.Warning("连接", "读取版本失败: " + ex.Message);
                }

                StartPoller();
                UpdateConnectionUi();
                RaiseConnectionChanged();
                RequestParameterReload();
                ShowStatus("已连接 " + _portText + (_startSimulated == simulate && simulate ? "（模拟设备）" : string.Empty), false);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                CleanupClient();
                UpdateConnectionUi();
                RaiseConnectionChanged();
                _log.Error("连接", "连接失败: " + ex.Message);
                return false;
            }
        }

        internal void DisconnectDevice()
        {
            StopPoller();
            if (_bridge != null)
            {
                try
                {
                    _bridge.Close();
                }
                catch (Exception)
                {
                    /* link may already be gone */
                }
                _bridge = null;
            }
            CleanupClient();
            _params = new ParamDesc[0];
            _lastTelemetry = null;
            _versionText = "-";
            _buildDateText = "-";
            _pingText = "未执行";
            _portText = "-";
            UpdateConnectionUi();
            RaiseConnectionChanged();
            RaiseParametersChanged();
            ShowStatus("已断开连接", false);
            _log.Info("连接", "已断开");
        }

        private void CleanupClient()
        {
            BoardClient client = _client;
            _client = null;
            if (client == null)
            {
                return;
            }
            try
            {
                client.Trace -= OnProtocolTrace;
                client.Log -= OnProtocolLog;
                client.Close();
            }
            catch (Exception)
            {
                /* ignore */
            }
            try
            {
                client.Dispose();
            }
            catch (Exception)
            {
                /* ignore */
            }
        }

        private void UpdateConnectionUi()
        {
            if (_statusLight == null)
            {
                return;
            }
            bool connected = IsConnected;
            if (connected)
            {
                _statusLight.State = LightState.Ok;
                _statusLight.Caption = "已连接: " + _portText;
                _versionLabel.Text = "设备版本: " + _versionText + "  编译: " + _buildDateText;
            }
            else
            {
                _statusLight.State = LightState.Off;
                _statusLight.Caption = "未连接";
                _versionLabel.Text = "设备版本: -";
                _summaryLabel.Text = "遥测: 无数据";
            }
        }

        internal void PingDevice()
        {
            if (!IsConnected)
            {
                ShowStatus("未连接设备", true);
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                _pingText = _client.Ping();
                _log.Info("连接", _pingText);
                ShowStatus(_pingText, false);
            }
            catch (Exception ex)
            {
                _pingText = "失败: " + ex.Message;
                ShowStatus("PING 失败: " + ex.Message, true);
                _log.Error("连接", "PING 失败: " + ex.Message);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            UpdateConnectionUi();
            RaiseConnectionChanged();
        }

        internal void ReadVersion()
        {
            if (!IsConnected)
            {
                ShowStatus("未连接设备", true);
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                VersionInfo version = _client.GetVersion();
                _versionText = "v" + version.Major + "." + version.Minor + "." + version.Patch;
                _buildDateText = version.BuildDate;
                _log.Info("连接", "固件版本 " + version);
                ShowStatus("固件版本 " + version, false);
            }
            catch (Exception ex)
            {
                ShowStatus("读取版本失败: " + ex.Message, true);
                _log.Error("连接", "读取版本失败: " + ex.Message);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            UpdateConnectionUi();
            RaiseConnectionChanged();
        }

        internal void SaveParameters()
        {
            if (!IsConnected)
            {
                ShowStatus("未连接设备", true);
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                _client.ParamSave();
                ShowStatus("参数已保存到 Flash", false);
                _log.Info("参数", "参数已保存到 Flash");
            }
            catch (Exception ex)
            {
                ShowStatus("保存参数失败: " + ex.Message, true);
                _log.Error("参数", "保存参数失败: " + ex.Message);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        internal void ResetParameters()
        {
            if (!IsConnected)
            {
                ShowStatus("未连接设备", true);
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                _client.ParamReset();
                ShowStatus("参数已恢复默认值（未写入 Flash）", false);
                _log.Info("参数", "参数已恢复默认值（RAM）");
            }
            catch (Exception ex)
            {
                ShowStatus("恢复默认失败: " + ex.Message, true);
                _log.Error("参数", "恢复默认失败: " + ex.Message);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            RequestParameterReload();
        }

        internal void ResetDevice()
        {
            if (!IsConnected)
            {
                ShowStatus("未连接设备", true);
                return;
            }
            string error;
            _client.Reset(out error);
            _log.Info("设备", "已发送固件复位命令");
            DisconnectDevice();
        }

        internal void EnterBootloader()
        {
            if (!IsConnected)
            {
                ShowStatus("未连接设备", true);
                return;
            }
            string error;
            bool ok = _client.EnterBootloader(out error);
            if (ok)
            {
                _log.Info("设备", "已发送进入 DFU 引导命令");
                DisconnectDevice();
            }
            else
            {
                _log.Error("设备", "进入 DFU 失败: " + error);
                ShowStatus("进入 DFU 失败: " + error, true);
            }
        }

        /* ===================================================================== */
        /* parameter cache                                                       */
        /* ===================================================================== */

        public void RequestParameterReload()
        {
            if (!IsConnected)
            {
                ShowStatus("未连接设备，无法读取参数", true);
                return;
            }
            BoardClient client = _client;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ParamDesc[] list = client.ListParams();
                    RunOnUi(delegate
                    {
                        if (_client != client)
                        {
                            return;
                        }
                        _params = list;
                        RaiseParametersChanged();
                        _log.Info("参数", "参数表读取完成: " + list.Length + " 项");
                        ShowStatus("参数表读取完成: " + list.Length + " 项", false);
                    });
                }
                catch (Exception ex)
                {
                    RunOnUi(delegate
                    {
                        _log.Error("参数", "读取参数表失败: " + ex.Message);
                        ShowStatus("读取参数表失败: " + ex.Message, true);
                    });
                }
            });
        }

        private int ParamCountLoaded
        {
            get { return _params.Length; }
        }

        /* ===================================================================== */
        /* telemetry poller                                                      */
        /* ===================================================================== */

        private void StartPoller()
        {
            StopPoller();
            _polling = true;
            _pollThread = new Thread(PollLoop);
            _pollThread.IsBackground = true;
            _pollThread.Name = "telemetry-poller";
            _pollThread.Start();
        }

        private void StopPoller()
        {
            _polling = false;
            Thread thread = _pollThread;
            _pollThread = null;
            if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
            {
                try
                {
                    thread.Join(800);
                }
                catch (Exception)
                {
                    /* ignore */
                }
            }
        }

        private void PollLoop()
        {
            int tick = 0;
            while (_polling)
            {
                BoardClient client = _client;
                if (client == null || !client.IsConnected)
                {
                    break;
                }
                int hz = _telemetryHz;
                if (hz < 1)
                {
                    hz = 1;
                }
                int period = Math.Max(20, 1000 / hz);
                DateTime started = DateTime.UtcNow;
                try
                {
                    TelemetryData telemetry = client.GetTelemetry();
                    PostTelemetry(telemetry);
                    int statusDivider = Math.Max(1, hz / 5);
                    if (tick % statusDivider == 0)
                    {
                        try
                        {
                            EcatStatus status = client.GetEcatStatus();
                            PostEcatStatus(status);
                        }
                        catch (Exception)
                        {
                            /* status is optional, do not spam the log */
                        }
                    }
                    tick++;
                }
                catch (Exception ex)
                {
                    LogPollError(ex);
                }
                int elapsed = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                int wait = period - elapsed;
                if (wait < 5)
                {
                    wait = 5;
                }
                if (!_polling)
                {
                    break;
                }
                try
                {
                    Thread.Sleep(wait);
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        private void LogPollError(Exception ex)
        {
            TimeSpan since = DateTime.Now - _lastPollErrorLog;
            if (since.TotalSeconds < 2.0)
            {
                return;
            }
            _lastPollErrorLog = DateTime.Now;
            RunOnUi(delegate
            {
                _log.Warning("遥测", "轮询失败: " + ex.Message);
            });
        }

        private void PostTelemetry(TelemetryData telemetry)
        {
            RunOnUi(delegate
            {
                if (_client == null)
                {
                    return;
                }
                _lastTelemetry = telemetry;
                _telemetryUpdatesShown++;
                UpdateSummary(telemetry);
                RaiseTelemetry(telemetry);
            });
        }

        private void PostEcatStatus(EcatStatus status)
        {
            RunOnUi(delegate
            {
                if (_client == null)
                {
                    return;
                }
                RaiseEcatStatus(status);
            });
        }

        private void UpdateSummary(TelemetryData telemetry)
        {
            if (_summaryLabel == null)
            {
                return;
            }
            string faults = telemetry.FaultCode == 0
                ? "无故障"
                : "故障 0x" + telemetry.FaultCode.ToString("X4");
            _summaryLabel.Text = string.Format("{0:F1} V  {1:F2} A  AL={2}  {3}/{4}  {5}",
                telemetry.SysVoltageVolt, telemetry.SysCurrentAmp,
                TelemetryData.DescribeEcatAlState(telemetry.EcatAlState),
                telemetry.EcatPort0Link ? "P0 Link" : "P0 Down",
                telemetry.EcatPort1Link ? "P1 Link" : "P1 Down",
                faults);
            _summaryLabel.ForeColor = telemetry.FaultCode == 0 ? Theme.SubText : Theme.Error;
        }

        /* ===================================================================== */
        /* log wiring                                                            */
        /* ===================================================================== */

        private void OnProtocolTrace(object sender, TraceEventArgs e)
        {
            string prefix;
            switch (e.Direction)
            {
                case TraceDirection.Tx: prefix = "TX >> "; break;
                default: prefix = "RX << "; break;
            }
            _log.AddTrace("协议", e.Direction, e.Data, prefix + e.Text);
        }

        private void OnProtocolLog(object sender, TextEventArgs e)
        {
            _log.Info("链路", e.Text);
        }

        private void OnAm32Trace(object sender, TraceEventArgs e)
        {
            _log.AddTrace("AM32", e.Direction, e.Data, (e.Direction == TraceDirection.Tx ? "TX >> " : "RX << ") + e.Text);
        }

        private void OnAm32Log(object sender, TextEventArgs e)
        {
            _log.Info("AM32", e.Text);
        }

        /* ===================================================================== */
        /* misc events                                                           */
        /* ===================================================================== */

        private void OnFormShown(object sender, EventArgs e)
        {
            if (_startSimulated && !IsConnected)
            {
                string error;
                if (ConnectDevice("模拟", true, out error))
                {
                    _log.Info("程序", "已自动连接模拟设备");
                }
                else
                {
                    _log.Error("程序", "自动连接模拟设备失败: " + error);
                }
            }
        }

        private void OnAutoTelemetryChanged(object sender, EventArgs e)
        {
            _telemetryHz = _autoTelemetryCheck.Checked ? (int)_telemetryHzNumeric.Value : 0;
            _telemetryHzNumeric.Enabled = _autoTelemetryCheck.Checked;
            _log.Info("遥测", _autoTelemetryCheck.Checked
                ? "自动遥测已开启: " + _telemetryHz + " Hz"
                : "自动遥测已暂停");
        }

        private void OnTelemetryHzChanged(object sender, EventArgs e)
        {
            if (_autoTelemetryCheck.Checked)
            {
                _telemetryHz = (int)_telemetryHzNumeric.Value;
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_closing)
            {
                return;
            }
            _closing = true;
            try
            {
                DisconnectDevice();
            }
            catch (Exception)
            {
                /* never block closing */
            }
        }

        private void RunOnUi(Action action)
        {
            if (IsDisposed || _closing || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke(action);
            }
            catch (Exception)
            {
                /* form closing */
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopPoller();
                CleanupClient();
            }
            base.Dispose(disposing);
        }
    }
}
