/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * ServoTab.cs - 总线舵机 tab: bus scan, angle test, status read, id change.
 */

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using EcatDroneHost.App;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.Ui
{
    public sealed class ServoTab : UserControl
    {
        private readonly MainForm _shell;
        private readonly ComboBox _busCombo = UiKit.NewCombo();
        private readonly NumericUpDown _idMin = UiKit.NewNumeric(0, 253, 0);
        private readonly NumericUpDown _idMax = UiKit.NewNumeric(0, 253, 20);
        private readonly ListBox _scanList = new ListBox();
        private readonly Label _scanResult = UiKit.NewKeyLabel("尚未扫描");
        private readonly NumericUpDown _idNumeric = UiKit.NewNumeric(0, 253, 1);
        private readonly NumericUpDown _newIdNumeric = UiKit.NewNumeric(0, 253, 1);
        private readonly NumericUpDown _angleNumeric = new NumericUpDown();
        private readonly TrackBar _angleSlider = UiKit.NewSlider(-3600, 3600, 0);
        private readonly Label _angleLabel = UiKit.NewValueLabel("0.0 °");
        private readonly KvPanel _statusInfo = new KvPanel();
        private readonly KvPanel _servo1Info = new KvPanel();
        private readonly KvPanel _servo2Info = new KvPanel();
        private readonly Label _statusLabel;
        private readonly Button _scanButton;
        private readonly Button _sendAngleButton;
        private readonly Button _readButton;
        private readonly Button _applyIdButton;
        private bool _busy;

        public ServoTab(MainForm shell)
        {
            _shell = shell;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            Padding = new Padding(8);

            _statusLabel = UiKit.NewKeyLabel("就绪");
            _scanButton = UiKit.NewButton("扫描总线", OnScanClicked);
            _sendAngleButton = UiKit.NewPrimaryButton("发送角度", OnSendAngleClicked);
            _readButton = UiKit.NewButton("读取状态", OnReadStatusClicked);
            _applyIdButton = UiKit.NewButton("修改 ID", OnApplyIdClicked);

            _busCombo.Items.AddRange(new object[] { "总线 1（UART2 / 舵机1）", "总线 2（UART3 / 舵机2）" });
            _busCombo.SelectedIndex = 0;
            _scanList.IntegralHeight = false;
            _scanList.Dock = DockStyle.Fill;
            _scanList.Font = Theme.Body;
            _scanList.SelectedIndexChanged += OnScanSelectionChanged;

            _angleNumeric.DecimalPlaces = 1;
            _angleNumeric.Minimum = -360.0M;
            _angleNumeric.Maximum = 360.0M;
            _angleNumeric.Increment = 0.1M;
            _angleNumeric.Value = 0M;
            _angleNumeric.Width = 90;
            _angleNumeric.Font = Theme.Body;
            _angleNumeric.TextAlign = HorizontalAlignment.Right;
            _angleNumeric.ValueChanged += delegate
            {
                int value = (int)Math.Round(_angleNumeric.Value * 10M);
                if (_angleSlider.Value != value)
                {
                    _angleSlider.Value = Math.Min(Math.Max(value, _angleSlider.Minimum), _angleSlider.Maximum);
                }
                _angleLabel.Text = FixedAngle(value);
            };
            _angleSlider.ValueChanged += delegate
            {
                _angleLabel.Text = FixedAngle(_angleSlider.Value);
                decimal degrees = _angleSlider.Value / 10M;
                if (_angleNumeric.Value != degrees)
                {
                    _angleNumeric.Value = Math.Min(Math.Max(degrees, _angleNumeric.Minimum), _angleNumeric.Maximum);
                }
            };

            _statusInfo.Add("位置", "-");
            _statusInfo.Add("速度", "-");
            _statusInfo.Add("电流", "-");
            _statusInfo.Add("总线电压", "-");
            _statusInfo.Add("温度", "-");
            _statusInfo.Add("错误码", "-");
            _statusInfo.Add("在线 / 使能", "-");
            _statusInfo.Add("OK / ERR 计数", "-");
            _statusInfo.Add("最后成功", "-");

            _servo1Info.Add("位置", "-");
            _servo1Info.Add("速度", "-");
            _servo1Info.Add("电流", "-");
            _servo1Info.Add("温度", "-");
            _servo1Info.Add("错误", "-");
            _servo2Info.Add("位置", "-");
            _servo2Info.Add("速度", "-");
            _servo2Info.Add("电流", "-");
            _servo2Info.Add("温度", "-");
            _servo2Info.Add("错误", "-");

            TableLayoutPanel root = UiKit.NewLayout(2);
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label danger = UiKit.NewDangerBanner(
                "危险: 舵机将按指令转动！测试前确认机构无卡死、人员远离；EtherCAT 主站运行时请勿使用本页。");
            UiKit.PlaceSpan(root, danger, 0, 0, 2);

            /* ---------------------------- left column -------------------------- */
            TableLayoutPanel left = UiKit.NewLayout(1);
            left.RowCount = 3;
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 56F));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 44F));

            GroupBox scanGroup = UiKit.NewGroup("总线扫描");
            TableLayoutPanel scanLayout = UiKit.NewLayout(4);
            scanLayout.RowCount = 3;
            scanLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            scanLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
            scanLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            scanLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            scanLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            scanLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            scanLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            UiKit.Place(scanLayout, UiKit.NewKeyLabel("总线"), 0, 0);
            UiKit.Place(scanLayout, _busCombo, 1, 0);
            UiKit.Place(scanLayout, _scanButton, 2, 0);
            scanLayout.SetColumnSpan(_busCombo, 1);
            UiKit.Place(scanLayout, UiKit.NewKeyLabel("ID 范围"), 0, 1);
            TableLayoutPanel rangePanel = UiKit.NewLayout(3);
            rangePanel.RowCount = 1;
            rangePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
            rangePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24F));
            rangePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
            UiKit.Place(rangePanel, _idMin, 0, 0);
            UiKit.Place(rangePanel, UiKit.NewKeyLabel("~"), 1, 0);
            UiKit.Place(rangePanel, _idMax, 2, 0);
            UiKit.Place(scanLayout, rangePanel, 1, 1);
            scanLayout.SetColumnSpan(rangePanel, 2);
            UiKit.Place(scanLayout, _scanResult, 3, 1);
            Panel listHost = new Panel();
            listHost.Dock = DockStyle.Fill;
            listHost.Padding = new Padding(0, 2, 0, 0);
            listHost.Controls.Add(_scanList);
            scanLayout.Controls.Add(listHost, 0, 2);
            scanLayout.SetColumnSpan(listHost, 4);
            scanGroup.Controls.Add(scanLayout);
            UiKit.Place(left, scanGroup, 0, 0);

            GroupBox idGroup = UiKit.NewGroup("修改舵机 ID");
            TableLayoutPanel idLayout = UiKit.NewLayout(4);
            idLayout.RowCount = 2;
            idLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            idLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            idLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            idLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            idLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            idLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            UiKit.Place(idLayout, UiKit.NewKeyLabel("当前 ID"), 0, 0);
            UiKit.Place(idLayout, _idNumeric, 1, 0);
            UiKit.Place(idLayout, UiKit.NewKeyLabel("新 ID"), 2, 0);
            UiKit.Place(idLayout, _newIdNumeric, 3, 0);
            UiKit.Place(idLayout, _applyIdButton, 0, 1);
            Label idHint = UiKit.NewHint("固件当前对 SERVO_SET_ID 返回 ERR_UNSUPPORTED（S288 总线无寄存器访问），改 ID 请用厂家工具。");
            UiKit.Place(idLayout, idHint, 1, 1);
            idLayout.SetColumnSpan(idHint, 3);
            idGroup.Controls.Add(idLayout);
            UiKit.Place(left, idGroup, 0, 1);

            GroupBox hintGroup = UiKit.NewGroup("说明");
            TableLayoutPanel hintLayout = UiKit.NewLayout(1);
            hintLayout.RowCount = 1;
            hintLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label hints = UiKit.NewHint(
                "· 角度单位 0.1°（滑块范围 -360.0° ~ 360.0°）。\r\n" +
                "· SERVO_SCAN 为异步扫描：固件返回 ERR_BUSY 时上位机自动重试，最长约 10 秒。\r\n" +
                "· 舵机 ID 必须与参数 SERVO_ID1/ID2 一致，否则固件返回 ERR_PARAM_ID。\r\n" +
                "· 台架租约 1000 ms：SERVO_MOVE_TEST 会续租，超时后固件自动关闭舵机输出。\r\n" +
                "· 舵机反馈来自 20 Hz 遥测（右侧下方）。");
            UiKit.Place(hintLayout, hints, 0, 0);
            hintGroup.Controls.Add(hintLayout);
            UiKit.Place(left, hintGroup, 0, 2);
            UiKit.Place(root, left, 0, 1);

            /* ---------------------------- right column ------------------------- */
            TableLayoutPanel right = UiKit.NewLayout(1);
            right.RowCount = 2;
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 62F));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));

            GroupBox testGroup = UiKit.NewGroup("舵机测试 / 状态");
            TableLayoutPanel testLayout = UiKit.NewLayout(1);
            testLayout.RowCount = 5;
            testLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            testLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            testLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            testLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            testLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));

            TableLayoutPanel targetRow = UiKit.NewLayout(4);
            targetRow.RowCount = 1;
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            UiKit.Place(targetRow, UiKit.NewKeyLabel("目标角度"), 0, 0);
            UiKit.Place(targetRow, _angleNumeric, 1, 0);
            UiKit.Place(targetRow, UiKit.NewKeyLabel("度"), 2, 0);
            UiKit.Place(targetRow, new Panel(), 3, 0);
            UiKit.Place(testLayout, targetRow, 0, 0);

            TableLayoutPanel sliderRow = UiKit.NewLayout(2);
            sliderRow.RowCount = 1;
            sliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            sliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            UiKit.Place(sliderRow, _angleSlider, 0, 0);
            UiKit.Place(sliderRow, _angleLabel, 1, 0);
            UiKit.Place(testLayout, sliderRow, 0, 1);

            TableLayoutPanel actionRow = UiKit.NewLayout(4);
            actionRow.RowCount = 1;
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            UiKit.Place(actionRow, _sendAngleButton, 0, 0);
            UiKit.Place(actionRow, _readButton, 1, 0);
            UiKit.Place(actionRow, _statusLabel, 2, 0);
            actionRow.SetColumnSpan(_statusLabel, 2);
            UiKit.Place(testLayout, actionRow, 0, 2);
            UiKit.Place(testLayout, _statusInfo, 0, 3);
            Label testHint = UiKit.NewHint("“发送角度”使用 SERVO_MOVE_TEST；“读取状态”使用 SERVO_READ 读取一次完整状态。");
            UiKit.Place(testLayout, testHint, 0, 4);
            testGroup.Controls.Add(testLayout);
            UiKit.Place(right, testGroup, 0, 0);

            GroupBox tlmGroup = UiKit.NewGroup("20 Hz 遥测中的舵机反馈");
            TableLayoutPanel tlmLayout = UiKit.NewLayout(2);
            tlmLayout.RowCount = 2;
            tlmLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tlmLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tlmLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            tlmLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            UiKit.Place(tlmLayout, UiKit.NewSectionHeader("舵机 1（遥测）"), 0, 0);
            UiKit.Place(tlmLayout, UiKit.NewSectionHeader("舵机 2（遥测）"), 1, 0);
            UiKit.Place(tlmLayout, _servo1Info, 0, 1);
            UiKit.Place(tlmLayout, _servo2Info, 1, 1);
            tlmGroup.Controls.Add(tlmLayout);
            UiKit.Place(right, tlmGroup, 0, 1);
            UiKit.Place(root, right, 1, 1);

            Controls.Add(root);

            _shell.TelemetryReceived += OnTelemetryReceived;
            _shell.ConnectionChanged += OnConnectionChanged;
            UpdateEnabled();
        }

        private static string FixedAngle(int angleD10)
        {
            return (angleD10 / 10.0).ToString("F1") + " °";
        }

        private int Bus
        {
            get { return _busCombo.SelectedIndex < 0 ? 0 : _busCombo.SelectedIndex; }
        }

        /* ------------------------------- actions ------------------------------- */

        private void OnScanClicked(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                SetStatus("未连接设备", true);
                return;
            }
            if (_busy)
            {
                return;
            }
            int bus = Bus;
            int idMin = (int)_idMin.Value;
            int idMax = (int)_idMax.Value;
            SetBusy(true);
            _scanResult.Text = "扫描中 ...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                int[] ids = null;
                string error = null;
                try
                {
                    ids = _shell.Client.ServoScan(bus, idMin, idMax);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                RunOnUi(delegate
                {
                    SetBusy(false);
                    _scanList.Items.Clear();
                    if (error != null)
                    {
                        _scanResult.Text = "扫描失败: " + error;
                        _scanResult.ForeColor = Theme.Error;
                        _shell.LogError("舵机", "扫描失败: " + error);
                        return;
                    }
                    for (int i = 0; i < ids.Length; i++)
                    {
                        _scanList.Items.Add("ID " + ids[i]);
                    }
                    _scanResult.Text = ids.Length == 0
                        ? "未发现舵机（检查供电 / 波特率 / 接线）"
                        : "发现 " + ids.Length + " 个舵机: " + string.Join(", ", Array.ConvertAll(ids, Convert.ToString));
                    _scanResult.ForeColor = ids.Length == 0 ? Theme.Warn : Theme.Ok;
                    if (ids.Length > 0)
                    {
                        _scanList.SelectedIndex = 0;
                        _idNumeric.Value = ids[0];
                    }
                    _shell.LogInfo("舵机", string.Format("总线{0} 扫描完成: {1} 个 ({2})",
                        bus + 1, ids.Length, _scanResult.Text));
                });
            });
        }

        private void OnScanSelectionChanged(object sender, EventArgs e)
        {
            if (_scanList.SelectedIndex < 0)
            {
                return;
            }
            string text = Convert.ToString(_scanList.SelectedItem);
            int id;
            if (int.TryParse(text.Replace("ID", string.Empty).Trim(), out id))
            {
                _idNumeric.Value = Math.Min(Math.Max(id, 0), 253);
                _newIdNumeric.Value = _idNumeric.Value;
            }
        }

        private void OnSendAngleClicked(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                SetStatus("未连接设备", true);
                return;
            }
            int bus = Bus;
            int id = (int)_idNumeric.Value;
            int angleD10 = (int)Math.Round(_angleNumeric.Value * 10M);
            try
            {
                _shell.Client.ServoMoveTest(bus, id, angleD10);
                SetStatus(string.Format("总线{0} ID{1} 目标 {2}", bus + 1, id, FixedAngle(angleD10)), false);
                _shell.LogInfo("舵机", string.Format("总线{0} ID{1} 发送角度 {2}",
                    bus + 1, id, FixedAngle(angleD10)));
            }
            catch (Exception ex)
            {
                SetStatus("发送失败: " + ex.Message, true);
                _shell.LogError("舵机", "发送角度失败: " + ex.Message);
            }
        }

        private void OnReadStatusClicked(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                SetStatus("未连接设备", true);
                return;
            }
            int bus = Bus;
            int id = (int)_idNumeric.Value;
            SetStatus("读取中 ...", false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                ServoStatusData status = null;
                string error = null;
                try
                {
                    status = _shell.Client.ServoRead(bus, id);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                RunOnUi(delegate
                {
                    if (error != null)
                    {
                        SetStatus("读取失败: " + error, true);
                        _shell.LogError("舵机", "读取状态失败: " + error);
                        return;
                    }
                    _statusInfo.Set("位置", (status.PosD10 / 10.0).ToString("F1") + " °");
                    _statusInfo.Set("速度", status.SpeedRpm + " rpm");
                    _statusInfo.Set("电流", status.CurrentMa + " mA");
                    _statusInfo.Set("总线电压", status.VoltageMv + " mV");
                    _statusInfo.Set("温度", status.TempC + " °C");
                    _statusInfo.Set("错误码", status.ErrorText);
                    _statusInfo.SetColor("错误码", status.Error == 0 ? Theme.Text : Theme.Error);
                    _statusInfo.Set("在线 / 使能",
                        (status.Online != 0 ? "在线" : "离线") + " / " + (status.Enabled != 0 ? "使能" : "未使能"));
                    _statusInfo.Set("OK / ERR 计数", status.OkCount + " / " + status.ErrCount);
                    _statusInfo.Set("最后成功", status.LastOkTickMs + " ms");
                    SetStatus("读取成功", false);
                });
            });
        }

        private void OnApplyIdClicked(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                SetStatus("未连接设备", true);
                return;
            }
            int bus = Bus;
            int oldId = (int)_idNumeric.Value;
            int newId = (int)_newIdNumeric.Value;
            if (oldId == newId)
            {
                SetStatus("新旧 ID 相同", true);
                return;
            }
            if (MessageBox.Show(this, string.Format("确认把总线{0} 上 ID {1} 的舵机改为 ID {2}？",
                    bus + 1, oldId, newId), "修改舵机 ID",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            try
            {
                _shell.Client.ServoSetId(bus, oldId, newId);
                _idNumeric.Value = newId;
                SetStatus(string.Format("ID 已修改: {0} -> {1}", oldId, newId), false);
                _shell.LogInfo("舵机", string.Format("总线{0} ID {1} -> {2}", bus + 1, oldId, newId));
            }
            catch (HostStatusException ex)
            {
                if (ex.Status == HostStatus.ErrUnsupported)
                {
                    SetStatus("固件不支持修改 S288 ID (ERR_UNSUPPORTED)", true);
                    MessageBox.Show(this,
                        "当前固件对 SERVO_SET_ID 返回 ERR_UNSUPPORTED：\r\n" +
                        "S288 总线协议不提供寄存器读写，修改舵机 ID 请使用厂家配置工具。",
                        "固件不支持", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    SetStatus("修改 ID 失败: " + ex.Message, true);
                }
                _shell.LogError("舵机", "修改 ID 失败: " + ex.Message);
            }
            catch (Exception ex)
            {
                SetStatus("修改 ID 失败: " + ex.Message, true);
                _shell.LogError("舵机", "修改 ID 失败: " + ex.Message);
            }
        }

        /* ------------------------------- updates ------------------------------- */

        private void OnTelemetryReceived(object sender, TelemetryEventArgs e)
        {
            TelemetryData t = e.Telemetry;
            if (t == null)
            {
                return;
            }
            UpdateServoTelemetry(_servo1Info, t, 0);
            UpdateServoTelemetry(_servo2Info, t, 1);
        }

        private static void UpdateServoTelemetry(KvPanel info, TelemetryData t, int ch)
        {
            info.Set("位置", t.ServoPosDeg(ch).ToString("F1") + " °");
            info.Set("速度", t.ServoSpeedRpm[ch] + " rpm");
            info.Set("电流", t.ServoCurrentMa[ch] + " mA");
            info.Set("温度", t.ServoTempC[ch] + " °C");
            info.Set("错误", t.ServoError[ch] == 0 ? "无" : "0x" + t.ServoError[ch].ToString("X2"));
            info.SetColor("错误", t.ServoError[ch] == 0 ? Theme.Text : Theme.Error);
        }

        private void OnConnectionChanged(object sender, EventArgs e)
        {
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            bool connected = _shell.IsConnected;
            _scanButton.Enabled = connected;
            _sendAngleButton.Enabled = connected;
            _readButton.Enabled = connected;
            _applyIdButton.Enabled = connected;
            if (!connected)
            {
                _scanResult.Text = "未连接";
                _scanResult.ForeColor = Theme.SubText;
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UpdateEnabled();
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
                /* closing */
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _shell.TelemetryReceived -= OnTelemetryReceived;
                _shell.ConnectionChanged -= OnConnectionChanged;
            }
            base.Dispose(disposing);
        }
    }
}
