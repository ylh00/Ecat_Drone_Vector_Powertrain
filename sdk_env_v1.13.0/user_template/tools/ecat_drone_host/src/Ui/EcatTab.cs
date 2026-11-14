/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * EcatTab.cs - EtherCAT tab: slave state, PDO layout, ECAT parameter editing.
 *
 * The PC host tool cannot read the process image directly (that is the
 * EtherCAT master's job); this page therefore shows
 *   - the live slave status obtained through GET_ECAT_STATUS / telemetry,
 *   - the documented RxPDO / TxPDO layout (inc/ecat_pdo.h),
 *   - a TxPDO preview recomposed from the 54 byte telemetry blob,
 *   - the EtherCAT related parameters (alias / DC / sync / watchdog / CoE).
 */

using System;
using System.Drawing;
using System.Windows.Forms;
using EcatDroneHost.App;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.Ui
{
    public sealed class EcatTab : UserControl
    {
        private readonly MainForm _shell;
        private readonly KvPanel _slaveInfo = new KvPanel();
        private readonly KvPanel _identityInfo = new KvPanel();
        private readonly DataGridView _rxGrid = UiKit.NewGrid();
        private readonly DataGridView _txGrid = UiKit.NewGrid();
        private readonly RichTextBox _txHex = UiKit.NewHexBox();
        private readonly Label _refreshTime = UiKit.NewKeyLabel("等待数据 ...");
        private readonly Label _paramsStatus = UiKit.NewKeyLabel("参数表未读取");

        private readonly NumericUpDown _alias = UiKit.NewNumeric(0, 65535, 0);
        private readonly ComboBox _dcMode = UiKit.NewCombo(new string[]
            {
                "0 = 关闭分布式时钟", "1 = SYNC0 同步", "2 = SYNC1 同步", "3 = DC SYNC0"
            }, 0);
        private readonly NumericUpDown _sync0 = UiKit.NewNumeric(125, 10000000, 1000);
        private readonly NumericUpDown _sync1 = UiKit.NewNumeric(125, 10000000, 1000);
        private readonly NumericUpDown _dcShift = UiKit.NewNumeric(-1000000, 1000000, 0);
        private readonly NumericUpDown _watchdog = UiKit.NewNumeric(10, 1000, 100);
        private readonly NumericUpDown _sdoTimeout = UiKit.NewNumeric(100, 60000, 1000);
        private readonly CheckBox _coeEnable = UiKit.NewCheck("使能 CoE (SDO/PDO)", true);
        private readonly CheckBox _ledInvert = UiKit.NewCheck("状态灯电平取反", false);
        private readonly Button _writeParamsButton;
        private readonly Button _readParamsButton;
        private readonly Button _saveButton;
        private bool _updating;
        private byte[] _lastTxPdo;

        public EcatTab(MainForm shell)
        {
            _shell = shell;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            Padding = new Padding(8);

            _writeParamsButton = UiKit.NewPrimaryButton("写入 ECAT 参数", OnWriteParamsClicked);
            _readParamsButton = UiKit.NewButton("从设备读取", OnReadParamsClicked);
            _saveButton = UiKit.NewButton("保存到 Flash", OnSaveClicked);

            _slaveInfo.Add("AL 状态", "-");
            _slaveInfo.Add("端口0 Link", "-");
            _slaveInfo.Add("端口1 Link", "-");
            _slaveInfo.Add("故障码", "-");
            _slaveInfo.Add("控制循环计数", "-");
            _slaveInfo.Add("ECAT 周期丢失计数", "-");
            _slaveInfo.Add("状态字 / 故障", "-");
            _slaveInfo.Add("最近更新", "-");
            _slaveInfo.Add("刷新时间戳", "-");

            _identityInfo.Add("Vendor ID", "0x00000E5A（HPMicro 测试 ID）");
            _identityInfo.Add("Product Code", "0x0000E3A1");
            _identityInfo.Add("Revision No", "0x00010001");
            _identityInfo.Add("Serial No", "0x00000001");
            _identityInfo.Add("RxPDO (0x1600)", "22 字节：控制字 / 油门 / 舵机角度 / 周期计数");
            _identityInfo.Add("TxPDO (0x1A00)", "48 字节：状态 / eRPM / 舵机 / ADC");
            _identityInfo.Add("EEPROM", "片内 Flash 模拟 @ 0x80000");
            _identityInfo.Add("主站配置", "TwinCAT / ESI 文件在从站侧配置");

            TableLayoutPanel root = UiKit.NewLayout(2);
            root.RowCount = 4;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 200F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));

            Label banner = UiKit.NewHint(
                "说明: 过程数据 (PDO) 只能由 EtherCAT 主站读写，上位机通过 USB 只看从站状态。" +
                "下方 TxPDO 数值由 20 Hz 遥测按同一布局重组，用于核对；RxPDO 为布局说明。");
            UiKit.PlaceSpan(root, banner, 0, 0, 2);

            GroupBox slaveGroup = UiKit.NewGroup("从站状态");
            Panel slavePanel = new Panel();
            slavePanel.Dock = DockStyle.Fill;
            _slaveInfo.Dock = DockStyle.Fill;
            slavePanel.Controls.Add(_slaveInfo);
            slaveGroup.Controls.Add(slavePanel);
            UiKit.Place(root, slaveGroup, 0, 1);

            GroupBox identityGroup = UiKit.NewGroup("从站身份 / PDO 信息");
            Panel identityPanel = new Panel();
            identityPanel.Dock = DockStyle.Fill;
            _identityInfo.Dock = DockStyle.Fill;
            identityPanel.Controls.Add(_identityInfo);
            identityGroup.Controls.Add(identityPanel);
            UiKit.Place(root, identityGroup, 1, 1);

            /* --------------------------- RxPDO layout -------------------------- */
            GroupBox rxGroup = UiKit.NewGroup("RxPDO 0x1600（主站 → 从站，22 字节）");
            _rxGrid.Columns.Add(Column("字段", 150));
            _rxGrid.Columns.Add(Column("偏移", 52));
            _rxGrid.Columns.Add(Column("类型", 74));
            _rxGrid.Columns.Add(Column("说明", 240));
            _rxGrid.Dock = DockStyle.Fill;
            rxGroup.Controls.Add(_rxGrid);
            AddRxRow("control_word", "0", "u16", "bit0 运行, bit1 清故障, bit2 急停, bit3 舵机使能, bit4 3D");
            AddRxRow("esc_cmd_us[2]", "2", "u16[2]", "ESC1/ESC2 油门 1000..2000 µs");
            AddRxRow("servo_angle_d10[2]", "6", "i16[2]", "舵机1/2 目标角度 0.1°");
            AddRxRow("servo_ctrl_word", "10", "u16", "bit0/1 舵机使能, bit2 清舵机故障");
            AddRxRow("dshot_cmd[2]", "12", "u16[2]", "原始 DShot 命令（0=无）");
            AddRxRow("reserved0", "14", "u16", "保留");
            AddRxRow("cycle_counter", "16", "u32", "主站写计数（链路调试）");
            UiKit.Place(root, rxGroup, 0, 2);

            /* --------------------------- TxPDO values -------------------------- */
            GroupBox txGroup = UiKit.NewGroup("TxPDO 0x1A00（从站 → 主站，48 字节，由遥测重组）");
            _txGrid.Columns.Add(Column("字段", 140));
            _txGrid.Columns.Add(Column("偏移", 50));
            _txGrid.Columns.Add(Column("当前值", 210));
            _txGrid.Columns.Add(Column("说明", 200));
            _txGrid.Dock = DockStyle.Fill;
            txGroup.Controls.Add(_txGrid);
            AddTxRow("status_word", "0", "位域");
            AddTxRow("fault_code", "2", "故障位");
            AddTxRow("esc_erpm[0]", "4", "ESC1 电转速");
            AddTxRow("esc_erpm[1]", "8", "ESC2 电转速");
            AddTxRow("esc_tlm_period_us[0]", "12", "ESC1 遥测周期");
            AddTxRow("esc_tlm_period_us[1]", "14", "ESC2 遥测周期");
            AddTxRow("servo_pos_d10[0]", "16", "舵机1 角度");
            AddTxRow("servo_pos_d10[1]", "18", "舵机2 角度");
            AddTxRow("servo_speed_rpm[0]", "20", "舵机1 速度");
            AddTxRow("servo_speed_rpm[1]", "22", "舵机2 速度");
            AddTxRow("servo_current_ma[0]", "24", "舵机1 电流");
            AddTxRow("servo_current_ma[1]", "26", "舵机2 电流");
            AddTxRow("servo_temp_c[0]", "28", "舵机1 温度");
            AddTxRow("servo_temp_c[1]", "29", "舵机2 温度");
            AddTxRow("servo_error[0]", "30", "舵机1 错误");
            AddTxRow("servo_error[1]", "31", "舵机2 错误");
            AddTxRow("sys_voltage_dv", "32", "系统电压 0.1 V");
            AddTxRow("sys_current_ca", "34", "系统电流 0.01 A");
            AddTxRow("esc_current_ma[0]", "36", "ESC1 电流");
            AddTxRow("esc_current_ma[1]", "38", "ESC2 电流");
            AddTxRow("loop_counter", "40", "控制循环计数");
            AddTxRow("led_status", "42", "RGB 状态码");
            AddTxRow("ecat_link_status", "44", "bit0/1 link, bit2..7 AL");
            AddTxRow("reserved0", "46", "保留");
            UiKit.Place(root, txGroup, 1, 2);

            /* --------------------------- ECAT parameters ----------------------- */
            GroupBox paramGroup = UiKit.NewGroup("EtherCAT 参数（写入后立即生效）");
            TableLayoutPanel paramLayout = UiKit.NewLayout(8);
            paramLayout.RowCount = 4;
            paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            paramLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            paramLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            paramLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            paramLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            UiKit.Place(paramLayout, UiKit.NewKeyLabel("节点别名"), 0, 0);
            UiKit.Place(paramLayout, _alias, 1, 0);
            UiKit.Place(paramLayout, UiKit.NewKeyLabel("DC 模式"), 2, 0);
            UiKit.Place(paramLayout, _dcMode, 3, 0);
            UiKit.Place(paramLayout, UiKit.NewKeyLabel("SYNC0 周期"), 4, 0);
            UiKit.Place(paramLayout, _sync0, 5, 0);
            UiKit.Place(paramLayout, UiKit.NewKeyLabel("SYNC1 周期"), 6, 0);
            UiKit.Place(paramLayout, _sync1, 7, 0);

            UiKit.Place(paramLayout, UiKit.NewKeyLabel("DC 偏移"), 0, 1);
            UiKit.Place(paramLayout, _dcShift, 1, 1);
            UiKit.Place(paramLayout, UiKit.NewKeyLabel("看门狗"), 2, 1);
            UiKit.Place(paramLayout, _watchdog, 3, 1);
            UiKit.Place(paramLayout, UiKit.NewKeyLabel("SDO 超时"), 4, 1);
            UiKit.Place(paramLayout, _sdoTimeout, 5, 1);
            UiKit.Place(paramLayout, _coeEnable, 6, 1);
            paramLayout.SetColumnSpan(_coeEnable, 2);

            TableLayoutPanel buttonRow = UiKit.NewLayout(4);
            buttonRow.RowCount = 1;
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            UiKit.Place(buttonRow, _writeParamsButton, 0, 0);
            UiKit.Place(buttonRow, _readParamsButton, 1, 0);
            UiKit.Place(buttonRow, _saveButton, 2, 0);
            UiKit.Place(buttonRow, _paramsStatus, 3, 0);
            UiKit.Place(paramLayout, buttonRow, 0, 2);
            paramLayout.SetColumnSpan(buttonRow, 8);

            Label note = UiKit.NewHint(
                "DC/SYNC 参数由 SSC 从站栈在进入 OP 时应用；修改后建议复位从站或重新初始化主站。" +
                "DC 偏移单位 ns；SYNC 周期单位 µs。主站侧（TwinCAT）需要正确配置 DC 与看门狗。");
            UiKit.Place(paramLayout, note, 0, 3);
            paramLayout.SetColumnSpan(note, 8);
            paramGroup.Controls.Add(paramLayout);
            UiKit.Place(root, paramGroup, 0, 3);

            /* --------------------------- TxPDO hex preview --------------------- */
            GroupBox hexGroup = UiKit.NewGroup("TxPDO 重组预览（48 字节，小端）");
            TableLayoutPanel hexLayout = UiKit.NewLayout(1);
            hexLayout.RowCount = 2;
            hexLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            hexLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            UiKit.Place(hexLayout, _refreshTime, 0, 0);
            UiKit.Place(hexLayout, _txHex, 0, 1);
            hexGroup.Controls.Add(hexLayout);
            UiKit.Place(root, hexGroup, 1, 3);

            Controls.Add(root);

            _shell.TelemetryReceived += OnTelemetryReceived;
            _shell.EcatStatusReceived += OnEcatStatusReceived;
            _shell.ParametersChanged += OnParametersChanged;
            _shell.ConnectionChanged += OnConnectionChanged;
            FillParameterEditors();
        }

        private static DataGridViewTextBoxColumn Column(string header, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.HeaderText = header;
            column.Width = width;
            column.ReadOnly = true;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            return column;
        }

        private void AddRxRow(string name, string offset, string type, string note)
        {
            _rxGrid.Rows.Add(name, offset, type, note);
        }

        private void AddTxRow(string name, string offset, string note)
        {
            int index = _txGrid.Rows.Add(name, offset, "-", note);
            _txGrid.Rows[index].Tag = name;
        }

        /* ------------------------------- updates ------------------------------- */

        private void OnConnectionChanged(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                _slaveInfo.Set("AL 状态", "未连接");
                _txHex.Clear();
                _lastTxPdo = null;
            }
            FillParameterEditors();
        }

        private void OnParametersChanged(object sender, EventArgs e)
        {
            FillParameterEditors();
        }

        private void OnTelemetryReceived(object sender, TelemetryEventArgs e)
        {
            TelemetryData t = e.Telemetry;
            if (t == null)
            {
                return;
            }
            _slaveInfo.Set("AL 状态", TelemetryData.DescribeEcatAlState(t.EcatAlState));
            _slaveInfo.SetColor("AL 状态", t.EcatAlState == 8 ? Theme.Ok : Theme.Warn);
            _slaveInfo.Set("端口0 Link", t.EcatPort0Link ? "Link 建立" : "断开");
            _slaveInfo.Set("端口1 Link", t.EcatPort1Link ? "Link 建立" : "断开");
            _slaveInfo.SetColor("端口0 Link", t.EcatPort0Link ? Theme.Ok : Theme.Warn);
            _slaveInfo.SetColor("端口1 Link", t.EcatPort1Link ? Theme.Ok : Theme.Warn);
            _slaveInfo.Set("故障码", t.FaultCode == 0 ? "无" : "0x" + t.FaultCode.ToString("X4"));
            _slaveInfo.SetColor("故障码", t.FaultCode == 0 ? Theme.Ok : Theme.Error);
            _slaveInfo.Set("控制循环计数", t.LoopCounter.ToString());
            _slaveInfo.Set("状态字 / 故障", "0x" + t.StatusWord.ToString("X4") + " / 0x" + t.FaultCode.ToString("X4"));
            _slaveInfo.Set("最近更新", DateTime.Now.ToString("HH:mm:ss.fff"));
            UpdateTxPdo(t);
        }

        private void OnEcatStatusReceived(object sender, EcatStatusEventArgs e)
        {
            EcatStatus status = e.Status;
            if (status == null)
            {
                return;
            }
            _slaveInfo.Set("ECAT 周期丢失计数", status.Cycles.ToString());
            _slaveInfo.SetColor("ECAT 周期丢失计数", status.Cycles == 0 ? Theme.Ok : Theme.Warn);
            _slaveInfo.Set("刷新时间戳", DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        private void UpdateTxPdo(TelemetryData t)
        {
            byte[] pdo = new byte[48];
            Bytes.PutU16(pdo, 0, t.StatusWord);
            Bytes.PutU16(pdo, 2, t.FaultCode);
            Bytes.PutU32(pdo, 4, t.EscErpm[0]);
            Bytes.PutU32(pdo, 8, t.EscErpm[1]);
            Bytes.PutU16(pdo, 12, t.EscTlmPeriodUs[0]);
            Bytes.PutU16(pdo, 14, t.EscTlmPeriodUs[1]);
            Bytes.PutU16(pdo, 16, (ushort)t.ServoPosD10[0]);
            Bytes.PutU16(pdo, 18, (ushort)t.ServoPosD10[1]);
            Bytes.PutU16(pdo, 20, (ushort)t.ServoSpeedRpm[0]);
            Bytes.PutU16(pdo, 22, (ushort)t.ServoSpeedRpm[1]);
            Bytes.PutU16(pdo, 24, (ushort)t.ServoCurrentMa[0]);
            Bytes.PutU16(pdo, 26, (ushort)t.ServoCurrentMa[1]);
            pdo[28] = t.ServoTempC[0];
            pdo[29] = t.ServoTempC[1];
            pdo[30] = t.ServoError[0];
            pdo[31] = t.ServoError[1];
            Bytes.PutU16(pdo, 32, t.SysVoltageDv);
            Bytes.PutU16(pdo, 34, (ushort)t.SysCurrentCa);
            Bytes.PutU16(pdo, 36, t.EscCurrentMa[0]);
            Bytes.PutU16(pdo, 38, t.EscCurrentMa[1]);
            Bytes.PutU16(pdo, 40, t.LoopCounter);
            Bytes.PutU16(pdo, 42, t.LedStatus);
            Bytes.PutU16(pdo, 44, t.EcatLinkStatus);
            _lastTxPdo = pdo;
            _txHex.Text = Bytes.ToHexString(pdo);

            SetTxValue("status_word", "0x" + t.StatusWord.ToString("X4"));
            SetTxValue("fault_code", "0x" + t.FaultCode.ToString("X4"));
            SetTxValue("esc_erpm[0]", t.EscErpm[0] + " eRPM");
            SetTxValue("esc_erpm[1]", t.EscErpm[1] + " eRPM");
            SetTxValue("esc_tlm_period_us[0]", t.EscTlmPeriodUs[0] + " µs");
            SetTxValue("esc_tlm_period_us[1]", t.EscTlmPeriodUs[1] + " µs");
            SetTxValue("servo_pos_d10[0]", t.ServoPosDeg(0).ToString("F1") + " °");
            SetTxValue("servo_pos_d10[1]", t.ServoPosDeg(1).ToString("F1") + " °");
            SetTxValue("servo_speed_rpm[0]", t.ServoSpeedRpm[0] + " rpm");
            SetTxValue("servo_speed_rpm[1]", t.ServoSpeedRpm[1] + " rpm");
            SetTxValue("servo_current_ma[0]", t.ServoCurrentMa[0] + " mA");
            SetTxValue("servo_current_ma[1]", t.ServoCurrentMa[1] + " mA");
            SetTxValue("servo_temp_c[0]", t.ServoTempC[0] + " °C");
            SetTxValue("servo_temp_c[1]", t.ServoTempC[1] + " °C");
            SetTxValue("servo_error[0]", "0x" + t.ServoError[0].ToString("X2"));
            SetTxValue("servo_error[1]", "0x" + t.ServoError[1].ToString("X2"));
            SetTxValue("sys_voltage_dv", t.SysVoltageVolt.ToString("F1") + " V");
            SetTxValue("sys_current_ca", t.SysCurrentAmp.ToString("F2") + " A");
            SetTxValue("esc_current_ma[0]", t.EscCurrentMa[0] + " mA");
            SetTxValue("esc_current_ma[1]", t.EscCurrentMa[1] + " mA");
            SetTxValue("loop_counter", t.LoopCounter.ToString());
            SetTxValue("led_status", "0x" + t.LedStatus.ToString("X4"));
            SetTxValue("ecat_link_status", "0x" + t.EcatLinkStatus.ToString("X4"));
            SetTxValue("reserved0", "0x0000");
        }

        private void SetTxValue(string name, string value)
        {
            for (int i = 0; i < _txGrid.Rows.Count; i++)
            {
                if (string.Equals(Convert.ToString(_txGrid.Rows[i].Cells[0].Value), name, StringComparison.Ordinal))
                {
                    _txGrid.Rows[i].Cells[2].Value = value;
                    return;
                }
            }
        }

        /* --------------------------- parameter editors ------------------------- */

        private void FillParameterEditors()
        {
            if (!_shell.IsConnected)
            {
                _paramsStatus.Text = "未连接设备";
                return;
            }
            _updating = true;
            try
            {
                SetNumericFromParam((int)ParamId.EcatNodeAlias, _alias);
                ParamDesc dc = _shell.FindParameter((int)ParamId.EcatDcMode);
                if (dc != null)
                {
                    _dcMode.SelectedIndex = Math.Min(Math.Max(dc.Value, 0), _dcMode.Items.Count - 1);
                }
                SetNumericFromParam((int)ParamId.EcatSync0PeriodUs, _sync0);
                SetNumericFromParam((int)ParamId.EcatSync1PeriodUs, _sync1);
                SetNumericFromParam((int)ParamId.EcatDcShiftNs, _dcShift);
                SetNumericFromParam((int)ParamId.EcatWatchdogMs, _watchdog);
                SetNumericFromParam((int)ParamId.EcatSdoTimeoutMs, _sdoTimeout);
                ParamDesc coe = _shell.FindParameter((int)ParamId.EcatEnableCoe);
                _coeEnable.Checked = coe == null || coe.Value != 0;
                ParamDesc invert = _shell.FindParameter((int)ParamId.EcatLedInvert);
                _ledInvert.Checked = invert != null && invert.Value != 0;
                _paramsStatus.Text = _shell.Parameters.Length > 0
                    ? "已从设备读取 " + _shell.Parameters.Length + " 项参数"
                    : "参数表未读取（点击“读取全部参数”）";
            }
            finally
            {
                _updating = false;
            }
        }

        private void SetNumericFromParam(int id, NumericUpDown numeric)
        {
            ParamDesc desc = _shell.FindParameter(id);
            if (desc == null)
            {
                return;
            }
            decimal value = desc.Value;
            if (value < numeric.Minimum)
            {
                value = numeric.Minimum;
            }
            if (value > numeric.Maximum)
            {
                value = numeric.Maximum;
            }
            numeric.Value = value;
        }

        private void OnReadParamsClicked(object sender, EventArgs e)
        {
            _shell.RequestParameterReload();
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            _shell.SaveParameters();
        }

        private void OnWriteParamsClicked(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                MessageBox.Show(this, "未连接设备。", "未就绪", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_updating)
            {
                return;
            }
            Cursor = Cursors.WaitCursor;
            int written = 0;
            string error = null;
            try
            {
                WriteParam(ParamId.EcatNodeAlias, (int)_alias.Value);
                written++;
                WriteParam(ParamId.EcatDcMode, _dcMode.SelectedIndex);
                written++;
                WriteParam(ParamId.EcatSync0PeriodUs, (int)_sync0.Value);
                written++;
                WriteParam(ParamId.EcatSync1PeriodUs, (int)_sync1.Value);
                written++;
                WriteParam(ParamId.EcatDcShiftNs, (int)_dcShift.Value);
                written++;
                WriteParam(ParamId.EcatWatchdogMs, (int)_watchdog.Value);
                written++;
                WriteParam(ParamId.EcatSdoTimeoutMs, (int)_sdoTimeout.Value);
                written++;
                WriteParam(ParamId.EcatEnableCoe, _coeEnable.Checked ? 1 : 0);
                written++;
                WriteParam(ParamId.EcatLedInvert, _ledInvert.Checked ? 1 : 0);
                written++;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            if (error != null)
            {
                _paramsStatus.Text = "写入失败: " + error;
                _paramsStatus.ForeColor = Theme.Error;
                _shell.LogError("EtherCAT", "写入 ECAT 参数失败: " + error);
            }
            else
            {
                _paramsStatus.Text = "已写入 " + written + " 项 ECAT 参数（点“保存到 Flash”持久化）";
                _paramsStatus.ForeColor = Theme.Ok;
                _shell.LogInfo("EtherCAT", "已写入 " + written + " 项 ECAT 参数");
            }
        }

        private void WriteParam(ParamId id, int value)
        {
            _shell.Client.SetParam((int)id, value);
            ParamDesc desc = _shell.FindParameter((int)id);
            if (desc != null)
            {
                desc.Value = value;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _shell.TelemetryReceived -= OnTelemetryReceived;
                _shell.EcatStatusReceived -= OnEcatStatusReceived;
                _shell.ParametersChanged -= OnParametersChanged;
                _shell.ConnectionChanged -= OnConnectionChanged;
            }
            base.Dispose(disposing);
        }
    }
}
