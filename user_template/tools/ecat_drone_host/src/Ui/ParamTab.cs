/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * ParamTab.cs - 参数 tab: parameter table editor grouped by module prefix.
 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EcatDroneHost.App;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.Ui
{
    public sealed class ParamTab : UserControl
    {
        private readonly MainForm _shell;
        private readonly DataGridView _grid = UiKit.NewGrid();
        private readonly ComboBox _groupCombo = UiKit.NewCombo();
        private readonly TextBox _searchBox = UiKit.NewTextBox(string.Empty, false);
        private readonly Label _countLabel = UiKit.NewKeyLabel("未读取");
        private readonly Label _statusLabel = UiKit.NewKeyLabel(string.Empty);

        /* detail pane */
        private readonly Label _detailName = UiKit.NewValueLabel("-");
        private readonly Label _detailType = UiKit.NewLabel("-");
        private readonly Label _detailRange = UiKit.NewLabel("-");
        private readonly Label _detailDefault = UiKit.NewLabel("-");
        private readonly Label _detailCurrent = UiKit.NewLabel("-");
        private readonly Label _detailDesc = UiKit.NewHint("-");
        private readonly NumericUpDown _detailValue = UiKit.NewNumeric(-2147483648M, 2147483647M, 0);
        private readonly Button _writeButton;

        private ParamDesc[] _all = new ParamDesc[0];
        private ParamDesc _selected;
        private bool _updating;
        private string _editingOriginal = string.Empty;

        public ParamTab(MainForm shell)
        {
            _shell = shell;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            Padding = new Padding(8);
            _writeButton = UiKit.NewPrimaryButton("写入该参数", OnWriteDetailClicked);

            TableLayoutPanel root = UiKit.NewLayout(1);
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));

            /* ------------------------------ toolbar ---------------------------- */
            TableLayoutPanel toolbar = UiKit.NewLayout(8);
            toolbar.RowCount = 1;
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            UiKit.Place(toolbar, UiKit.NewButton("读取全部参数", OnReloadClicked), 0, 0);
            UiKit.Place(toolbar, UiKit.NewButton("保存到 Flash", OnSaveClicked), 1, 0);
            UiKit.Place(toolbar, UiKit.NewButton("恢复默认值", OnResetClicked), 2, 0);
            UiKit.Place(toolbar, UiKit.NewKeyLabel("分组"), 3, 0);
            _groupCombo.Width = 110;
            _groupCombo.Items.Add("全部");
            _groupCombo.Items.AddRange(ParamCatalog.GroupOrder);
            _groupCombo.SelectedIndex = 0;
            _groupCombo.SelectedIndexChanged += OnFilterChanged;
            UiKit.Place(toolbar, _groupCombo, 4, 0);
            _searchBox.Text = string.Empty;
            _searchBox.TextChanged += OnFilterChanged;
            Panel searchPanel = new Panel();
            searchPanel.Dock = DockStyle.Fill;
            Label searchLabel = UiKit.NewKeyLabel("搜索: ");
            searchLabel.Dock = DockStyle.Left;
            searchLabel.Width = 46;
            _searchBox.Dock = DockStyle.Fill;
            searchPanel.Controls.Add(_searchBox);
            searchPanel.Controls.Add(searchLabel);
            UiKit.Place(toolbar, searchPanel, 5, 0);
            UiKit.Place(toolbar, _countLabel, 6, 0);
            Label hint = UiKit.NewHint("双击“当前值”单元格可直接编辑；只读参数不可写。修改立即生效，点“保存到 Flash”掉电保持。");
            UiKit.Place(toolbar, hint, 7, 0);
            UiKit.PlaceSpan(root, toolbar, 0, 0, 1);

            /* ------------------------------- grid ------------------------------ */
            _grid.Columns.Add(NewColumn("名称", 200, false));
            _grid.Columns.Add(NewColumn("中文名", 190, false));
            _grid.Columns.Add(NewColumn("当前值", 110, false));
            _grid.Columns.Add(NewColumn("范围", 150, false));
            _grid.Columns.Add(NewColumn("单位", 76, false));
            _grid.Columns.Add(NewColumn("说明", 260, false));
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns[0].FillWeight = 16;
            _grid.Columns[1].FillWeight = 15;
            _grid.Columns[2].FillWeight = 9;
            _grid.Columns[3].FillWeight = 13;
            _grid.Columns[4].FillWeight = 7;
            _grid.Columns[5].FillWeight = 40;
            _grid.CellBeginEdit += OnCellBeginEdit;
            _grid.CellEndEdit += OnCellEndEdit;
            _grid.SelectionChanged += OnSelectionChanged;
            _grid.DataError += OnGridDataError;
            UiKit.PlaceSpan(root, _grid, 0, 1, 1);

            /* ----------------------------- detail pane ------------------------- */
            GroupBox detailGroup = UiKit.NewGroup("选中参数详情 / 编辑");
            TableLayoutPanel detail = UiKit.NewLayout(8);
            detail.RowCount = 3;
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            UiKit.Place(detail, UiKit.NewKeyLabel("参数"), 0, 0);
            UiKit.Place(detail, _detailName, 1, 0);
            UiKit.Place(detail, UiKit.NewKeyLabel("类型"), 2, 0);
            UiKit.Place(detail, _detailType, 3, 0);
            UiKit.Place(detail, UiKit.NewKeyLabel("范围"), 4, 0);
            UiKit.Place(detail, _detailRange, 5, 0);
            UiKit.Place(detail, _writeButton, 7, 0);
            detail.SetRowSpan(_writeButton, 2);

            UiKit.Place(detail, UiKit.NewKeyLabel("默认值"), 0, 1);
            UiKit.Place(detail, _detailDefault, 1, 1);
            UiKit.Place(detail, UiKit.NewKeyLabel("当前值"), 2, 1);
            UiKit.Place(detail, _detailCurrent, 3, 1);
            UiKit.Place(detail, UiKit.NewKeyLabel("新值"), 4, 1);
            UiKit.Place(detail, _detailValue, 5, 1);

            Label descTitle = UiKit.NewKeyLabel("说明");
            UiKit.Place(detail, descTitle, 0, 2);
            UiKit.Place(detail, _detailDesc, 1, 2);
            detail.SetColumnSpan(_detailDesc, 7);
            detailGroup.Controls.Add(detail);
            UiKit.PlaceSpan(root, detailGroup, 0, 2, 1);

            UiKit.PlaceSpan(root, _statusLabel, 0, 3, 1);
            Controls.Add(root);

            _shell.ConnectionChanged += OnConnectionChanged;
            _shell.ParametersChanged += OnParametersChanged;
            Rebuild();
        }

        private static DataGridViewTextBoxColumn NewColumn(string header, int width, bool readOnly)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.HeaderText = header;
            column.Width = width;
            column.ReadOnly = readOnly;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            return column;
        }

        /* ------------------------------ events -------------------------------- */

        private void OnConnectionChanged(object sender, EventArgs e)
        {
            Rebuild();
        }

        private void OnParametersChanged(object sender, EventArgs e)
        {
            Rebuild();
        }

        private void OnReloadClicked(object sender, EventArgs e)
        {
            _shell.RequestParameterReload();
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            _shell.SaveParameters();
        }

        private void OnResetClicked(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "确认把所有参数恢复为默认值？\r\n（只修改 RAM，需再点“保存到 Flash”才持久化）",
                    "恢复默认值", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            _shell.ResetParameters();
        }

        private void OnFilterChanged(object sender, EventArgs e)
        {
            RebuildRows();
        }

        private void OnGridDataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        /* ------------------------------- build -------------------------------- */

        private void Rebuild()
        {
            _all = _shell.Parameters ?? new ParamDesc[0];
            _grid.Rows.Clear();
            _selected = null;
            _countLabel.Text = _all.Length > 0 ? _all.Length + " 项" : "未读取";
            RebuildRows();
            if (_grid.Rows.Count > 0)
            {
                _grid.Rows[0].Selected = true;
            }
            else
            {
                _detailName.Text = "-";
                _detailType.Text = "-";
                _detailRange.Text = "-";
                _detailDefault.Text = "-";
                _detailCurrent.Text = "-";
                _detailDesc.Text = _shell.IsConnected
                    ? "参数表尚未读取，点击“读取全部参数”。"
                    : "未连接设备。连接后可读取设备参数表。";
            }
        }

        private void RebuildRows()
        {
            _updating = true;
            try
            {
                _grid.Rows.Clear();
                string group = _groupCombo.SelectedIndex > 0 ? _groupCombo.SelectedItem as string : null;
                string search = _searchBox.Text.Trim();

                List<ParamDesc> ordered = new List<ParamDesc>(_all);
                ordered.Sort(delegate (ParamDesc a, ParamDesc b)
                {
                    int groupCompare = string.Compare(GroupOf(a), GroupOf(b), StringComparison.Ordinal);
                    if (groupCompare != 0)
                    {
                        return groupCompare;
                    }
                    return a.Id.CompareTo(b.Id);
                });

                for (int i = 0; i < ordered.Count; i++)
                {
                    ParamDesc desc = ordered[i];
                    if (group != null && string.Compare(GroupOf(desc), group, StringComparison.Ordinal) != 0)
                    {
                        continue;
                    }
                    if (search.Length > 0 && !Matches(desc, search))
                    {
                        continue;
                    }
                    ParamInfo info = ParamCatalog.Find(desc.Id);
                    string nameZh = info != null ? info.NameZh : string.Empty;
                    string unit = info != null ? (info.DisplayUnit.Length > 0 ? info.DisplayUnit : info.Unit) : string.Empty;
                    string description = desc.Description.Length > 0
                        ? desc.Description
                        : (info != null ? info.DescriptionZh : string.Empty);
                    int rowIndex = _grid.Rows.Add(
                        desc.Name,
                        nameZh,
                        FormatCellValue(desc),
                        "[" + desc.Min + " .. " + desc.Max + "]",
                        unit,
                        description);
                    DataGridViewRow row = _grid.Rows[rowIndex];
                    row.Tag = desc;
                    row.Cells[2].ReadOnly = desc.ReadOnly;
                    if (desc.ReadOnly)
                    {
                        row.Cells[2].Style.ForeColor = Theme.SubText;
                    }
                    if (!desc.ValueInRange)
                    {
                        row.Cells[2].Style.ForeColor = Theme.Error;
                    }
                }
            }
            finally
            {
                _updating = false;
            }
            _countLabel.Text = _all.Length > 0
                ? _grid.Rows.Count + " / " + _all.Length + " 项"
                : "未读取";
        }

        private static string GroupOf(ParamDesc desc)
        {
            ParamInfo info = ParamCatalog.Find(desc.Id);
            if (info != null)
            {
                return info.Group;
            }
            return ParamCatalog.GuessGroup(desc.Name);
        }

        private static bool Matches(ParamDesc desc, string search)
        {
            if (desc.Name.ToUpperInvariant().IndexOf(search.ToUpperInvariant(), StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            if (desc.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            ParamInfo info = ParamCatalog.Find(desc.Id);
            if (info != null && info.NameZh.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return false;
        }

        private static string FormatCellValue(ParamDesc desc)
        {
            ParamInfo info = ParamCatalog.Find(desc.Id);
            if (info != null)
            {
                return info.FormatValue(desc.Value);
            }
            return desc.Value.ToString();
        }

        /* ------------------------------ editing ------------------------------- */

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            if (_updating || _grid.SelectedRows.Count == 0)
            {
                return;
            }
            ParamDesc desc = _grid.SelectedRows[0].Tag as ParamDesc;
            if (desc == null)
            {
                return;
            }
            _selected = desc;
            ParamInfo info = ParamCatalog.Find(desc.Id);
            _detailName.Text = desc.Name + (info != null ? "（" + info.NameZh + "）" : string.Empty);
            _detailType.Text = desc.TypeName + " / " + (desc.ReadOnly ? "只读" : "可写") +
                               (desc.Persist ? " / 掉电保存" : " / 运行期");
            _detailRange.Text = "[" + desc.Min + " .. " + desc.Max + "]";
            _detailDefault.Text = desc.Default.ToString();
            _detailCurrent.Text = info != null ? info.FormatValue(desc.Value) + "（" + desc.Value + "）" : desc.Value.ToString();
            _detailDesc.Text = (desc.Description.Length > 0 ? desc.Description : string.Empty) +
                               (info != null && info.DescriptionZh.Length > 0
                                   ? "\r\n" + info.DescriptionZh
                                   : string.Empty);
            _detailValue.Minimum = ClampToDecimal(desc.Min);
            _detailValue.Maximum = ClampToDecimal(desc.Max);
            _detailValue.Value = ClampToDecimal(desc.Value);
            _detailValue.Enabled = !desc.ReadOnly;
            _writeButton.Enabled = !desc.ReadOnly && _shell.IsConnected;
        }

        private static decimal ClampToDecimal(int value)
        {
            decimal minimum = -2147483648M;
            decimal maximum = 2147483647M;
            if (value < minimum)
            {
                return minimum;
            }
            if (value > maximum)
            {
                return maximum;
            }
            return value;
        }

        private void OnCellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.ColumnIndex != 2 || e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
            {
                return;
            }
            _editingOriginal = Convert.ToString(_grid.Rows[e.RowIndex].Cells[2].Value);
        }

        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_updating || e.ColumnIndex != 2 || e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
            {
                return;
            }
            DataGridViewRow row = _grid.Rows[e.RowIndex];
            ParamDesc desc = row.Tag as ParamDesc;
            if (desc == null || desc.ReadOnly)
            {
                return;
            }
            string text = Convert.ToString(row.Cells[2].Value);
            ParamInfo info = ParamCatalog.Find(desc.Id);
            int value;
            if (!TryParseValue(text, desc, out value))
            {
                MessageBox.Show(this, "输入值无法解析: " + text, "参数格式错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                row.Cells[2].Value = _editingOriginal;
                return;
            }
            if (value < desc.Min || value > desc.Max)
            {
                MessageBox.Show(this, string.Format("参数 {0} 超出范围：允许 [{1} .. {2}]，输入 {3}",
                    desc.Name, desc.Min, desc.Max, value), "参数越界",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                row.Cells[2].Value = _editingOriginal;
                return;
            }
            WriteParameter(desc, value);
            row.Cells[2].Value = info != null ? info.FormatValue(desc.Value) : desc.Value.ToString();
        }

        private static bool TryParseValue(string text, ParamDesc desc, out int value)
        {
            value = 0;
            if (text == null)
            {
                return false;
            }
            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                uint parsed;
                if (!uint.TryParse(trimmed.Substring(2), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    return false;
                }
                value = (int)parsed;
                return true;
            }
            /* allow a trailing unit / parenthesised raw value, e.g. "52.0 V" or "关闭 (0)" */
            int bracket = trimmed.LastIndexOf('(');
            if (bracket > 0 && trimmed.EndsWith(")", StringComparison.Ordinal))
            {
                string inner = trimmed.Substring(bracket + 1, trimmed.Length - bracket - 2).Trim();
                if (int.TryParse(inner, out value))
                {
                    return true;
                }
            }
            string firstToken = trimmed.Split(' ')[0];
            ParamInfo info = ParamCatalog.Find(desc.Id);
            if (info != null && Math.Abs(info.Scale - 1.0) > 1e-9)
            {
                double scaled;
                if (double.TryParse(firstToken, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out scaled))
                {
                    double raw = scaled / info.Scale;
                    if (Math.Abs(raw - Math.Round(raw)) > 1e-6)
                    {
                        return false;
                    }
                    value = (int)Math.Round(raw);
                    return true;
                }
            }
            if (int.TryParse(firstToken, out value))
            {
                return true;
            }
            double plain;
            if (double.TryParse(firstToken, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out plain))
            {
                value = (int)Math.Round(plain);
                return true;
            }
            return false;
        }

        private void WriteParameter(ParamDesc desc, int value)
        {
            if (!_shell.IsConnected)
            {
                _statusLabel.Text = "未连接设备，无法写入参数";
                _statusLabel.ForeColor = Theme.Error;
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                _shell.Client.SetParam(desc.Id, value);
                desc.Value = value;
                ParamInfo info = ParamCatalog.Find(desc.Id);
                _statusLabel.Text = string.Format("参数 {0} 已写入: {1}", desc.Name,
                    info != null ? info.FormatValue(value) : value.ToString());
                _statusLabel.ForeColor = Theme.Ok;
                _detailCurrent.Text = info != null
                    ? info.FormatValue(value) + "（" + value + "）"
                    : value.ToString();
                _detailValue.Value = ClampToDecimal(value);
                _shell.LogInfo("参数", string.Format("{0} = {1}", desc.Name, value));
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "写入失败: " + ex.Message;
                _statusLabel.ForeColor = Theme.Error;
                _shell.LogError("参数", string.Format("写入 {0} 失败: {1}", desc.Name, ex.Message));
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OnWriteDetailClicked(object sender, EventArgs e)
        {
            if (_selected == null)
            {
                return;
            }
            int value = (int)_detailValue.Value;
            if (value < _selected.Min || value > _selected.Max)
            {
                MessageBox.Show(this, "新值超出允许范围。", "参数越界",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            WriteParameter(_selected, value);
            RebuildRows();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _shell.ConnectionChanged -= OnConnectionChanged;
                _shell.ParametersChanged -= OnParametersChanged;
            }
            base.Dispose(disposing);
        }
    }
}
