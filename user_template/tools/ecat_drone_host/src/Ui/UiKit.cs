/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * UiKit.cs - theme + small programmatic WinForms widgets.
 *
 * The whole GUI is built in code (no designer files) so that it can be compiled
 * with the standalone .NET Framework csc.exe on a machine without Visual Studio.
 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EcatDroneHost.Ui
{
    /// <summary>Colors and fonts of the tool.</summary>
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(243, 245, 248);
        public static readonly Color Panel = Color.White;
        public static readonly Color Accent = Color.FromArgb(25, 118, 210);
        public static readonly Color Ok = Color.FromArgb(46, 125, 50);
        public static readonly Color Warn = Color.FromArgb(239, 108, 0);
        public static readonly Color Error = Color.FromArgb(198, 40, 40);
        public static readonly Color Border = Color.FromArgb(208, 213, 221);
        public static readonly Color Text = Color.FromArgb(33, 37, 41);
        public static readonly Color SubText = Color.FromArgb(96, 106, 120);
        public static readonly Color HintBackground = Color.FromArgb(255, 248, 225);
        public static readonly Color HintBorder = Color.FromArgb(255, 224, 130);
        public static readonly Color WarningBackground = Color.FromArgb(255, 235, 238);
        public static readonly Color WarningBorder = Color.FromArgb(239, 154, 154);
        public static readonly Color GridAlt = Color.FromArgb(248, 250, 252);

        public static readonly Font Body = new Font("Microsoft YaHei UI", 9F);
        public static readonly Font BodyBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        public static readonly Font Small = new Font("Microsoft YaHei UI", 8F);
        public static readonly Font Title = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        public static readonly Font Big = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold);
        public static readonly Font Mono = new Font("Consolas", 9F);

        public static void Apply(Form form)
        {
            form.BackColor = Background;
            form.Font = Body;
            form.ForeColor = Text;
        }
    }

    public enum LightState
    {
        Off,
        Ok,
        Warn,
        Error,
        Busy
    }

    /// <summary>Small colored status light + caption.</summary>
    public sealed class StatusLight : Control
    {
        private LightState _state = LightState.Off;
        private string _caption = "未连接";

        public StatusLight()
        {
            DoubleBuffered = true;
            Height = 26;
            Width = 260;
            Font = Theme.BodyBold;
            ForeColor = Theme.Text;
        }

        public LightState State
        {
            get { return _state; }
            set { _state = value; Invalidate(); }
        }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value ?? string.Empty; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color color;
            switch (_state)
            {
                case LightState.Ok: color = Theme.Ok; break;
                case LightState.Warn: color = Theme.Warn; break;
                case LightState.Error: color = Theme.Error; break;
                case LightState.Busy: color = Theme.Accent; break;
                default: color = Color.FromArgb(158, 158, 158); break;
            }
            int diameter = Math.Min(14, Height - 8);
            g.FillEllipse(new SolidBrush(color), 2, (Height - diameter) / 2, diameter, diameter);
            g.DrawEllipse(new Pen(Color.FromArgb(90, 0, 0, 0)), 2, (Height - diameter) / 2, diameter, diameter);
            TextRenderer.DrawText(g, _caption, Font, new Point(diameter + 8, (Height - Font.Height) / 2),
                ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Horizontal bar meter (used for eRPM / voltage / current).</summary>
    public sealed class BarMeter : Control
    {
        private double _value;
        private double _minimum;
        private double _maximum = 100;
        private string _caption = string.Empty;
        private string _unit = string.Empty;
        private string _valueText = string.Empty;
        private Color _barColor = Theme.Accent;

        public BarMeter()
        {
            DoubleBuffered = true;
            Height = 42;
            Font = Theme.Body;
        }

        public double Value
        {
            get { return _value; }
            set { _value = value; Invalidate(); }
        }

        public double Minimum
        {
            get { return _minimum; }
            set { _minimum = value; Invalidate(); }
        }

        public double Maximum
        {
            get { return _maximum; }
            set { _maximum = value <= 0 ? 1 : value; Invalidate(); }
        }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value ?? string.Empty; Invalidate(); }
        }

        public string Unit
        {
            get { return _unit; }
            set { _unit = value ?? string.Empty; Invalidate(); }
        }

        /// <summary>Override the text drawn on the right (empty = automatic).</summary>
        public string ValueText
        {
            get { return _valueText; }
            set { _valueText = value ?? string.Empty; Invalidate(); }
        }

        public Color BarColor
        {
            get { return _barColor; }
            set { _barColor = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            string valueText = _valueText;
            if (valueText.Length == 0)
            {
                valueText = _value.ToString("F0") + (_unit.Length > 0 ? " " + _unit : string.Empty);
            }
            TextRenderer.DrawText(g, _caption, Theme.Body, new Rectangle(0, 0, Width, 18),
                Theme.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, valueText, Theme.BodyBold, new Rectangle(0, 0, Width, 18),
                Theme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int trackY = 24;
            int trackHeight = 12;
            Rectangle track = new Rectangle(0, trackY, Math.Max(10, Width - 1), trackHeight);
            using (GraphicsPath path = RoundedPath(track, 6))
            {
                g.FillPath(new SolidBrush(Color.FromArgb(232, 236, 241)), path);
                double span = _maximum - _minimum;
                double ratio = span <= 0 ? 0 : (_value - _minimum) / span;
                if (ratio < 0)
                {
                    ratio = 0;
                }
                if (ratio > 1)
                {
                    ratio = 1;
                }
                int fillWidth = (int)(track.Width * ratio);
                if (fillWidth > 0)
                {
                    Rectangle fill = new Rectangle(track.X, track.Y, fillWidth, track.Height);
                    using (GraphicsPath fillPath = RoundedPath(fill, 6))
                    {
                        g.FillPath(new SolidBrush(_barColor), fillPath);
                    }
                }
                g.DrawPath(new Pen(Theme.Border), path);
            }
        }

        private static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            if (rect.Width <= diameter || rect.Height <= diameter)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return path;
            }
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>Two column key / value grid with update-by-key support.</summary>
    public sealed class KvPanel : TableLayoutPanel
    {
        private readonly Dictionary<string, Label> _values = new Dictionary<string, Label>();

        public KvPanel()
        {
            ColumnCount = 2;
            Dock = DockStyle.Fill;
            Padding = new Padding(6, 4, 6, 4);
            BackColor = Color.Transparent;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
        }

        /// <summary>Append a key / value row and return the value label.</summary>
        public Label Add(string key, string initialValue)
        {
            int row = RowCount;
            RowCount = row + 1;
            RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            Label keyLabel = new Label();
            keyLabel.Text = key;
            keyLabel.Dock = DockStyle.Fill;
            keyLabel.TextAlign = ContentAlignment.MiddleLeft;
            keyLabel.ForeColor = Theme.SubText;
            keyLabel.Font = Theme.Body;
            keyLabel.AutoEllipsis = true;

            Label valueLabel = new Label();
            valueLabel.Text = initialValue ?? "-";
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.ForeColor = Theme.Text;
            valueLabel.Font = Theme.BodyBold;
            valueLabel.AutoEllipsis = true;

            Controls.Add(keyLabel, 0, row);
            Controls.Add(valueLabel, 1, row);
            _values[key] = valueLabel;
            return valueLabel;
        }

        public void Set(string key, string text)
        {
            Label label;
            if (_values.TryGetValue(key, out label))
            {
                label.Text = text ?? "-";
            }
        }

        public void SetColor(string key, Color color)
        {
            Label label;
            if (_values.TryGetValue(key, out label))
            {
                label.ForeColor = color;
            }
        }

        public Label Get(string key)
        {
            Label label;
            return _values.TryGetValue(key, out label) ? label : null;
        }
    }

    /// <summary>Programmatic widget factory + formatting helpers.</summary>
    public static class UiKit
    {
        /* ------------------------------ containers ----------------------------- */

        public static TableLayoutPanel NewLayout(int columns)
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.ColumnCount = columns;
            table.BackColor = Color.Transparent;
            return table;
        }

        public static GroupBox NewGroup(string title)
        {
            GroupBox box = new GroupBox();
            box.Text = " " + title + " ";
            box.Font = Theme.BodyBold;
            box.ForeColor = Theme.Text;
            box.BackColor = Theme.Panel;
            box.Padding = new Padding(8, 6, 8, 8);
            box.Dock = DockStyle.Fill;
            return box;
        }

        public static Panel NewCard()
        {
            Panel panel = new Panel();
            panel.BackColor = Theme.Panel;
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.Padding = new Padding(8);
            panel.Dock = DockStyle.Fill;
            return panel;
        }

        public static Label NewSectionHeader(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = Theme.BodyBold;
            label.ForeColor = Theme.Accent;
            label.AutoSize = false;
            label.Height = 24;
            label.Dock = DockStyle.Top;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        public static Label NewHint(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = Theme.Small;
            label.ForeColor = Theme.SubText;
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        public static Label NewWarningBanner(string text)
        {
            Label label = new Label();
            label.Text = "  ⚠  " + text;
            label.Font = Theme.Body;
            label.ForeColor = Color.FromArgb(120, 60, 0);
            label.BackColor = Theme.HintBackground;
            label.BorderStyle = BorderStyle.FixedSingle;
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        public static Label NewDangerBanner(string text)
        {
            Label label = new Label();
            label.Text = "  ⚠  " + text;
            label.Font = Theme.BodyBold;
            label.ForeColor = Color.FromArgb(140, 20, 20);
            label.BackColor = Theme.WarningBackground;
            label.BorderStyle = BorderStyle.FixedSingle;
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        /* ------------------------------- controls ------------------------------ */

        public static Label NewLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Height = 22;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = Theme.Text;
            label.Font = Theme.Body;
            label.Dock = DockStyle.Fill;
            return label;
        }

        public static Label NewKeyLabel(string text)
        {
            Label label = NewLabel(text);
            label.ForeColor = Theme.SubText;
            return label;
        }

        public static Label NewValueLabel(string text)
        {
            Label label = NewLabel(text);
            label.Font = Theme.BodyBold;
            return label;
        }

        public static Button NewButton(string text, EventHandler onClick)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = false;
            button.Height = 28;
            button.Width = Math.Max(78, TextRenderer.MeasureText(text, Theme.Body).Width + 22);
            button.Font = Theme.Body;
            button.BackColor = Color.FromArgb(250, 251, 252);
            button.FlatStyle = FlatStyle.System;
            if (onClick != null)
            {
                button.Click += onClick;
            }
            return button;
        }

        public static Button NewPrimaryButton(string text, EventHandler onClick)
        {
            Button button = NewButton(text, onClick);
            button.Font = Theme.BodyBold;
            return button;
        }

        public static NumericUpDown NewNumeric(decimal minimum, decimal maximum, decimal value)
        {
            NumericUpDown numeric = new NumericUpDown();
            numeric.Minimum = minimum;
            numeric.Maximum = maximum;
            numeric.Value = Math.Min(Math.Max(value, minimum), maximum);
            numeric.Font = Theme.Body;
            numeric.Height = 24;
            numeric.Width = 96;
            numeric.TextAlign = HorizontalAlignment.Right;
            return numeric;
        }

        public static ComboBox NewCombo()
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Font = Theme.Body;
            combo.Height = 24;
            return combo;
        }

        public static ComboBox NewCombo(string[] items, int selectedIndex)
        {
            ComboBox combo = NewCombo();
            if (items != null)
            {
                combo.Items.AddRange(items);
            }
            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = Math.Min(Math.Max(selectedIndex, 0), combo.Items.Count - 1);
            }
            return combo;
        }

        public static TextBox NewTextBox(string text, bool readOnly)
        {
            TextBox box = new TextBox();
            box.Text = text;
            box.ReadOnly = readOnly;
            box.Font = readOnly ? Theme.Body : Theme.Body;
            box.Height = 24;
            if (readOnly)
            {
                box.BackColor = Color.FromArgb(248, 249, 251);
            }
            return box;
        }

        public static TextBox NewHexTextBox(string text)
        {
            TextBox box = NewTextBox(text, false);
            box.Font = Theme.Mono;
            return box;
        }

        public static CheckBox NewCheck(string text, bool isChecked)
        {
            CheckBox check = new CheckBox();
            check.Text = text;
            check.Checked = isChecked;
            check.AutoSize = true;
            check.Font = Theme.Body;
            check.ForeColor = Theme.Text;
            return check;
        }

        public static TrackBar NewSlider(int minimum, int maximum, int value)
        {
            TrackBar bar = new TrackBar();
            bar.Minimum = minimum;
            bar.Maximum = maximum;
            bar.Value = Math.Min(Math.Max(value, minimum), maximum);
            bar.TickFrequency = Math.Max(1, (maximum - minimum) / 10);
            bar.SmallChange = 1;
            bar.LargeChange = 10;
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = false;
            bar.Height = 34;
            return bar;
        }

        public static DataGridView NewGrid()
        {
            DataGridView grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.BackgroundColor = Theme.Panel;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Color.FromArgb(228, 232, 238);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 242, 247);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
            grid.ColumnHeadersDefaultCellStyle.Font = Theme.BodyBold;
            grid.ColumnHeadersHeight = 28;
            grid.DefaultCellStyle.Font = Theme.Body;
            grid.DefaultCellStyle.BackColor = Theme.Panel;
            grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 233, 250);
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.RowTemplate.Height = 24;
            grid.AutoGenerateColumns = false;
            return grid;
        }

        public static RichTextBox NewLogBox()
        {
            RichTextBox box = new RichTextBox();
            box.Dock = DockStyle.Fill;
            box.ReadOnly = true;
            box.BackColor = Color.FromArgb(30, 32, 36);
            box.ForeColor = Color.FromArgb(220, 224, 228);
            box.Font = Theme.Mono;
            box.BorderStyle = BorderStyle.None;
            box.WordWrap = false;
            box.ScrollBars = RichTextBoxScrollBars.Both;
            box.DetectUrls = false;
            return box;
        }

        public static RichTextBox NewHexBox()
        {
            RichTextBox box = new RichTextBox();
            box.Dock = DockStyle.Fill;
            box.ReadOnly = true;
            box.BackColor = Color.White;
            box.ForeColor = Theme.Text;
            box.Font = Theme.Mono;
            box.BorderStyle = BorderStyle.None;
            box.WordWrap = false;
            box.ScrollBars = RichTextBoxScrollBars.Both;
            box.DetectUrls = false;
            return box;
        }

        /// <summary>Add a control to a table layout panel cell.</summary>
        public static void Place(TableLayoutPanel table, Control control, int column, int row)
        {
            control.Dock = DockStyle.Fill;
            table.Controls.Add(control, column, row);
        }

        /// <summary>Place a group box spanning columns.</summary>
        public static void PlaceSpan(TableLayoutPanel table, Control control, int column, int row, int span)
        {
            control.Dock = DockStyle.Fill;
            table.Controls.Add(control, column, row);
            table.SetColumnSpan(control, span);
        }

        /* ------------------------------- dialogs ------------------------------- */

        public static string PickSaveFile(IWin32Window owner, string title, string filter, string defaultName)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = title;
                dialog.Filter = filter;
                dialog.FileName = defaultName;
                dialog.OverwritePrompt = true;
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        public static string PickOpenFile(IWin32Window owner, string title, string filter)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = title;
                dialog.Filter = filter;
                dialog.CheckFileExists = true;
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        /* ------------------------------ formatting ----------------------------- */

        public static string Fixed(double value, int decimals)
        {
            return value.ToString("F" + decimals);
        }

        public static string Percent(double ratio)
        {
            return (ratio * 100.0).ToString("F0") + " %";
        }

        public static void StyleValueCell(Label label, Color color, bool bold)
        {
            if (label == null)
            {
                return;
            }
            label.ForeColor = color;
            label.Font = bold ? Theme.BodyBold : Theme.Body;
        }
    }
}
