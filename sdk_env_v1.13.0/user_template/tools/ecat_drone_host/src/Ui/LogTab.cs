/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * LogTab.cs - 日志 tab: timestamped protocol log with hex toggle and file export.
 */

using System;
using System.Drawing;
using System.Windows.Forms;
using EcatDroneHost.App;

namespace EcatDroneHost.Ui
{
    public sealed class LogTab : UserControl
    {
        private readonly MainForm _shell;
        private readonly RichTextBox _box = UiKit.NewLogBox();
        private readonly CheckBox _hexCheck;
        private readonly CheckBox _autoScrollCheck;
        private readonly CheckBox _pauseCheck;
        private readonly Label _countLabel;
        private readonly Timer _flushTimer;
        private readonly Button _saveButton;
        private readonly Button _clearButton;
        private readonly Button _copyButton;
        private int _rendered;
        private bool _dirty;
        private bool _forceRebuild;

        public LogTab(MainForm shell)
        {
            _shell = shell;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            Padding = new Padding(8);

            _hexCheck = UiKit.NewCheck("显示十六进制原始帧", true);
            _autoScrollCheck = UiKit.NewCheck("自动滚动", true);
            _pauseCheck = UiKit.NewCheck("暂停刷新", false);
            _countLabel = UiKit.NewKeyLabel("0 条");
            _saveButton = UiKit.NewButton("保存日志到文件", OnSaveClicked);
            _clearButton = UiKit.NewButton("清空", OnClearClicked);
            _copyButton = UiKit.NewButton("复制全部", OnCopyClicked);

            _hexCheck.CheckedChanged += delegate { _forceRebuild = true; };
            _pauseCheck.CheckedChanged += delegate { _forceRebuild = true; };

            TableLayoutPanel root = UiKit.NewLayout(1);
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel toolbar = UiKit.NewLayout(9);
            toolbar.RowCount = 1;
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));

            UiKit.Place(toolbar, _saveButton, 0, 0);
            UiKit.Place(toolbar, _copyButton, 1, 0);
            UiKit.Place(toolbar, _clearButton, 2, 0);
            UiKit.Place(toolbar, _hexCheck, 3, 0);
            UiKit.Place(toolbar, _autoScrollCheck, 4, 0);
            UiKit.Place(toolbar, _pauseCheck, 5, 0);
            UiKit.Place(toolbar, _countLabel, 6, 0);
            Label hint = UiKit.NewHint("协议收发帧、参数/AM32 操作都会记录；十六进制模式下每帧附原始字节。");
            UiKit.Place(toolbar, hint, 7, 0);
            Label pathLabel = UiKit.NewKeyLabel("日志缓冲上限 20000 条");
            UiKit.Place(toolbar, pathLabel, 8, 0);
            UiKit.PlaceSpan(root, toolbar, 0, 0, 1);

            UiKit.PlaceSpan(root, _box, 0, 1, 1);
            Controls.Add(root);

            _flushTimer = new Timer();
            _flushTimer.Interval = 200;
            _flushTimer.Tick += OnFlushTick;
            _flushTimer.Start();

            _shell.Log.Changed += OnLogChanged;
            _shell.ConnectionChanged += OnConnectionChanged;
            _forceRebuild = true;
        }

        private void OnConnectionChanged(object sender, EventArgs e)
        {
            /* nothing special - the log keeps all history */
        }

        private void OnLogChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)delegate { _dirty = true; });
            }
            catch (Exception)
            {
                /* closing */
            }
        }

        private void OnFlushTick(object sender, EventArgs e)
        {
            if (_forceRebuild)
            {
                _forceRebuild = false;
                RebuildAll();
                return;
            }
            if (!_dirty)
            {
                return;
            }
            _dirty = false;
            if (_pauseCheck.Checked)
            {
                return;
            }
            AppendNewEntries();
        }

        private void RebuildAll()
        {
            LogEntry[] entries = _shell.Log.Snapshot();
            _box.Clear();
            _rendered = 0;
            int start = Math.Max(0, entries.Length - 2000);
            for (int i = start; i < entries.Length; i++)
            {
                AppendEntry(entries[i]);
            }
            _rendered = entries.Length;
            _countLabel.Text = entries.Length + " 条";
            if (_autoScrollCheck.Checked)
            {
                ScrollToEnd();
            }
        }

        private void AppendNewEntries()
        {
            LogEntry[] entries = _shell.Log.Snapshot();
            if (_rendered > entries.Length)
            {
                _rendered = 0;
                _box.Clear();
            }
            if (_rendered == entries.Length)
            {
                _countLabel.Text = entries.Length + " 条";
                return;
            }
            if (_box.TextLength > 600000)
            {
                RebuildAll();
                return;
            }
            for (int i = _rendered; i < entries.Length; i++)
            {
                AppendEntry(entries[i]);
            }
            _rendered = entries.Length;
            _countLabel.Text = entries.Length + " 条";
            if (_autoScrollCheck.Checked)
            {
                ScrollToEnd();
            }
        }

        private void AppendEntry(LogEntry entry)
        {
            Color color;
            switch (entry.Level)
            {
                case LogLevel.Warning: color = Color.FromArgb(255, 183, 77); break;
                case LogLevel.Error: color = Color.FromArgb(255, 138, 128); break;
                case LogLevel.Trace: color = Color.FromArgb(150, 158, 168); break;
                default: color = Color.FromArgb(224, 228, 232); break;
            }
            _box.SelectionStart = _box.TextLength;
            _box.SelectionLength = 0;
            _box.SelectionColor = color;
            _box.AppendText(entry.Format(true) + Environment.NewLine);
            _box.SelectionColor = _box.ForeColor;
        }

        private void ScrollToEnd()
        {
            _box.SelectionStart = _box.TextLength;
            _box.SelectionLength = 0;
            _box.ScrollToCaret();
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            string path = UiKit.PickSaveFile(this, "保存日志",
                "文本文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                "ecat_drone_host_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            if (path == null)
            {
                return;
            }
            try
            {
                int count = _shell.Log.SaveToFile(path, _hexCheck.Checked);
                _shell.ShowStatus("日志已保存: " + path + "（" + count + " 条）", false);
                _shell.LogInfo("日志", "已保存 " + count + " 条到 " + path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存日志失败:\r\n" + ex.Message, "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnCopyClicked(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(_box.Text);
                _shell.ShowStatus("日志已复制到剪贴板", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制失败:\r\n" + ex.Message, "复制失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            _shell.Log.Clear();
            _box.Clear();
            _rendered = 0;
            _countLabel.Text = "0 条";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _flushTimer.Dispose();
                _shell.Log.Changed -= OnLogChanged;
                _shell.ConnectionChanged -= OnConnectionChanged;
            }
            base.Dispose(disposing);
        }
    }
}
