using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowLabels
{
    internal sealed partial class LabelsPanel
    {
        private static readonly Color Surface = Color.FromArgb(240, 245, 243);
        private static readonly Color Ink = Color.FromArgb(23, 40, 37);
        private static readonly Color Muted = Color.FromArgb(70, 92, 87);
        private static readonly Color Border = Color.FromArgb(211, 224, 219);
        private static readonly Color Accent = Color.FromArgb(0, 98, 80);
        private static readonly Color Selected = Color.FromArgb(221, 244, 233);
        private static readonly Color[] Palette =
        {
            Color.FromArgb(41, 207, 167), Color.FromArgb(255, 152, 99),
            Color.FromArgb(170, 157, 254), Color.FromArgb(255, 131, 177),
            Color.FromArgb(113, 190, 252), Color.FromArgb(245, 202, 87)
        };
        private static readonly string[] ColorNames = { "青绿", "橙色", "紫色", "粉色", "蓝色", "黄色" };
        private static readonly string[] CornerNames = { "右上角", "左上角", "右下角", "左下角" };

        private readonly Label intro = new Label();
        private readonly Label shortcutHint = new Label();
        private readonly WindowPickerButton picker = new WindowPickerButton();
        private readonly DataGridView windowList = new DataGridView();
        private readonly Panel editor = new Panel();
        private readonly Label selectedTitle = new Label();
        private readonly Label selectedProcess = new Label();
        private readonly TextBox labelInput = new TextBox();
        private readonly Button[] swatches = new Button[Palette.Length];
        private readonly ComboBox cornerInput = new ComboBox();
        private readonly Button saveButton = new Button();
        private readonly Button removeButton = new Button();
        private readonly Button locateButton = new Button();
        private readonly Button pauseButton = new Button();
        private readonly Label labelState = new Label();
        private readonly Label status = new Label();
        private readonly ToolTip tips = new ToolTip();

        private void BuildInterface()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Surface;
            ForeColor = Ink;
            Text = "窗口标签";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(930, 530);
            MinimumSize = new Size(820, 570);
            MaximizeBox = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(24, 17, 24, 6), ColumnCount = 1, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            Controls.Add(layout);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 2 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            var heading = MakeLabel("窗口标签", 22, true, Ink);
            heading.Dock = DockStyle.Fill;
            header.Controls.Add(heading, 0, 0);
            ConfigureLabel(intro, "从列表中选择窗口，或拖动右侧按钮直接选窗。", 10, false, Muted);
            intro.Dock = DockStyle.Fill;
            header.Controls.Add(intro, 0, 1);
            ConfigureLabel(shortcutHint, "Ctrl + Alt + T", 10, true, Accent);
            shortcutHint.Dock = DockStyle.Fill;
            shortcutHint.Margin = new Padding(5, 6, 0, 3);
            shortcutHint.BackColor = Selected;
            shortcutHint.TextAlign = ContentAlignment.MiddleCenter;
            header.Controls.Add(shortcutHint, 1, 0);
            ConfigureButton(picker, "拖到目标窗口", true);
            picker.Dock = DockStyle.Fill;
            picker.Margin = new Padding(5, 0, 0, 0);
            picker.WindowPicked += SelectPickedWindow;
            tips.SetToolTip(picker, "按住按钮，拖到目标窗口的可见区域后松开鼠标。");
            header.Controls.Add(picker, 1, 1);
            layout.Controls.Add(header, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(BuildWindowList(), 0, 0);
            BuildEditor();
            body.Controls.Add(editor, 1, 0);
            layout.Controls.Add(body, 0, 1);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ConfigureLabel(status, "正在读取已打开的窗口…", 9, false, Muted);
            status.Dock = DockStyle.Fill;
            footer.Controls.Add(status, 0, 0);
            var trayHint = MakeLabel("关闭面板后从系统托盘打开", 9, false, Muted);
            trayHint.TextAlign = ContentAlignment.MiddleRight;
            trayHint.Dock = DockStyle.Fill;
            footer.Controls.Add(trayHint, 1, 0);
            layout.Controls.Add(footer, 0, 2);

            ResumeLayout(true);
        }

        private Control BuildWindowList()
        {
            var card = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0), Padding = new Padding(1), BackColor = Border, ColumnCount = 1, RowCount = 2 };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(14, 8, 10, 8), BackColor = Color.White, ColumnCount = 4, RowCount = 1 };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            var title = MakeLabel("打开的窗口", 12, true, Ink);
            title.Dock = DockStyle.Fill;
            bar.Controls.Add(title, 0, 0);
            ConfigureButton(locateButton, "切换到窗口", false);
            locateButton.Dock = DockStyle.Fill;
            locateButton.Click += (s, e) =>
            {
                var candidate = SelectedWindow;
                if (candidate != null) ReturnToWindow(candidate);
            };
            bar.Controls.Add(locateButton, 1, 0);
            ConfigureButton(pauseButton, "暂停显示", false);
            pauseButton.Dock = DockStyle.Fill;
            pauseButton.Click += (s, e) => SetPaused(!paused);
            bar.Controls.Add(pauseButton, 2, 0);
            var refresh = new Button();
            ConfigureButton(refresh, "刷新", false);
            refresh.Dock = DockStyle.Fill;
            refresh.Click += (s, e) => RefreshWindows(IntPtr.Zero, true);
            bar.Controls.Add(refresh, 3, 0);
            card.Controls.Add(bar, 0, 0);

            windowList.Dock = DockStyle.Fill;
            windowList.Margin = Padding.Empty;
            windowList.BackgroundColor = Color.White;
            windowList.BorderStyle = BorderStyle.None;
            windowList.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            windowList.GridColor = Surface;
            windowList.ReadOnly = true;
            windowList.MultiSelect = false;
            windowList.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            windowList.RowHeadersVisible = false;
            windowList.AllowUserToAddRows = false;
            windowList.AllowUserToDeleteRows = false;
            windowList.AllowUserToResizeRows = false;
            windowList.RowTemplate.Height = 37;
            windowList.ColumnHeadersHeight = 33;
            windowList.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            windowList.EnableHeadersVisualStyles = false;
            windowList.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            windowList.ColumnHeadersDefaultCellStyle.BackColor = Surface;
            windowList.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            windowList.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface;
            windowList.ColumnHeadersDefaultCellStyle.SelectionForeColor = Muted;
            windowList.ColumnHeadersDefaultCellStyle.Padding = new Padding(9, 0, 3, 0);
            windowList.DefaultCellStyle.ForeColor = Ink;
            windowList.DefaultCellStyle.SelectionBackColor = Selected;
            windowList.DefaultCellStyle.SelectionForeColor = Ink;
            windowList.DefaultCellStyle.Padding = new Padding(9, 0, 3, 0);
            var nameColumn = GridColumn("窗口标题", 115);
            nameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            windowList.Columns.Add(nameColumn);
            windowList.Columns.Add(GridColumn("进程 · PID", 174));
            windowList.Columns.Add(GridColumn("标签", 98));
            windowList.SelectionChanged += (s, e) => LoadSelection();
            locateButton.Enabled = false;
            card.Controls.Add(windowList, 0, 1);
            return card;
        }

        private void BuildEditor()
        {
            editor.Dock = DockStyle.Fill;
            editor.Margin = Padding.Empty;
            editor.BackColor = Color.White;
            editor.BorderStyle = BorderStyle.FixedSingle;
            editor.Enabled = false;

            AddAt(editor, MakeLabel("标签设置", 11, true, Accent), 18, 13, 278, 25);
            ConfigureLabel(selectedTitle, "请先选择目标窗口", 13, true, Ink);
            AddAt(editor, selectedTitle, 18, 42, 278, 31);
            ConfigureLabel(selectedProcess, "", 9, false, Muted);
            AddAt(editor, selectedProcess, 18, 73, 278, 21);
            AddAt(editor, MakeLabel("标签文字（留空自动编号）", 10, true, Ink), 18, 106, 278, 24);

            labelInput.MaxLength = 18;
            labelInput.AccessibleName = "标签文字";
            AddAt(editor, labelInput, 18, 133, 278, 29);
            tips.SetToolTip(labelInput, "例如 主账号、测试号或项目分支名");
            AddAt(editor, MakeLabel("识别颜色", 10, true, Ink), 18, 174, 278, 24);
            for (int i = 0; i < swatches.Length; i++)
            {
                int index = i;
                var swatch = new Button();
                ConfigureButton(swatch, "", false);
                swatch.AccessibleName = ColorNames[index];
                swatch.BackColor = Palette[index];
                swatch.FlatAppearance.MouseOverBackColor = ControlPaint.Light(Palette[index]);
                swatch.FlatAppearance.MouseDownBackColor = Palette[index];
                swatch.Click += (s, e) => ChooseColor(index);
                swatches[index] = swatch;
                tips.SetToolTip(swatch, ColorNames[index]);
                AddAt(editor, swatch, 18 + i * 47, 200, 42, 31);
            }

            AddAt(editor, MakeLabel("优先显示位置", 10, true, Ink), 18, 237, 278, 24);
            cornerInput.DropDownStyle = ComboBoxStyle.DropDownList;
            cornerInput.AccessibleName = "显示位置";
            cornerInput.Items.AddRange(CornerNames);
            cornerInput.SelectedIndex = 0;
            AddAt(editor, cornerInput, 18, 264, 278, 28);
            ConfigureLabel(labelState, "", 9, false, Muted);
            AddAt(editor, labelState, 18, 294, 278, 25);

            ConfigureButton(saveButton, "贴上并查看", true);
            saveButton.Click += (s, e) => SaveLabel();
            AddAt(editor, saveButton, 18, 325, 163, 38);
            ConfigureButton(removeButton, "移除标签", false);
            removeButton.Click += (s, e) => RemoveSelectedLabel();
            AddAt(editor, removeButton, 191, 325, 105, 38);
        }

        private void ChooseColor(int index)
        {
            selectedColor = index;
            for (int i = 0; i < swatches.Length; i++)
            {
                bool chosen = i == index;
                swatches[i].Text = chosen ? "✓" : "";
                swatches[i].FlatAppearance.BorderColor = chosen ? Ink : Border;
                swatches[i].FlatAppearance.BorderSize = chosen ? 2 : 1;
                swatches[i].AccessibleDescription = chosen ? "已选颜色" : "";
            }
        }

        private void SetStatus(string message, bool error = false)
        {
            status.Text = message;
            status.ForeColor = error ? Color.FromArgb(149, 56, 37) : Muted;
        }

        private void ShowLabelState(OpenLabel label)
        {
            string message = label == null ? "" : label.Presentation ?? "等待显示标签";
            if (labelState.Text != message)
            {
                labelState.Text = message;
                tips.SetToolTip(labelState, message);
            }
            if (labelState.ForeColor != Muted) labelState.ForeColor = Muted;
        }

        private static void SetRowLabel(DataGridViewRow row, OpenLabel label)
        {
            var cell = row.Cells[2];
            cell.Value = label == null ? "" : label.Text;
            cell.ToolTipText = label == null ? "" : label.Text;
            cell.Style.BackColor = label == null ? Color.Empty : Mix(Palette[label.ColorIndex], Color.White, 3);
            cell.Style.SelectionBackColor = label == null ? Color.Empty : Mix(Palette[label.ColorIndex], Color.White, 1);
        }

        private static Color Mix(Color color, Color other, int otherWeight)
        {
            int total = otherWeight + 1;
            return Color.FromArgb((color.R + otherWeight * other.R) / total, (color.G + otherWeight * other.G) / total, (color.B + otherWeight * other.B) / total);
        }

        private static DataGridViewTextBoxColumn GridColumn(string title, int width)
        {
            return new DataGridViewTextBoxColumn { HeaderText = title, Width = width, MinimumWidth = Math.Min(width, 115), SortMode = DataGridViewColumnSortMode.NotSortable };
        }

        private static void AddAt(Control parent, Control child, int x, int y, int w, int h)
        {
            child.SetBounds(x, y, w, h);
            parent.Controls.Add(child);
        }

        private Label MakeLabel(string text, float size, bool bold, Color color)
        {
            var label = new Label();
            ConfigureLabel(label, text, size, bold, color);
            return label;
        }

        private void ConfigureLabel(Label label, string text, float size, bool bold, Color color)
        {
            label.Text = text;
            label.Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular);
            label.ForeColor = color;
            label.Margin = Padding.Empty;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            label.UseMnemonic = false;
        }

        private static void ConfigureButton(Button button, string text, bool primary)
        {
            button.Text = text;
            button.Margin = new Padding(4, 0, 0, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = primary ? Accent : Color.White;
            button.ForeColor = primary ? Color.White : Ink;
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(14, 119, 97) : Surface;
            button.Cursor = Cursors.Hand;
        }
    }
}
