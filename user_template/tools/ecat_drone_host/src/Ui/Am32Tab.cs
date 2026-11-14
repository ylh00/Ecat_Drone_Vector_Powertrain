/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * Am32Tab.cs - AM32调参 tab: 4-way bridge EEPROM read/write + decoded settings
 *              grid + raw hex view + raw exchange terminal.
 */

using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using EcatDroneHost.App;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.Ui
{
    public sealed class Am32Tab : UserControl
    {
        private readonly MainForm _shell;
        private readonly ComboBox _escCombo = UiKit.NewCombo();
        private readonly ComboBox _modeCombo = UiKit.NewCombo();
        private readonly Button _beginButton;
        private readonly Button _testButton;
        private readonly Button _readButton;
        private readonly Button _writeProgButton;
        private readonly Button _cancelWriteButton;
        private readonly Button _exitButton;
        private readonly Button _importButton;
        private readonly Button _exportButton;
        private readonly Button _statusButton;
        private readonly DataGridView _grid = UiKit.NewGrid();
        private readonly RichTextBox _hexBox = UiKit.NewHexBox();
        private readonly RichTextBox _terminal = UiKit.NewLogBox();
        private readonly TextBox _rawInput = UiKit.NewHexTextBox(string.Empty);
        private readonly Button _rawSendButton;
        private readonly Button _rawClearButton;
        private readonly Label _stateLabel;
        private readonly Label _bridgeStatusLabel;
        private readonly Label _loadedLabel;
        private readonly ProgressBar _progressBar;
        private readonly Label _progressLabel;
        private bool _busy;
        private bool _updating;
        private volatile bool _cancelWrite;
        private volatile bool _writing;
        private byte[] _image;

        private static readonly string[] ModeNames = new string[]
        {
            "引导命令容器 (0x04 / 0x05, CRC16-XMODEM)",
            "4way 接口帧 (0x3D / 0x3E, BLHeliSuite 兼容)",
            "原始字节（自行输入完整帧）"
        };

        public Am32Tab(MainForm shell)
        {
            _shell = shell;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            Padding = new Padding(8);

            _beginButton = UiKit.NewPrimaryButton("开始桥接", OnBeginClicked);
            _testButton = UiKit.NewButton("测试在线", OnTestClicked);
            _readButton = UiKit.NewButton("读取 EEPROM（4way）", OnReadClicked);
            _writeProgButton = UiKit.NewPrimaryButton("写入设置 (DShot 编程模式)", OnWriteProgramClicked);
            _cancelWriteButton = UiKit.NewButton("取消写入", OnCancelWriteClicked);
            _exitButton = UiKit.NewButton("退出引导", OnExitClicked);
            _progressBar = new ProgressBar();
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 192;
            _progressBar.Width = 190;
            _progressBar.Height = 18;
            _progressLabel = UiKit.NewKeyLabel("就绪");
            _importButton = UiKit.NewButton("导入 .bin", OnImportClicked);
            _exportButton = UiKit.NewButton("导出 .bin", OnExportClicked);
            _statusButton = UiKit.NewButton("查询状态", OnStatusClicked);
            _rawSendButton = UiKit.NewPrimaryButton("发送", OnRawSendClicked);
            _rawClearButton = UiKit.NewButton("清空", OnRawClearClicked);
            _stateLabel = UiKit.NewKeyLabel("未开始桥接");
            _bridgeStatusLabel = UiKit.NewKeyLabel("状态: -");
            _loadedLabel = UiKit.NewKeyLabel("EEPROM: 未读取");

            _escCombo.Items.AddRange(new object[] { "ESC1", "ESC2" });
            _escCombo.SelectedIndex = 0;
            _modeCombo.Items.AddRange(ModeNames);
            _modeCombo.SelectedIndex = 0;
            _modeCombo.SelectedIndexChanged += OnModeChanged;

            _rawInput.Multiline = true;
            _rawInput.Height = 60;
            _rawInput.ScrollBars = ScrollBars.Vertical;

            TableLayoutPanel root = UiKit.NewLayout(1);
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));

            /* ------------------------------ banner ----------------------------- */
            Label banner = UiKit.NewWarningBanner(
                "使用前: ESC 必须外接电池供电、电机必须停止、EtherCAT 不能处于 OP（否则固件返回 ERR_NOT_READY）。" +
                "读取/校验用 4way 桥接（暂停 DShot）；写入用 DShot 编程模式（4way 无法编程 ESC Flash），写完后自动 SAVE 并回读校验。");
            UiKit.PlaceSpan(root, banner, 0, 0, 1);

            /* ------------------------------ toolbar ---------------------------- */
            TableLayoutPanel toolbar = UiKit.NewLayout(10);
            toolbar.RowCount = 1;
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            UiKit.Place(toolbar, UiKit.NewKeyLabel("ESC"), 0, 0);
            UiKit.Place(toolbar, _escCombo, 1, 0);
            UiKit.Place(toolbar, _modeCombo, 2, 0);
            UiKit.Place(toolbar, _beginButton, 3, 0);
            UiKit.Place(toolbar, _testButton, 4, 0);
            UiKit.Place(toolbar, _statusButton, 5, 0);
            UiKit.Place(toolbar, _bridgeStatusLabel, 6, 0);
            UiKit.Place(toolbar, _stateLabel, 7, 0);
            toolbar.SetColumnSpan(_stateLabel, 2);
            UiKit.Place(toolbar, _loadedLabel, 9, 0);
            UiKit.PlaceSpan(root, toolbar, 0, 1, 1);

            /* --------------------------- settings / hex ------------------------- */
            TableLayoutPanel content = UiKit.NewLayout(2);
            content.RowCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));

            GroupBox settingsGroup = UiKit.NewGroup("AM32 设置（192 字节 EEPROM 解码，可直接编辑“值”）");
            TableLayoutPanel settingsLayout = UiKit.NewLayout(1);
            settingsLayout.RowCount = 2;
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.AutoScroll = true;
            actions.Controls.Add(_readButton);
            actions.Controls.Add(_writeProgButton);
            actions.Controls.Add(_cancelWriteButton);
            actions.Controls.Add(_progressBar);
            actions.Controls.Add(_progressLabel);
            actions.Controls.Add(_importButton);
            actions.Controls.Add(_exportButton);
            actions.Controls.Add(_exitButton);
            UiKit.Place(settingsLayout, actions, 0, 0);

            _grid.Columns.Add(MakeColumn("偏移", 58));
            _grid.Columns.Add(MakeColumn("字段", 178));
            _grid.Columns.Add(MakeColumn("中文", 176));
            _grid.Columns.Add(MakeColumn("值", 64));
            _grid.Columns.Add(MakeColumn("解码", 150));
            _grid.Columns.Add(MakeColumn("说明", 220));
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns[0].FillWeight = 6;
            _grid.Columns[1].FillWeight = 18;
            _grid.Columns[2].FillWeight = 17;
            _grid.Columns[3].FillWeight = 6;
            _grid.Columns[4].FillWeight = 14;
            _grid.Columns[5].FillWeight = 39;
            _grid.CellBeginEdit += OnGridCellBeginEdit;
            _grid.CellEndEdit += OnGridCellEndEdit;
            _grid.DataError += OnGridDataError;
            UiKit.Place(settingsLayout, _grid, 0, 1);
            settingsGroup.Controls.Add(settingsLayout);
            UiKit.Place(content, settingsGroup, 0, 0);

            GroupBox hexGroup = UiKit.NewGroup("原始 EEPROM（十进制偏移 / 16 字节每行）");
            TableLayoutPanel hexLayout = UiKit.NewLayout(1);
            hexLayout.RowCount = 2;
            hexLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            hexLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label hexHint = UiKit.NewHint("来源: AM32 固件 Inc/eeprom.h（见 .work/refs/DSHOT_AM32_NOTES.md §7）。tune[128] 与保留区不可编辑。写入按钮 = DShot 编程模式逐字节写入 + SAVE + 4way 回读校验。");
            UiKit.Place(hexLayout, hexHint, 0, 0);
            UiKit.Place(hexLayout, _hexBox, 0, 1);
            hexGroup.Controls.Add(hexLayout);
            UiKit.Place(content, hexGroup, 1, 0);

            UiKit.PlaceSpan(root, content, 0, 2, 1);

            /* --------------------------- raw terminal -------------------------- */
            GroupBox rawGroup = UiKit.NewGroup("原始 4way 交换终端（发送原始字节，显示 ESC 应答）");
            TableLayoutPanel rawLayout = UiKit.NewLayout(2);
            rawLayout.RowCount = 1;
            rawLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            rawLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            UiKit.Place(rawLayout, _terminal, 0, 0);

            TableLayoutPanel inputLayout = UiKit.NewLayout(1);
            inputLayout.RowCount = 4;
            inputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            inputLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            inputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            inputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            UiKit.Place(inputLayout, UiKit.NewKeyLabel("十六进制字节（例如 A5 5A ...）："), 0, 0);
            UiKit.Place(inputLayout, _rawInput, 0, 1);
            TableLayoutPanel rawButtonRow = UiKit.NewLayout(3);
            rawButtonRow.RowCount = 1;
            rawButtonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
            rawButtonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
            rawButtonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            UiKit.Place(rawButtonRow, _rawSendButton, 0, 0);
            UiKit.Place(rawButtonRow, _rawClearButton, 1, 0);
            UiKit.Place(inputLayout, rawButtonRow, 0, 2);
            Label rawHint = UiKit.NewHint("“原始字节”模式直接把输入发到 ESC 信号线；其他模式请使用上方按钮。");
            UiKit.Place(inputLayout, rawHint, 0, 3);
            UiKit.Place(rawLayout, inputLayout, 1, 0);
            rawGroup.Controls.Add(rawLayout);
            UiKit.PlaceSpan(root, rawGroup, 0, 3, 1);

            Controls.Add(root);

            _grid.Rows.Clear();
            _shell.ConnectionChanged += OnConnectionChanged;
            RefreshBridgeState();
        }

        private static DataGridViewTextBoxColumn MakeColumn(string header, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.HeaderText = header;
            column.Width = width;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            return column;
        }

        /* -------------------------------- state -------------------------------- */

        private void OnModeChanged(object sender, EventArgs e)
        {
            if (_shell.Am32Bridge == null)
            {
                return;
            }
            _shell.Am32Bridge.Mode = (FourWayFrameMode)_modeCombo.SelectedIndex;
            _shell.LogInfo("AM32", "4way 帧模式: " + Am32Bridge.ModeText(_shell.Am32Bridge.Mode));
        }

        private void OnConnectionChanged(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                if (_shell.Am32Bridge != null && _shell.Am32Bridge.IsOpen)
                {
                    try
                    {
                        _shell.Am32Bridge.Close();
                    }
                    catch (Exception)
                    {
                        /* connection already gone */
                    }
                }
            }
            RefreshBridgeState();
        }

        private void RefreshBridgeState()
        {
            bool connected = _shell.IsConnected;
            bool open = connected && _shell.Am32Bridge != null && _shell.Am32Bridge.IsOpen;
            bool loaded = _image != null;

            _beginButton.Text = open ? "结束桥接" : "开始桥接";
            _beginButton.Enabled = connected && !_busy;
            _testButton.Enabled = open && !_busy;
            _readButton.Enabled = open && !_busy;
            /* programming mode needs the ESC line free (do not hold the 4-way bridge) */
            _writeProgButton.Enabled = connected && !_busy && loaded && !open;
            _cancelWriteButton.Enabled = _writing;
            _exitButton.Enabled = open && !_busy;
            _importButton.Enabled = !_busy;
            _exportButton.Enabled = !_busy && loaded;
            _statusButton.Enabled = connected && !_busy;
            _rawSendButton.Enabled = open && !_busy;
            _rawClearButton.Enabled = true;
            _grid.Enabled = !_busy;
            _escCombo.Enabled = !open && connected && !_busy;
            _modeCombo.Enabled = !open && !_busy;

            if (!connected)
            {
                _stateLabel.Text = "未连接";
                _stateLabel.ForeColor = Theme.SubText;
            }
            else if (open)
            {
                _stateLabel.Text = "桥接已占用（ESC" + (_shell.Am32Bridge.Esc + 1) + "）";
                _stateLabel.ForeColor = Theme.Warn;
            }
            else
            {
                _stateLabel.Text = "未开始桥接";
                _stateLabel.ForeColor = Theme.SubText;
            }
            _loadedLabel.Text = _image == null
                ? "EEPROM: 未读取"
                : "EEPROM: 已加载 192 字节";
        }

        /* ------------------------------- actions ------------------------------- */

        private void OnBeginClicked(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                return;
            }
            if (_shell.Am32Bridge.IsOpen)
            {
                try
                {
                    _shell.Am32Bridge.Close();
                    _shell.LogInfo("AM32", "桥接结束，DShot 输出已恢复");
                }
                catch (Exception ex)
                {
                    ShowError("结束桥接失败", ex);
                }
                RefreshBridgeState();
                return;
            }
            int esc = _escCombo.SelectedIndex;
            try
            {
                _shell.Am32Bridge.Mode = (FourWayFrameMode)_modeCombo.SelectedIndex;
                _shell.Am32Bridge.Open(esc);
                _shell.LogInfo("AM32", string.Format("桥接开始: ESC{0}, 模式 {1}", esc + 1,
                    Am32Bridge.ModeText(_shell.Am32Bridge.Mode)));
                RefreshBridgeState();
                QueryBridgeStatusAsync();
            }
            catch (Exception ex)
            {
                ShowError("开始桥接失败", ex);
                RefreshBridgeState();
            }
        }

        private void OnTestClicked(object sender, EventArgs e)
        {
            RunBridgeOperation("测试在线", delegate
            {
                _shell.Am32Bridge.TestAlive();
            });
        }

        private void OnReadClicked(object sender, EventArgs e)
        {
            RunBridgeOperation("读取 EEPROM", delegate
            {
                byte[] image = _shell.Am32Bridge.ReadEeprom();
                RunOnUi(delegate
                {
                    _image = image;
                    PopulateFromImage();
                    _shell.LogInfo("AM32", "已读取 192 字节 EEPROM");
                });
            });
        }

        private void OnCancelWriteClicked(object sender, EventArgs e)
        {
            _cancelWrite = true;
            _progressLabel.Text = "正在取消 ...";
            _shell.LogInfo("AM32", "用户请求取消 DShot 编程写入");
        }

        /// <summary>
        /// Write the whole 192 byte EEPROM image through the firmware's AM32
        /// DShot programming mode (HOST_CMD_AM32_PROG_WRITE, one byte at a time),
        /// then commit with HOST_CMD_AM32_PROG_SAVE and verify the result with a
        /// 4-way EEPROM read-back.
        /// </summary>
        private void OnWriteProgramClicked(object sender, EventArgs e)
        {
            if (!_shell.IsConnected)
            {
                MessageBox.Show(this, "未连接设备。", "未就绪", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_image == null)
            {
                MessageBox.Show(this, "请先读取 EEPROM（4way 读取为权威数据，同时作为写入前备份），再写入。",
                    "没有数据", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            /* precondition: the firmware only allows the passthrough when enabled */
            ParamDesc passthrough = _shell.FindParameter((int)ParamId.Am32EnablePassthrough);
            if (passthrough != null && passthrough.Value == 0)
            {
                MessageBox.Show(this,
                    "参数 am32_enable_passthrough = 0，固件会以 ERR_UNSUPPORTED 拒绝写入。\r\n" +
                    "请到“参数”页把 AM32_ENABLE_PASSTHROUGH 设为 1 后再试。",
                    "AM32 透传未启用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            /* precondition: EtherCAT must not be in OP and outputs must be disabled */
            TelemetryData telemetry = _shell.LastTelemetry;
            if (telemetry != null && (telemetry.StatusBit(0) || telemetry.StatusBit(1)))
            {
                MessageBox.Show(this,
                    "EtherCAT 当前处于 OP 或输出使能，固件会以 ERR_NOT_READY 拒绝台架命令。\r\n" +
                    "请先让主站退出 OP，并确认电机已停转。",
                    "EtherCAT 运行中", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_shell.Am32Bridge != null && _shell.Am32Bridge.IsOpen)
            {
                MessageBox.Show(this,
                    "DShot 编程模式需要独占 ESC 信号线。\r\n请先点“结束桥接”，再写入设置。",
                    "桥接占用中", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this,
                    "确认通过 DShot 编程模式写入全部 192 字节，写完后保存到 ESC Flash 并回读校验？\r\n" +
                    "整个流程约 2..3 秒，期间不要断电。写入错误参数可能导致电机异常。",
                    "写入 AM32 设置", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            byte[] image = (byte[])_image.Clone();
            int esc = _escCombo.SelectedIndex;
            string backupPath = SaveBackup(image, esc);
            _cancelWrite = false;
            _writing = true;
            SetBusy(true);
            _progressBar.Value = 0;
            _progressLabel.Text = "写入 0/192";
            _shell.LogInfo("AM32", "前置条件检查通过: 4way 桥未占用 / EtherCAT 非 OP / passthrough 已启用");
            if (backupPath != null)
            {
                _shell.LogInfo("AM32", "写入前备份当前 EEPROM: " + backupPath);
            }
            _shell.LogInfo("AM32", string.Format(
                "开始 DShot 编程模式写入: ESC{0}, 192 字节, 每字节约 9 ms（每 4 字节让出约 10 ms）", esc + 1));

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                bool cancelled = false;
                int written = 0;
                try
                {
                    for (int address = 0; address < Am32EepromMap.Size; address++)
                    {
                        if (_cancelWrite)
                        {
                            cancelled = true;
                            break;
                        }
                        _shell.Client.Am32ProgWrite(esc, address, image[address], 40);
                        written++;
                        /* pacing: one request at a time, let the board drain the queue */
                        if ((written % 4) == 0)
                        {
                            Thread.Sleep(10);
                        }
                        if ((written % 16) == 0 || written == Am32EepromMap.Size)
                        {
                            int snapshot = written;
                            RunOnUi(delegate
                            {
                                _progressBar.Value = Math.Min(snapshot, _progressBar.Maximum);
                                _progressLabel.Text = string.Format("写入 {0}/192", snapshot);
                            });
                            _shell.LogInfo("AM32", string.Format("DShot 编程: 已写 {0}/192 字节", snapshot));
                        }
                    }
                    if (!cancelled)
                    {
                        RunOnUi(delegate { _progressLabel.Text = "保存到 ESC Flash ..."; });
                        _shell.LogInfo("AM32", "192 字节写入完成，发送 0x55 AM32_PROG_SAVE ...");
                        _shell.Client.Am32ProgSave(esc);
                        /* let the ESC finish the flash write before reading back */
                        Thread.Sleep(50);
                        _shell.LogInfo("AM32", "SAVE 完成（ESC Flash 写入等待 50 ms），准备 4way 回读校验");
                    }
                }
                catch (HostStatusException ex)
                {
                    error = DescribeAm32WriteStatus(ex.Status);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                RunOnUi(delegate
                {
                    _writing = false;
                    SetBusy(false);
                    if (error != null)
                    {
                        _progressLabel.Text = "写入失败";
                        _shell.LogError("AM32", "DShot 编程写入失败: " + error);
                        MessageBox.Show(this, "写入失败:\r\n" + error, "AM32 写入",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    if (cancelled)
                    {
                        _shell.LogWarning("AM32", string.Format(
                            "DShot 编程写入已取消（已写 {0}/192 字节，未保存到 Flash）", written));
                        _progressLabel.Text = "已取消";
                        return;
                    }
                    _shell.LogInfo("AM32", "DShot 编程写入 + SAVE 完成，开始 4way 回读校验 ...");
                    StartVerifyAfterProgram(esc);
                });
            });
        }

        /// <summary>Save the current EEPROM image into build\backup before writing.</summary>
        private string SaveBackup(byte[] image, int esc)
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                if (string.IsNullOrEmpty(exeDir))
                {
                    exeDir = Environment.CurrentDirectory;
                }
                string dir = Path.Combine(exeDir, "backup");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, string.Format("am32_esc{0}_{1}.bin",
                    esc + 1, DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                File.WriteAllBytes(path, image);
                return path;
            }
            catch (Exception ex)
            {
                _shell.LogWarning("AM32", "写入前备份失败（不影响写入）: " + ex.Message);
                return null;
            }
        }

        /// <summary>Actionable Chinese text for the AM32 programming mode status codes.</summary>
        private static string DescribeAm32WriteStatus(HostStatus status)
        {
            switch (status)
            {
                case HostStatus.ErrBusy:
                    return "命令队列持续忙（ERR_BUSY）：稍后重试，或降低台架操作频率。";
                case HostStatus.ErrParamRange:
                    return "ESC 编号 / EEPROM 地址非法（ERR_PARAM_RANGE）。";
                case HostStatus.ErrNotReady:
                    return "固件拒绝（ERR_NOT_READY）：EtherCAT 处于 OP / 输出使能 / 电机未停转 / 4way 桥仍占用。";
                case HostStatus.ErrUnsupported:
                    return "固件不支持（ERR_UNSUPPORTED）：请确认固件已编译 AM32 调参，且参数 am32_enable_passthrough = 1。";
                case HostStatus.ErrParamId:
                    return "参数 ID / ESC 编号无效（ERR_PARAM_ID）。";
                default:
                    return HostStatusText.Describe(status) + string.Format("（0x{0:X2}）", (byte)status);
            }
        }

        /// <summary>Re-open the 4-way bridge, read the EEPROM back and compare.</summary>
        private void StartVerifyAfterProgram(int esc)
        {
            byte[] expected = (byte[])_image.Clone();
            SetBusy(true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                byte[] actual = null;
                try
                {
                    if (!_shell.Am32Bridge.IsOpen)
                    {
                        _shell.Am32Bridge.Open(esc);
                    }
                    actual = _shell.Am32Bridge.ReadEeprom();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                finally
                {
                    try
                    {
                        if (_shell.Am32Bridge.IsOpen)
                        {
                            _shell.Am32Bridge.Close();
                        }
                    }
                    catch (Exception)
                    {
                        /* ignore close errors */
                    }
                }

                RunOnUi(delegate
                {
                    SetBusy(false);
                    RefreshBridgeState();
                    if (error != null)
                    {
                        _progressLabel.Text = "回读校验失败";
                        _shell.LogError("AM32", "写入后回读校验失败: " + error);
                        MessageBox.Show(this,
                            "设置已写入并保存，但 4way 回读校验失败:\r\n" + error,
                            "校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    int mismatch = -1;
                    int count = Math.Min(expected.Length, actual.Length);
                    for (int i = 0; i < count; i++)
                    {
                        if (expected[i] != actual[i])
                        {
                            mismatch = i;
                            break;
                        }
                    }
                    if (mismatch >= 0)
                    {
                        _progressLabel.Text = "回读不一致";
                        _shell.LogError("AM32", string.Format(
                            "回读校验不一致: 偏移 {0} 期望 0x{1:X2} 实际 0x{2:X2}",
                            mismatch, expected[mismatch], actual[mismatch]));
                        MessageBox.Show(this,
                            string.Format("回读校验不一致：偏移 {0} 期望 0x{1:X2}，实际 0x{2:X2}。",
                                mismatch, expected[mismatch], actual[mismatch]),
                            "校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    _image = actual;
                    PopulateFromImage();
                    _progressBar.Value = _progressBar.Maximum;
                    _progressLabel.Text = "写入并校验成功";
                    _shell.LogInfo("AM32", "DShot 编程写入成功，4way 回读校验一致（192 字节）");
                    _shell.ShowStatus("AM32: 写入并校验成功", false);
                });
            });
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            RunBridgeOperation("退出引导", delegate
            {
                _shell.Am32Bridge.ExitBootloader();
            });
        }

        private void OnStatusClicked(object sender, EventArgs e)
        {
            QueryBridgeStatusAsync();
        }

        private void QueryBridgeStatusAsync()
        {
            if (!_shell.IsConnected)
            {
                return;
            }
            int esc = _shell.Am32Bridge.IsOpen ? _shell.Am32Bridge.Esc : _escCombo.SelectedIndex;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Am32BridgeStatus status = null;
                string error = null;
                try
                {
                    status = _shell.Client.Am32Status(esc);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                RunOnUi(delegate
                {
                    if (error != null)
                    {
                        _bridgeStatusLabel.Text = "状态: 读取失败";
                        _bridgeStatusLabel.ForeColor = Theme.Error;
                    }
                    else
                    {
                        _bridgeStatusLabel.Text = "状态: " + status.ToString();
                        _bridgeStatusLabel.ForeColor = status.LastError == 0 ? Theme.Ok : Theme.Warn;
                    }
                });
            });
        }

        private void OnImportClicked(object sender, EventArgs e)
        {
            string path = UiKit.PickOpenFile(this, "导入 AM32 EEPROM 镜像",
                "AM32 EEPROM 镜像 (*.bin)|*.bin|所有文件 (*.*)|*.*");
            if (path == null)
            {
                return;
            }
            try
            {
                byte[] data = File.ReadAllBytes(path);
                if (data.Length < Am32EepromMap.Size)
                {
                    MessageBox.Show(this, "文件太短: " + data.Length + " 字节（需要 192 字节）。",
                        "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _image = new byte[Am32EepromMap.Size];
                Array.Copy(data, _image, _image.Length);
                PopulateFromImage();
                _shell.LogInfo("AM32", "已导入 " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                ShowError("导入失败", ex);
            }
        }

        private void OnExportClicked(object sender, EventArgs e)
        {
            if (_image == null)
            {
                return;
            }
            string path = UiKit.PickSaveFile(this, "导出 AM32 EEPROM 镜像",
                "AM32 EEPROM 镜像 (*.bin)|*.bin|所有文件 (*.*)|*.*", "am32_eeprom_esc1.bin");
            if (path == null)
            {
                return;
            }
            try
            {
                File.WriteAllBytes(path, _image);
                _shell.LogInfo("AM32", "已导出 " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                ShowError("导出失败", ex);
            }
        }

        private void OnRawSendClicked(object sender, EventArgs e)
        {
            if (!_shell.IsConnected || !_shell.Am32Bridge.IsOpen)
            {
                MessageBox.Show(this, "请先连接设备并开始桥接。", "未就绪",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string error;
            byte[] data = Bytes.ParseHexString(_rawInput.Text, out error);
            if (data == null || data.Length == 0)
            {
                MessageBox.Show(this, "十六进制数据无效: " + (error == null ? "空" : error),
                    "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RunBridgeOperation("原始交换", delegate
            {
                byte[] reply = _shell.Am32Bridge.RawExchange(data);
                RunOnUi(delegate
                {
                    AppendTerminal("TX (" + data.Length + " B): " + Bytes.ToHexString(data));
                    AppendTerminal("RX (" + (reply == null ? 0 : reply.Length) + " B): " +
                        (reply == null || reply.Length == 0 ? "(无应答)" : Bytes.ToHexString(reply)));
                });
            });
        }

        private void OnRawClearClicked(object sender, EventArgs e)
        {
            _terminal.Clear();
        }

        private void AppendTerminal(string text)
        {
            _terminal.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text + "\r\n");
        }

        /* --------------------------- background helper ------------------------- */

        private void RunBridgeOperation(string name, Action operation)
        {
            if (_busy)
            {
                return;
            }
            if (!_shell.IsConnected)
            {
                MessageBox.Show(this, "未连接设备。", "未就绪", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!_shell.Am32Bridge.IsOpen && name != "原始交换")
            {
                MessageBox.Show(this, "请先点击“开始桥接”占用 ESC 信号线。", "未开始桥接",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetBusy(true);
            _loadedLabel.Text = name + " ...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try
                {
                    operation();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                RunOnUi(delegate
                {
                    SetBusy(false);
                    RefreshBridgeState();
                    if (error != null)
                    {
                        _shell.LogError("AM32", name + " 失败: " + error);
                        ShowError(name + " 失败", new Exception(error));
                    }
                    else
                    {
                        _shell.ShowStatus("AM32: " + name + " 成功", false);
                    }
                });
            });
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            RefreshBridgeState();
        }

        private void ShowError(string title, Exception ex)
        {
            _shell.LogError("AM32", title + ": " + ex.Message);
            MessageBox.Show(this, title + ":\r\n" + ex.Message, title,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        /* ------------------------------ grid <-> image ------------------------ */

        private void PopulateFromImage()
        {
            _updating = true;
            try
            {
                _grid.Rows.Clear();
                if (_image == null)
                {
                    _hexBox.Clear();
                    return;
                }
                for (int i = 0; i < Am32EepromMap.Fields.Length; i++)
                {
                    Am32Field field = Am32EepromMap.Fields[i];
                    int rowIndex;
                    if (field.Kind == Am32FieldKind.Range)
                    {
                        rowIndex = _grid.Rows.Add(field.Offset + ".." + (field.Offset + field.Length - 1),
                            field.Tag, field.NameZh, "-",
                            field.Length + " 字节", field.Note);
                        _grid.Rows[rowIndex].Cells[3].ReadOnly = true;
                        _grid.Rows[rowIndex].Cells[3].Style.ForeColor = Theme.SubText;
                    }
                    else
                    {
                        int value = _image[field.Offset];
                        rowIndex = _grid.Rows.Add(field.Offset.ToString(), field.Tag, field.NameZh,
                            value.ToString(), field.FormatValue(value), field.Note);
                        _grid.Rows[rowIndex].Tag = field;
                    }
                }
                _hexBox.Text = Am32EepromMap.ToHexDump(_image, 16);
            }
            finally
            {
                _updating = false;
            }
        }

        private void OnGridCellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (_updating || e.ColumnIndex != 3 || e.RowIndex < 0)
            {
                return;
            }
        }

        private void OnGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_updating || e.ColumnIndex != 3 || e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
            {
                return;
            }
            DataGridViewRow row = _grid.Rows[e.RowIndex];
            Am32Field field = row.Tag as Am32Field;
            if (field == null || field.Kind == Am32FieldKind.Range || _image == null)
            {
                return;
            }
            int value;
            string text = Convert.ToString(row.Cells[3].Value);
            if (!int.TryParse(text, out value) || value < 0 || value > 255)
            {
                MessageBox.Show(this, "请输入 0..255 的数值。", "参数错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                row.Cells[3].Value = _image[field.Offset].ToString();
                return;
            }
            Am32EepromMap.SetByte(_image, field, value);
            row.Cells[4].Value = field.FormatValue(value);
            _hexBox.Text = Am32EepromMap.ToHexDump(_image, 16);
            _shell.LogInfo("AM32", string.Format("字段 {0} 改为 {1}", field.Tag, value));
        }

        private void OnGridDataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _shell.ConnectionChanged -= OnConnectionChanged;
            }
            base.Dispose(disposing);
        }
    }
}
