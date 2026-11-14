/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * ConnectionTab.cs - 连接 tab: COM port enumeration, connect / disconnect,
 * device identification and link statistics.
 */

using System;
using System.Drawing;
using System.Windows.Forms;
using EcatDroneHost.App;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.Ui
{
    public sealed class ConnectionTab : UserControl
    {
        private readonly MainForm _shell;
        private readonly ComboBox _portCombo = UiKit.NewCombo();
        private readonly ComboBox _baudCombo = UiKit.NewCombo();
        private readonly Button _connectButton;
        private readonly CheckBox _simulateCheck;
        private readonly Label _stateLabel;
        private readonly KvPanel _deviceInfo = new KvPanel();
        private readonly KvPanel _statistics = new KvPanel();
        private readonly Timer _statsTimer;
        private readonly TextBox _logHint;

        public ConnectionTab(MainForm shell)
        {
            _shell = shell;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            Padding = new Padding(8);

            _connectButton = UiKit.NewPrimaryButton("连接设备", OnConnectClicked);
            _simulateCheck = UiKit.NewCheck("使用内置模拟设备（无硬件演示）", false);
            _stateLabel = UiKit.NewValueLabel("未连接");
            _stateLabel.ForeColor = Theme.SubText;
            _logHint = UiKit.NewTextBox(string.Empty, true);
            _logHint.Multiline = true;
            _logHint.Height = 90;
            _logHint.ScrollBars = ScrollBars.Vertical;
            _logHint.Font = Theme.Body;

            TableLayoutPanel root = UiKit.NewLayout(2);
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 236F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            /* ------------------------- top: connect panel ---------------------- */
            GroupBox connectGroup = UiKit.NewGroup("串口连接 / 设备枚举");
            TableLayoutPanel connectLayout = UiKit.NewLayout(4);
            connectLayout.RowCount = 3;
            connectLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            connectLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            connectLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            connectLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            connectLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            connectLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            connectLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            UiKit.Place(connectLayout, UiKit.NewKeyLabel("串口"), 0, 0);
            UiKit.Place(connectLayout, _portCombo, 1, 0);
            UiKit.Place(connectLayout, UiKit.NewButton("刷新串口", OnRefreshClicked), 2, 0);
            UiKit.Place(connectLayout, _simulateCheck, 3, 0);

            UiKit.Place(connectLayout, UiKit.NewKeyLabel("波特率"), 0, 1);
            _baudCombo.Items.AddRange(new object[] { "115200", "921600", "460800", "230400", "57600", "9600" });
            _baudCombo.SelectedIndex = 0;
            UiKit.Place(connectLayout, _baudCombo, 1, 1);
            UiKit.Place(connectLayout, _connectButton, 2, 1);
            UiKit.Place(connectLayout, _stateLabel, 3, 1);

            Label hint = UiKit.NewHint("USB CDC 虚拟串口不依赖波特率（保持 115200 即可）。插入板子后点击“刷新串口”。" +
                                       "未连接硬件时可勾选“模拟设备”体验全部界面与协议流程。");
            connectLayout.Controls.Add(hint, 0, 2);
            connectLayout.SetColumnSpan(hint, 4);
            connectGroup.Controls.Add(connectLayout);

            UiKit.PlaceSpan(root, connectGroup, 0, 0, 2);

            /* ------------------------- device information ---------------------- */
            GroupBox infoGroup = UiKit.NewGroup("设备信息");
            GroupBox statsGroup = UiKit.NewGroup("链路 / 协议统计");

            Panel infoPanel = new Panel();
            infoPanel.Dock = DockStyle.Fill;
            infoPanel.Padding = new Padding(4);
            _deviceInfo.Dock = DockStyle.Top;
            _deviceInfo.Height = 168;
            _deviceInfo.Add("连接状态", "未连接");
            _deviceInfo.Add("串口", "-");
            _deviceInfo.Add("固件名称", HostProto.FirmwareName);
            _deviceInfo.Add("固件版本", "-");
            _deviceInfo.Add("编译日期", "-");
            _deviceInfo.Add("PING", "未执行");
            _deviceInfo.Add("参数表", "未读取");
            infoPanel.Controls.Add(_deviceInfo);
            infoGroup.Controls.Add(infoPanel);

            Panel statsPanel = new Panel();
            statsPanel.Dock = DockStyle.Fill;
            statsPanel.Padding = new Padding(4);
            _statistics.Dock = DockStyle.Top;
            _statistics.Height = 168;
            _statistics.Add("请求帧", "0");
            _statistics.Add("超时次数", "0");
            _statistics.Add("重试次数", "0");
            _statistics.Add("状态错误", "0");
            _statistics.Add("解析 CRC 错误", "0");
            _statistics.Add("重同步次数", "0");
            _statistics.Add("已解析应答帧", "0");
            statsPanel.Controls.Add(_statistics);
            statsGroup.Controls.Add(statsPanel);

            UiKit.Place(root, infoGroup, 0, 1);
            UiKit.Place(root, statsGroup, 1, 1);

            /* --------------------------- device actions ------------------------ */
            GroupBox actionGroup = UiKit.NewGroup("设备操作（危险操作请谨慎）");
            TableLayoutPanel actions = UiKit.NewLayout(3);
            actions.RowCount = 4;
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            UiKit.Place(actions, UiKit.NewButton("PING 测试", OnPingClicked), 0, 0);
            UiKit.Place(actions, UiKit.NewButton("读取版本", OnVersionClicked), 1, 0);
            UiKit.Place(actions, UiKit.NewButton("重新读取参数", OnReloadParamsClicked), 2, 0);

            UiKit.Place(actions, UiKit.NewButton("保存参数到 Flash", OnSaveParamsClicked), 0, 1);
            UiKit.Place(actions, UiKit.NewButton("恢复参数默认值", OnResetParamsClicked), 1, 1);
            UiKit.Place(actions, UiKit.NewButton("固件复位", OnResetDeviceClicked), 2, 1);

            UiKit.Place(actions, UiKit.NewButton("进入 DFU 引导", OnBootloaderClicked), 0, 2);
            Label actionHint = UiKit.NewHint("复位 / 进入 DFU 会断开连接；“恢复参数默认值”只改 RAM，需再点“保存参数到 Flash”才能掉电保持。");
            actions.Controls.Add(actionHint, 1, 2);
            actions.SetColumnSpan(actionHint, 2);

            Label protection = UiKit.NewWarningBanner(
                "参数与固件操作会影响正在运行的 EtherCAT 从站；请在调试环境、电机断电或卸桨状态下操作。");
            actions.Controls.Add(protection, 0, 3);
            actions.SetColumnSpan(protection, 3);
            actionGroup.Controls.Add(actions);

            GroupBox tipsGroup = UiKit.NewGroup("连接说明");
            _logHint.Text = "1. 用 USB 线连接板子的 USB0（CDC-ACM），Windows 会枚举一个虚拟串口。\r\n" +
                            "2. 点击“刷新串口”，选择新出现的 COMx，点击“连接设备”。\r\n" +
                            "3. 连接成功后自动执行 PING 与 GET_VERSION，并读取参数表。\r\n" +
                            "4. 若设备无响应：检查是否被其他工具占用、USB 线是否为数据线、板子是否已上电。\r\n" +
                            "5. 模拟设备模式不需要硬件，用于演示与上位机自检。\r\n" +
                            "6. 也可以运行 ecat_drone_host.exe --selftest 进行无硬件协议自检。";
            Panel tipsPanel = new Panel();
            tipsPanel.Dock = DockStyle.Fill;
            tipsPanel.Padding = new Padding(4);
            _logHint.Dock = DockStyle.Fill;
            tipsPanel.Controls.Add(_logHint);
            tipsGroup.Controls.Add(tipsPanel);

            UiKit.Place(root, actionGroup, 0, 2);
            UiKit.Place(root, tipsGroup, 1, 2);

            Controls.Add(root);

            _statsTimer = new Timer();
            _statsTimer.Interval = 500;
            _statsTimer.Tick += OnStatsTick;
            _statsTimer.Start();

            _shell.ConnectionChanged += OnConnectionChanged;
            RefreshPorts();
            UpdateConnectionState();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _simulateCheck.Checked = _shell.SimulateMode;
        }

        private void OnRefreshClicked(object sender, EventArgs e)
        {
            RefreshPorts();
        }

        private void RefreshPorts()
        {
            string previous = _portCombo.Text;
            string[] ports = _shell.GetPortNames();
            _portCombo.Items.Clear();
            _portCombo.Items.AddRange(ports);
            if (ports.Length == 0)
            {
                _portCombo.Items.Add("(无可用串口)");
                _portCombo.SelectedIndex = 0;
                return;
            }
            int index = Array.IndexOf(ports, previous);
            _portCombo.SelectedIndex = index >= 0 ? index : 0;
            _shell.LogInfo("连接", "发现串口: " + string.Join(", ", ports));
        }

        private void OnConnectClicked(object sender, EventArgs e)
        {
            if (_shell.IsConnected)
            {
                _shell.DisconnectDevice();
                return;
            }
            bool simulate = _simulateCheck.Checked;
            string port = _portCombo.Text;
            if (!simulate && (port.Length == 0 || port.StartsWith("(", StringComparison.Ordinal)))
            {
                MessageBox.Show(this, "请先选择一个可用的串口，或勾选“使用内置模拟设备”。",
                    "未选择串口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string error;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (!_shell.ConnectDevice(port, simulate, out error))
                {
                    MessageBox.Show(this, "连接失败:\r\n" + error, "连接失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OnPingClicked(object sender, EventArgs e)
        {
            _shell.PingDevice();
        }

        private void OnVersionClicked(object sender, EventArgs e)
        {
            _shell.ReadVersion();
        }

        private void OnReloadParamsClicked(object sender, EventArgs e)
        {
            _shell.RequestParameterReload();
        }

        private void OnSaveParamsClicked(object sender, EventArgs e)
        {
            _shell.SaveParameters();
        }

        private void OnResetParamsClicked(object sender, EventArgs e)
        {
            _shell.ResetParameters();
        }

        private void OnResetDeviceClicked(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "确认复位固件？连接会中断。", "固件复位",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            _shell.ResetDevice();
        }

        private void OnBootloaderClicked(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "确认让固件进入 DFU 引导模式？连接会中断，需要重新上电或烧录。",
                    "进入 DFU", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            _shell.EnterBootloader();
        }

        private void OnConnectionChanged(object sender, EventArgs e)
        {
            UpdateConnectionState();
        }

        private void UpdateConnectionState()
        {
            bool connected = _shell.IsConnected;
            _connectButton.Text = connected ? "断开连接" : "连接设备";
            _stateLabel.Text = connected ? (_shell.SimulateMode ? "已连接（模拟设备）" : "已连接") : "未连接";
            _stateLabel.ForeColor = connected ? Theme.Ok : Theme.SubText;
            _deviceInfo.Set("连接状态", connected ? (_shell.SimulateMode ? "模拟设备" : "已连接") : "未连接");
            _deviceInfo.SetColor("连接状态", connected ? Theme.Ok : Theme.SubText);
            _deviceInfo.Set("串口", connected ? _shell.ActivePortText : "-");
            _deviceInfo.Set("固件版本", _shell.DeviceVersionText);
            _deviceInfo.Set("编译日期", _shell.DeviceBuildDateText);
            _deviceInfo.Set("PING", _shell.DevicePingText);
            _deviceInfo.Set("参数表", _shell.Parameters.Length > 0
                ? _shell.Parameters.Length + " 项"
                : "未读取");
            if (connected)
            {
                _simulateCheck.Enabled = false;
                _portCombo.Enabled = false;
                _baudCombo.Enabled = false;
            }
            else
            {
                _simulateCheck.Enabled = true;
                _portCombo.Enabled = true;
                _baudCombo.Enabled = true;
                RefreshPorts();
            }
        }

        private void OnStatsTick(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                return;
            }
            BoardClient client = _shell.Client;
            _statistics.Set("请求帧", client.RequestCount.ToString());
            _statistics.Set("超时次数", client.TimeoutCount.ToString());
            _statistics.Set("重试次数", client.RetryCount.ToString());
            _statistics.Set("状态错误", client.StatusErrorCount.ToString());
            _statistics.Set("解析 CRC 错误", client.ParserCrcErrors.ToString());
            _statistics.Set("重同步次数", client.ParserResyncs.ToString());
            _statistics.Set("已解析应答帧", client.ParserFramesOk.ToString());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _statsTimer.Dispose();
                _shell.ConnectionChanged -= OnConnectionChanged;
            }
            base.Dispose(disposing);
        }
    }
}
