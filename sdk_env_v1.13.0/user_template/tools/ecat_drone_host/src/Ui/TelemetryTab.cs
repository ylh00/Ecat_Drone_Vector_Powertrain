/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * TelemetryTab.cs - 遥测 tab: live 20 Hz telemetry dashboard.
 */

using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using EcatDroneHost.App;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.Ui
{
    public sealed class TelemetryTab : UserControl
    {
        private readonly MainForm _shell;

        private readonly BarMeter _esc1Bar = new BarMeter();
        private readonly BarMeter _esc2Bar = new BarMeter();
        private readonly KvPanel _esc1Info = new KvPanel();
        private readonly KvPanel _esc2Info = new KvPanel();
        private readonly KvPanel _systemInfo = new KvPanel();
        private readonly KvPanel _servo1Info = new KvPanel();
        private readonly KvPanel _servo2Info = new KvPanel();
        private readonly BarMeter _voltageBar = new BarMeter();
        private readonly BarMeter _currentBar = new BarMeter();
        private readonly KvPanel _powerInfo = new KvPanel();
        private readonly Label _faultLabel;
        private readonly Label _statusLabel;
        private readonly KvPanel _hostInfo = new KvPanel();
        private readonly RichTextBox _rawBox = UiKit.NewHexBox();
        private readonly Label _updateLabel;
        private readonly Timer _watchdog;
        private DateTime _lastUpdate = DateTime.MinValue;
        private bool _everReceived;
        private byte[] _lastRaw;

        public TelemetryTab(MainForm shell)
        {
            _shell = shell;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            Padding = new Padding(8);

            _voltageBar.Caption = "系统电压";
            _voltageBar.Unit = "V";
            _voltageBar.Maximum = 60;
            _voltageBar.BarColor = Theme.Accent;
            _currentBar.Caption = "系统电流";
            _currentBar.Unit = "A";
            _currentBar.Maximum = 60;
            _currentBar.BarColor = Theme.Warn;
            _esc1Bar.Caption = "ESC1 电转速";
            _esc1Bar.Unit = "eRPM";
            _esc1Bar.Maximum = 100000;
            _esc1Bar.BarColor = Color.FromArgb(0, 137, 123);
            _esc2Bar.Caption = "ESC2 电转速";
            _esc2Bar.Unit = "eRPM";
            _esc2Bar.Maximum = 100000;
            _esc2Bar.BarColor = Color.FromArgb(94, 53, 177);

            _esc1Info.Add("电转速 eRPM", "-");
            _esc1Info.Add("机械转速", "-");
            _esc1Info.Add("遥测周期", "-");
            _esc1Info.Add("ESC 电流", "-");
            _esc2Info.Add("电转速 eRPM", "-");
            _esc2Info.Add("机械转速", "-");
            _esc2Info.Add("遥测周期", "-");
            _esc2Info.Add("ESC 电流", "-");

            _systemInfo.Add("EtherCAT AL 状态", "-");
            _systemInfo.Add("端口0 / 端口1", "-");
            _systemInfo.Add("控制循环计数", "-");
            _systemInfo.Add("RGB 状态码", "-");
            _systemInfo.Add("故障 / 状态", "-");
            _systemInfo.Add("数据更新", "等待数据");

            _servo1Info.Add("位置", "-");
            _servo1Info.Add("速度", "-");
            _servo1Info.Add("电流", "-");
            _servo1Info.Add("温度", "-");
            _servo1Info.Add("错误码", "-");
            _servo2Info.Add("位置", "-");
            _servo2Info.Add("速度", "-");
            _servo2Info.Add("电流", "-");
            _servo2Info.Add("温度", "-");
            _servo2Info.Add("错误码", "-");

            _powerInfo.Add("电压", "-");
            _powerInfo.Add("电流", "-");
            _powerInfo.Add("ESC1 电流", "-");
            _powerInfo.Add("ESC2 电流", "-");

            _faultLabel = new Label();
            _faultLabel.Dock = DockStyle.Fill;
            _faultLabel.Font = Theme.Body;
            _faultLabel.ForeColor = Theme.Ok;
            _faultLabel.Text = "无故障";
            _faultLabel.TextAlign = ContentAlignment.TopLeft;
            _faultLabel.Padding = new Padding(6);

            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.Font = Theme.Body;
            _statusLabel.ForeColor = Theme.SubText;
            _statusLabel.Text = "-";
            _statusLabel.TextAlign = ContentAlignment.TopLeft;
            _statusLabel.Padding = new Padding(6);

            _hostInfo.Add("上位机接收帧", "-");
            _hostInfo.Add("设备 CRC 错误", "-");
            _hostInfo.Add("数据长度", "-");

            _updateLabel = UiKit.NewValueLabel("最近更新: -");
            _updateLabel.ForeColor = Theme.SubText;

            TableLayoutPanel root = UiKit.NewLayout(3);
            root.RowCount = 4;
            for (int i = 0; i < 3; i++)
            {
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            }
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            /* ---- row 0: ESC1 / ESC2 / system ---- */
            UiKit.Place(root, BuildEscGroup("ESC1（双向 DShot）", _esc1Bar, _esc1Info), 0, 0);
            UiKit.Place(root, BuildEscGroup("ESC2（双向 DShot）", _esc2Bar, _esc2Info), 1, 0);
            UiKit.Place(root, WrapGroup("系统状态", _systemInfo), 2, 0);

            /* ---- row 1: servers + power ---- */
            UiKit.Place(root, WrapGroup("总线舵机 1（S288）", _servo1Info), 0, 1);
            UiKit.Place(root, WrapGroup("总线舵机 2（S288）", _servo2Info), 1, 1);

            GroupBox powerGroup = UiKit.NewGroup("电源 / 电流");
            TableLayoutPanel powerLayout = UiKit.NewLayout(1);
            powerLayout.RowCount = 4;
            powerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            powerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            powerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            powerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            UiKit.Place(powerLayout, _voltageBar, 0, 0);
            UiKit.Place(powerLayout, _currentBar, 0, 1);
            UiKit.Place(powerLayout, _powerInfo, 0, 2);
            powerLayout.SetRowSpan(_powerInfo, 2);
            powerGroup.Controls.Add(powerLayout);
            UiKit.Place(root, powerGroup, 2, 1);

            /* ---- row 2: fault / status / host ---- */
            GroupBox faultGroup = UiKit.NewGroup("故障位（fault_code 解码）");
            Panel faultPanel = new Panel();
            faultPanel.Dock = DockStyle.Fill;
            faultPanel.Controls.Add(_faultLabel);
            faultGroup.Controls.Add(faultPanel);

            GroupBox statusGroup = UiKit.NewGroup("状态字（status_word 解码）");
            Panel statusPanel = new Panel();
            statusPanel.Dock = DockStyle.Fill;
            statusPanel.Controls.Add(_statusLabel);
            statusGroup.Controls.Add(statusPanel);

            GroupBox hostGroup = UiKit.NewGroup("上位机 / 数据质量");
            TableLayoutPanel hostLayout = UiKit.NewLayout(1);
            hostLayout.RowCount = 2;
            hostLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            hostLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            UiKit.Place(hostLayout, _hostInfo, 0, 0);
            UiKit.Place(hostLayout, _updateLabel, 0, 1);
            hostGroup.Controls.Add(hostLayout);

            UiKit.Place(root, faultGroup, 0, 2);
            UiKit.Place(root, statusGroup, 1, 2);
            UiKit.Place(root, hostGroup, 2, 2);

            /* ---- row 3: raw telemetry bytes ---- */
            GroupBox rawGroup = UiKit.NewGroup("原始遥测数据（54 字节，小端）");
            rawGroup.Controls.Add(_rawBox);
            UiKit.PlaceSpan(root, rawGroup, 0, 3, 3);

            Controls.Add(root);

            _watchdog = new Timer();
            _watchdog.Interval = 500;
            _watchdog.Tick += OnWatchdogTick;
            _watchdog.Start();

            _shell.TelemetryReceived += OnTelemetryReceived;
            _shell.ConnectionChanged += OnConnectionChanged;
        }

        private GroupBox BuildEscGroup(string title, BarMeter meter, KvPanel info)
        {
            GroupBox group = UiKit.NewGroup(title);
            TableLayoutPanel layout = UiKit.NewLayout(1);
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            UiKit.Place(layout, meter, 0, 0);
            UiKit.Place(layout, info, 0, 1);
            group.Controls.Add(layout);
            return group;
        }

        private GroupBox WrapGroup(string title, Control content)
        {
            GroupBox group = UiKit.NewGroup(title);
            group.Controls.Add(content);
            return group;
        }

        private void OnConnectionChanged(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                _lastUpdate = DateTime.MinValue;
                _everReceived = false;
                _lastRaw = null;
                _faultLabel.Text = "未连接";
                _faultLabel.ForeColor = Theme.SubText;
                _statusLabel.Text = "-";
                _updateLabel.Text = "最近更新: -";
                _rawBox.Clear();
                _esc1Bar.Value = 0;
                _esc2Bar.Value = 0;
                _voltageBar.Value = 0;
                _currentBar.Value = 0;
            }
        }

        private void OnTelemetryReceived(object sender, TelemetryEventArgs e)
        {
            TelemetryData t = e.Telemetry;
            if (t == null)
            {
                return;
            }
            _lastUpdate = DateTime.Now;
            _everReceived = true;
            _lastRaw = t.Raw;
            int polePairs = _shell.PolePairs;

            UpdateEsc(_esc1Bar, _esc1Info, t, 0, polePairs);
            UpdateEsc(_esc2Bar, _esc2Info, t, 1, polePairs);

            _systemInfo.Set("EtherCAT AL 状态", TelemetryData.DescribeEcatAlState(t.EcatAlState));
            _systemInfo.Set("端口0 / 端口1",
                (t.EcatPort0Link ? "Link" : "断开") + " / " + (t.EcatPort1Link ? "Link" : "断开"));
            _systemInfo.SetColor("端口0 / 端口1",
                t.EcatPort0Link && t.EcatPort1Link ? Theme.Ok : Theme.Warn);
            _systemInfo.Set("控制循环计数", t.LoopCounter.ToString());
            _systemInfo.Set("RGB 状态码", "0x" + t.LedStatus.ToString("X4"));
            _systemInfo.Set("故障 / 状态", "0x" + t.FaultCode.ToString("X4") + " / 0x" + t.StatusWord.ToString("X4"));
            _systemInfo.Set("数据更新", _lastUpdate.ToString("HH:mm:ss.fff"));
            _systemInfo.SetColor("EtherCAT AL 状态", t.EcatAlState == 8 ? Theme.Ok : Theme.Warn);

            UpdateServo(_servo1Info, t, 0);
            UpdateServo(_servo2Info, t, 1);

            _voltageBar.Value = t.SysVoltageVolt;
            _currentBar.Value = t.SysCurrentAmp;
            _powerInfo.Set("电压", t.SysVoltageVolt.ToString("F1") + " V");
            _powerInfo.Set("电流", t.SysCurrentAmp.ToString("F2") + " A");
            _powerInfo.Set("ESC1 电流", t.EscCurrentMa[0] + " mA");
            _powerInfo.Set("ESC2 电流", t.EscCurrentMa[1] + " mA");

            UpdateFaults(t);
            UpdateStatus(t);

            _hostInfo.Set("上位机接收帧", t.HostRxFrames.ToString());
            _hostInfo.Set("设备 CRC 错误", t.HostCrcErrors.ToString());
            _hostInfo.Set("数据长度", t.RawLength + " 字节" + (t.Complete ? "（完整）" : "（不完整！）"));
            UpdateLabelNow();

            if (t.Raw != null)
            {
                _rawBox.Text = Bytes.ToHexString(t.Raw);
            }
        }

        private void UpdateEsc(BarMeter bar, KvPanel info, TelemetryData t, int esc, int polePairs)
        {
            double erpm = t.GetErpm(esc);
            double mechanical = t.GetMechanicalRpm(esc, polePairs);
            bar.Maximum = Math.Max(20000, erpm * 1.2);
            bar.Value = erpm;
            bar.ValueText = erpm.ToString("F0") + " eRPM";
            info.Set("电转速 eRPM", erpm.ToString("F0"));
            info.Set("机械转速", mechanical.ToString("F0") + " rpm");
            info.Set("遥测周期", t.EscTlmPeriodUs[esc] == 0 ? "-" : t.EscTlmPeriodUs[esc] + " µs");
            info.Set("ESC 电流", t.EscCurrentMa[esc] + " mA");
        }

        private void UpdateServo(KvPanel info, TelemetryData t, int ch)
        {
            info.Set("位置", t.ServoPosDeg(ch).ToString("F1") + " °");
            info.Set("速度", t.ServoSpeedRpm[ch] + " rpm");
            info.Set("电流", t.ServoCurrentMa[ch] + " mA");
            info.Set("温度", t.ServoTempC[ch] + " °C");
            info.Set("错误码", t.ServoError[ch] == 0 ? "无" : "0x" + t.ServoError[ch].ToString("X2"));
            info.SetColor("错误码", t.ServoError[ch] == 0 ? Theme.Text : Theme.Error);
        }

        private void UpdateFaults(TelemetryData t)
        {
            if (!t.HasFault)
            {
                _faultLabel.ForeColor = Theme.Ok;
                _faultLabel.Text = "无故障";
                return;
            }
            _faultLabel.ForeColor = Theme.Error;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("检测到故障: 0x" + t.FaultCode.ToString("X4"));
            System.Collections.Generic.List<string> faults = t.GetFaultTexts();
            for (int i = 0; i < faults.Count; i++)
            {
                sb.AppendLine("· " + faults[i]);
            }
            _faultLabel.Text = sb.ToString();
        }

        private void UpdateStatus(TelemetryData t)
        {
            System.Collections.Generic.List<string> bits = t.GetStatusTexts();
            if (bits.Count == 0)
            {
                _statusLabel.Text = "无状态位置位";
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("状态字: 0x" + t.StatusWord.ToString("X4"));
            for (int i = 0; i < bits.Count; i++)
            {
                sb.AppendLine("· " + bits[i]);
            }
            _statusLabel.Text = sb.ToString();
        }

        private void UpdateLabelNow()
        {
            _updateLabel.Text = "最近更新: " + _lastUpdate.ToString("HH:mm:ss.fff");
        }

        private void OnWatchdogTick(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                return;
            }
            if (!_everReceived)
            {
                _updateLabel.Text = "最近更新: 等待第一个遥测包 ...";
                return;
            }
            double age = (DateTime.Now - _lastUpdate).TotalMilliseconds;
            if (age > 1500)
            {
                _updateLabel.Text = "最近更新: " + _lastUpdate.ToString("HH:mm:ss.fff") +
                                    "（已 " + (int)(age / 1000) + " 秒无数据！）";
                _updateLabel.ForeColor = Theme.Error;
            }
            else
            {
                _updateLabel.ForeColor = Theme.SubText;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _watchdog.Dispose();
                _shell.TelemetryReceived -= OnTelemetryReceived;
                _shell.ConnectionChanged -= OnConnectionChanged;
            }
            base.Dispose(disposing);
        }
    }
}
