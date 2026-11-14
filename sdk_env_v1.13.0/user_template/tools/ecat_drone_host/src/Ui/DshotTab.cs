/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * DshotTab.cs - 无刷电机(DSHOT) tab: throttle test, eRPM readout, DShot commands.
 */

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using EcatDroneHost.App;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.Ui
{
    public sealed class DshotTab : UserControl
    {
        private readonly MainForm _shell;
        private readonly EscPanel[] _panels = new EscPanel[2];
        private readonly System.Windows.Forms.Timer _autoSendTimer;
        private readonly System.Windows.Forms.Timer _updateTimer;
        private readonly System.Windows.Forms.Timer _leaseTimer;
        private readonly CheckBox _autoSendCheck;
        private readonly CheckBox _bothEscCheck;
        private readonly CheckBox _keepLeaseCheck;
        private readonly NumericUpDown _repeatNumeric;
        private readonly Label _statusLabel;
        private readonly Button _stopAllButton;
        private int _pendingAutoSendEsc = -1;
        private bool _busy;

        private static readonly string[] CommandNames = new string[]
        {
            "蜂鸣 1", "蜂鸣 2", "蜂鸣 3", "蜂鸣 4", "蜂鸣 5",
            "保存设置 12", "正转 7", "反转 8", "3D 关闭 9", "3D 打开 10",
            "停止 0", "扩展遥测 13", "设置请求 11"
        };

        private static readonly int[] CommandValues = new int[]
        {
            1, 2, 3, 4, 5, 12, 7, 8, 9, 10, 0, 13, 11
        };

        public DshotTab(MainForm shell)
        {
            _shell = shell;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            Padding = new Padding(8);
            _autoSendCheck = UiKit.NewCheck("滑块变化时立即发送（实时跟随）", false);
            _bothEscCheck = UiKit.NewCheck("命令发送到两个 ESC（0xFF 广播）", false);
            _keepLeaseCheck = UiKit.NewCheck("保持台架租约（每 400ms 重发油门）", true);
            _repeatNumeric = UiKit.NewNumeric(1, 20, 6);
            _statusLabel = UiKit.NewKeyLabel("就绪");
            _stopAllButton = UiKit.NewButton("全部停止 (1000 µs)", OnStopAllClicked);

            TableLayoutPanel root = UiKit.NewLayout(2);
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));

            for (int esc = 0; esc < 2; esc++)
            {
                _panels[esc] = BuildEscPanel(esc);
                UiKit.Place(root, _panels[esc].Group, esc, 0);
            }

            GroupBox globalGroup = UiKit.NewGroup("全局操作 / 安全");
            TableLayoutPanel globalLayout = UiKit.NewLayout(7);
            globalLayout.RowCount = 3;
            globalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168F));
            globalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));
            globalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            globalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66F));
            globalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            globalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            globalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            globalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            globalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            globalLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            UiKit.Place(globalLayout, _stopAllButton, 0, 0);
            UiKit.Place(globalLayout, _autoSendCheck, 1, 0);
            UiKit.Place(globalLayout, UiKit.NewKeyLabel("命令重复次数"), 2, 0);
            UiKit.Place(globalLayout, _repeatNumeric, 3, 0);
            Label hint = UiKit.NewHint("AM32 要求设置类命令连续发送 ≥6 帧才生效（换向 / 保存 / 3D）。");
            UiKit.Place(globalLayout, hint, 4, 0);
            globalLayout.SetColumnSpan(hint, 2);
            UiKit.Place(globalLayout, _statusLabel, 6, 0);
            UiKit.Place(globalLayout, _bothEscCheck, 1, 1);
            UiKit.Place(globalLayout, _keepLeaseCheck, 4, 1);
            Label leaseHint = UiKit.NewHint("台架租约 1000 ms：测试命令会续租，超时后固件自动停止电机 / 舵机。");
            UiKit.Place(globalLayout, leaseHint, 5, 1);
            globalLayout.SetColumnSpan(leaseHint, 2);
            Label danger = UiKit.NewDangerBanner(
                "危险: 电机可能立即转动！测试前必须卸桨、固定电机，并确保人员远离。EtherCAT 主站运行时请勿使用本页。");
            globalLayout.Controls.Add(danger, 0, 2);
            globalLayout.SetColumnSpan(danger, 7);
            globalGroup.Controls.Add(globalLayout);
            UiKit.PlaceSpan(root, globalGroup, 0, 1, 2);

            Controls.Add(root);

            _autoSendTimer = new System.Windows.Forms.Timer();
            _autoSendTimer.Interval = 80;
            _autoSendTimer.Tick += OnAutoSendTick;

            _updateTimer = new System.Windows.Forms.Timer();
            _updateTimer.Interval = 1000;
            _updateTimer.Tick += OnUpdateTick;
            _updateTimer.Start();

            /* the firmware bench lease expires after PARAM_USB_PROTO_TIMEOUT_MS
             * (default 1000 ms) without a DSHOT_TEST / DSHOT_CMD refresh */
            _leaseTimer = new System.Windows.Forms.Timer();
            _leaseTimer.Interval = 400;
            _leaseTimer.Tick += OnLeaseTick;
            _leaseTimer.Start();

            _shell.TelemetryReceived += OnTelemetryReceived;
            _shell.ConnectionChanged += OnConnectionChanged;
        }

        private sealed class EscPanel
        {
            public GroupBox Group;
            public TrackBar Slider;
            public Label ThrottleLabel;
            public Button SendButton;
            public Button StopButton;
            public Button ArmButton;
            public Button ReadButton;
            public KvPanel Info;
            public int Esc;
            public int LastThrottleUs;
        }

        private EscPanel BuildEscPanel(int esc)
        {
            EscPanel panel = new EscPanel();
            panel.Esc = esc;
            panel.Group = UiKit.NewGroup("ESC" + (esc + 1) + " 测试");
            panel.Slider = UiKit.NewSlider(1000, 2000, 1000);
            panel.ThrottleLabel = UiKit.NewValueLabel("1000 µs（停止）");
            panel.SendButton = UiKit.NewButton("发送油门", delegate { OnSendThrottle(esc); });
            panel.StopButton = UiKit.NewButton("停止", delegate { SendThrottle(esc, 1000); });
            panel.ArmButton = UiKit.NewPrimaryButton("解锁 1150", delegate { SendThrottle(esc, 1150); });
            panel.ReadButton = UiKit.NewButton("读取遥测", delegate { OnReadTelemetry(esc); });
            panel.Info = new KvPanel();
            panel.Info.Add("电转速 eRPM", "0");
            panel.Info.Add("机械转速", "0 rpm");
            panel.Info.Add("遥测周期", "-");
            panel.Info.Add("遥测状态", "-");
            panel.Info.Add("OK / ERR / 超时", "-");
            panel.Info.Add("驱动状态", "-");

            int escIndex = esc;
            panel.Slider.ValueChanged += delegate
            {
                panel.ThrottleLabel.Text = panel.Slider.Value + " µs" +
                    (panel.Slider.Value <= 1000 ? "（停止）" : string.Empty);
                if (_autoSendCheck.Checked)
                {
                    _pendingAutoSendEsc = escIndex;
                    _autoSendTimer.Stop();
                    _autoSendTimer.Start();
                }
            };

            TableLayoutPanel layout = UiKit.NewLayout(1);
            layout.RowCount = 6;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 54F));

            Label caption = UiKit.NewKeyLabel("油门指令（1000 = 停止，2000 = 满油门）");
            UiKit.Place(layout, caption, 0, 0);

            TableLayoutPanel sliderRow = UiKit.NewLayout(3);
            sliderRow.RowCount = 1;
            sliderRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            sliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            sliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            sliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            UiKit.Place(sliderRow, UiKit.NewKeyLabel("油门"), 0, 0);
            UiKit.Place(sliderRow, panel.Slider, 1, 0);
            UiKit.Place(sliderRow, panel.ThrottleLabel, 2, 0);
            UiKit.Place(layout, sliderRow, 0, 1);

            TableLayoutPanel buttonRow = UiKit.NewLayout(5);
            buttonRow.RowCount = 1;
            buttonRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            UiKit.Place(buttonRow, panel.SendButton, 0, 0);
            UiKit.Place(buttonRow, panel.StopButton, 1, 0);
            UiKit.Place(buttonRow, panel.ArmButton, 2, 0);
            UiKit.Place(buttonRow, panel.ReadButton, 2, 0);
            UiKit.Place(layout, buttonRow, 0, 2);

            UiKit.Place(layout, UiKit.NewKeyLabel("实时状态（来自 20 Hz 遥测）"), 0, 3);
            UiKit.Place(layout, panel.Info, 0, 4);

            GroupBox commandGroup = UiKit.NewGroup("DShot 命令");
            FlowLayoutPanel commands = new FlowLayoutPanel();
            commands.Dock = DockStyle.Fill;
            commands.FlowDirection = FlowDirection.LeftToRight;
            commands.WrapContents = true;
            commands.AutoScroll = true;
            for (int i = 0; i < CommandNames.Length; i++)
            {
                int command = CommandValues[i];
                Button button = UiKit.NewButton(CommandNames[i], delegate
                {
                    OnSendCommand(escIndex, command);
                });
                button.Height = 26;
                commands.Controls.Add(button);
            }
            commandGroup.Controls.Add(commands);
            UiKit.Place(layout, commandGroup, 0, 5);

            panel.Group.Controls.Add(layout);
            return panel;
        }

        /* ------------------------------- actions ------------------------------- */

        private void OnSendThrottle(int esc)
        {
            SendThrottle(esc, _panels[esc].Slider.Value);
        }

        private void SendThrottle(int esc, int microseconds)
        {
            if (!_shell.IsConnected)
            {
                SetStatus("未连接设备", true);
                return;
            }
            try
            {
                _shell.Client.DshotTest(esc, microseconds);
                _panels[esc].LastThrottleUs = microseconds > 1000 ? microseconds : 0;
                SetStatus(string.Format("ESC{0} 油门 = {1} µs", esc + 1, microseconds), false);
                _shell.LogInfo("DShot", string.Format("ESC{0} 油门 {1} µs", esc + 1, microseconds));
            }
            catch (Exception ex)
            {
                SetStatus("发送失败: " + ex.Message, true);
                _shell.LogError("DShot", "油门设置失败: " + ex.Message);
            }
        }

        private void OnStopAllClicked(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                SetStatus("未连接设备", true);
                return;
            }
            for (int esc = 0; esc < 2; esc++)
            {
                _panels[esc].Slider.Value = 1000;
                SendThrottle(esc, 1000);
                _panels[esc].LastThrottleUs = 0;
            }
            _shell.LogInfo("DShot", "已发送全部停止命令");
        }

        private void OnSendCommand(int esc, int command)
        {
            if (!_shell.IsConnected)
            {
                SetStatus("未连接设备", true);
                return;
            }
            if (_busy)
            {
                SetStatus("上一条命令仍在发送", true);
                return;
            }
            int repeats = (int)_repeatNumeric.Value;
            int target = _bothEscCheck.Checked ? 0xFF : esc;
            SetBusy(true);
            string description = "CMD " + command + (target == 0xFF ? "（广播）" : string.Empty);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try
                {
                    for (int i = 0; i < repeats; i++)
                    {
                        _shell.Client.DshotCommand(target, command);
                        if (i + 1 < repeats)
                        {
                            Thread.Sleep(15);
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                RunOnUi(delegate
                {
                    SetBusy(false);
                    if (error != null)
                    {
                        SetStatus("命令失败: " + error, true);
                        _shell.LogError("DShot", "命令失败: " + error);
                    }
                    else
                    {
                        SetStatus(string.Format("ESC{0} 已发送 {1} x {2}", esc + 1, description, repeats), false);
                        _shell.LogInfo("DShot", string.Format("ESC{0} 发送 {1}，重复 {2} 次",
                            esc + 1, description, repeats));
                    }
                });
            });
        }

        private void OnReadTelemetry(int esc)
        {
            if (!_shell.IsConnected)
            {
                SetStatus("未连接设备", true);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                DshotTelemetryData telemetry = null;
                try
                {
                    telemetry = _shell.Client.DshotGetTelemetry(esc);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                RunOnUi(delegate
                {
                    if (error != null)
                    {
                        SetStatus("读取遥测失败: " + error, true);
                        return;
                    }
                    UpdatePanelTelemetry(esc, telemetry.Erpm, telemetry.PeriodUs);
                    _panels[esc].Info.Set("遥测状态", telemetry.Valid ? "有效" : "无效");
                    _panels[esc].Info.Set("OK / ERR / 超时",
                        telemetry.OkCount + " / " + telemetry.ErrCount + " / " + telemetry.TimeoutCount);
                    _panels[esc].Info.Set("驱动状态", telemetry.StateName);
                    SetStatus("ESC" + (esc + 1) + " 遥测: " + telemetry.ToString(), false);
                });
            });
        }

        /* ------------------------------- updates ------------------------------- */

        private void OnTelemetryReceived(object sender, TelemetryEventArgs e)
        {
            TelemetryData telemetry = e.Telemetry;
            if (telemetry == null)
            {
                return;
            }
            for (int esc = 0; esc < 2; esc++)
            {
                UpdatePanelTelemetry(esc, telemetry.EscErpm[esc], telemetry.EscTlmPeriodUs[esc]);
                string state = telemetry.StatusBit(esc == 0 ? 3 : 4) ? "遥测正常" : "无遥测";
                _panels[esc].Info.Set("遥测状态", state);
                _panels[esc].Info.SetColor("遥测状态", telemetry.StatusBit(esc == 0 ? 3 : 4) ? Theme.Ok : Theme.Warn);
            }
        }

        private void UpdatePanelTelemetry(int esc, uint erpm, ushort periodUs)
        {
            /* USB_PROTOCOL.md: mechanical rpm = electrical rpm / pole pairs */
            double mechanical = erpm / Math.Max(1, _shell.PolePairs);
            _panels[esc].Info.Set("电转速 eRPM", erpm.ToString("F0"));
            _panels[esc].Info.Set("机械转速", mechanical.ToString("F0") + " rpm");
            _panels[esc].Info.Set("遥测周期", periodUs == 0 ? "-" : periodUs + " µs");
        }

        private void OnAutoSendTick(object sender, EventArgs e)
        {
            _autoSendTimer.Stop();
            if (_pendingAutoSendEsc < 0 || !_shell.IsConnected)
            {
                _pendingAutoSendEsc = -1;
                return;
            }
            int esc = _pendingAutoSendEsc;
            _pendingAutoSendEsc = -1;
            SendThrottle(esc, _panels[esc].Slider.Value);
        }

        /// <summary>Re-send the armed throttle to keep the firmware bench lease alive.</summary>
        private void OnLeaseTick(object sender, EventArgs e)
        {
            if (!_shell.IsConnected || !_keepLeaseCheck.Checked || _busy)
            {
                return;
            }
            for (int esc = 0; esc < 2; esc++)
            {
                int throttle = _panels[esc].LastThrottleUs;
                if (throttle > 1000)
                {
                    try
                    {
                        _shell.Client.DshotTest(esc, throttle);
                    }
                    catch (Exception)
                    {
                        /* lease renewal failures are not logged to avoid spam */
                    }
                }
            }
        }

        private void OnUpdateTick(object sender, EventArgs e)
        {
            bool connected = _shell.IsConnected;
            for (int esc = 0; esc < 2; esc++)
            {
                _panels[esc].SendButton.Enabled = connected;
                _panels[esc].StopButton.Enabled = connected;
                _panels[esc].ArmButton.Enabled = connected;
                _panels[esc].ReadButton.Enabled = connected;
            }
            _stopAllButton.Enabled = connected;
        }

        private void OnConnectionChanged(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                SetStatus("未连接", false);
                for (int esc = 0; esc < 2; esc++)
                {
                    _panels[esc].Info.Set("电转速 eRPM", "-");
                    _panels[esc].Info.Set("机械转速", "-");
                    _panels[esc].Info.Set("遥测状态", "-");
                }
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            for (int esc = 0; esc < 2; esc++)
            {
                _panels[esc].Group.Enabled = !busy;
            }
            _stopAllButton.Enabled = !busy && _shell.IsConnected;
        }

        private void SetStatus(string text, bool error)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = error ? Theme.Error : Theme.SubText;
        }

        private void RunOnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke(action);
            }
            catch (Exception)
            {
                /* form is closing */
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _autoSendTimer.Dispose();
                _updateTimer.Dispose();
                _leaseTimer.Dispose();
                _shell.TelemetryReceived -= OnTelemetryReceived;
                _shell.ConnectionChanged -= OnConnectionChanged;
            }
            base.Dispose(disposing);
        }
    }
}
