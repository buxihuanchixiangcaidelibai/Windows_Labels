using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EventWaitHandle = System.Threading.EventWaitHandle;
using EventResetMode = System.Threading.EventResetMode;

namespace WindowLabels
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            NativeWindows.InstallDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool firstInstance;
            using (var reopen = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WindowLabels.OpenPanel", out firstInstance))
            {
                if (!firstInstance)
                {
                    reopen.Set();
                    return;
                }
                using (var panel = new LabelsPanel(reopen))
                {
                    Application.Run(panel);
                }
            }
        }
    }

    internal sealed partial class LabelsPanel : Form
    {
        private const int HotkeyId = 1;
        private const int WmHotkey = 0x0312;
        private const uint Alt = 0x0001;
        private const uint Control = 0x0002;
        private const uint Shift = 0x0004;
        private const uint NoRepeat = 0x4000;
        private readonly Dictionary<IntPtr, OpenLabel> labels = new Dictionary<IntPtr, OpenLabel>();
        private readonly Timer tracking = new Timer { Interval = 100 };
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem pauseCommand = new ToolStripMenuItem("暂停显示标签");
        private IntPtr hotkeyWindow;
        private bool paused;
        private bool exiting;
        private bool refreshing;
        private int selectedColor;

        internal LabelsPanel(EventWaitHandle reopen)
        {
            BuildInterface();
            trayMenu.Items.Add("打开窗口标签", null, (s, e) => OpenPanel(null));
            pauseCommand.Click += (s, e) => SetPaused(!paused);
            trayMenu.Items.Add(pauseCommand);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出", null, (s, e) => Exit());
            tray.Text = "窗口标签";
            tray.Icon = Icon;
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += (s, e) => OpenPanel(null);
            tracking.Tick += (s, e) =>
            {
                if (reopen.WaitOne(0)) OpenPanel(null);
                UpdateBadges();
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            tray.Visible = true;
            RefreshWindows(IntPtr.Zero, false);
            tracking.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            string shortcut = RegisterShortcut();
            shortcutHint.Text = shortcut ?? "请拖动选窗";
            intro.Text = "从列表中选择窗口，或将「拖到目标窗口」按钮拖到要标记的应用上。";
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (hotkeyWindow != IntPtr.Zero)
            {
                UnregisterHotKey(hotkeyWindow, HotkeyId);
                hotkeyWindow = IntPtr.Zero;
            }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
            {
                OpenPanel(NativeWindows.GetForegroundCandidate());
                return;
            }
            base.WndProc(ref m);
        }

        private string RegisterShortcut()
        {
            var window = Handle;
            string shortcut;
            if (RegisterHotKey(window, HotkeyId, NoRepeat | Control | Alt, (uint)Keys.T))
                shortcut = "Ctrl + Alt + T";
            else if (RegisterHotKey(window, HotkeyId, NoRepeat | Control | Alt | Shift, (uint)Keys.T))
                shortcut = "Ctrl + Alt + Shift + T";
            else if (RegisterHotKey(window, HotkeyId, NoRepeat | Control | Shift, (uint)Keys.F9))
                shortcut = "Ctrl + Shift + F9";
            else
                return null;
            hotkeyWindow = window;
            return shortcut;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                tracking.Dispose();
                foreach (var label in labels.Values) label.Dispose();
                labels.Clear();
                tray.Dispose();
                trayMenu.Dispose();
                tips.Dispose();
            }
            base.Dispose(disposing);
        }

        private void Exit()
        {
            exiting = true;
            Close();
        }

        private void OpenPanel(WindowCandidate target)
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            RefreshWindows(target == null ? IntPtr.Zero : target.Handle, target == null, target);
            Show();
            Activate();
            if (target != null)
            {
                labelInput.Focus();
                labelInput.SelectAll();
            }
        }

        private void SelectPickedWindow(WindowCandidate target)
        {
            if (target == null)
            {
                SetStatus("请按住按钮拖到目标应用的可见区域，再松开鼠标。", true);
                return;
            }
            OpenPanel(target);
            SetStatus("已选中「" + target.Title + "」。");
        }

        private WindowCandidate SelectedWindow
        {
            get
            {
                return windowList.SelectedRows.Count == 0 ? null : windowList.SelectedRows[0].Tag as WindowCandidate;
            }
        }

        private void RefreshWindows(IntPtr preferred, bool preserveDraft, WindowCandidate foreground = null)
        {
            var previous = SelectedWindow;
            RemoveClosedLabels();
            var candidates = NativeWindows.EnumerateCandidates();
            if (foreground != null && !candidates.Exists(c => c.Handle == foreground.Handle))
                candidates.Insert(0, foreground);

            IntPtr wanted = preferred != IntPtr.Zero ? preferred : previous == null ? IntPtr.Zero : previous.Handle;
            DataGridViewRow selectedRow = null;
            refreshing = true;
            windowList.SuspendLayout();
            try
            {
                windowList.Rows.Clear();
                foreach (var candidate in candidates)
                {
                    OpenLabel label;
                    labels.TryGetValue(candidate.Handle, out label);
                    int index = windowList.Rows.Add(candidate.Title, ProcessDescription(candidate), label == null ? "" : label.Text);
                    var row = windowList.Rows[index];
                    row.Tag = candidate;
                    row.Cells[0].ToolTipText = candidate.Title;
                    SetRowLabel(row, label);
                    if (candidate.Handle == wanted) selectedRow = row;
                }
                if (selectedRow == null && windowList.Rows.Count == 1) selectedRow = windowList.Rows[0];
                windowList.CurrentCell = selectedRow == null ? null : selectedRow.Cells[0];
                windowList.ClearSelection();
                if (selectedRow != null) selectedRow.Selected = true;
            }
            finally
            {
                windowList.ResumeLayout();
                refreshing = false;
            }

            var selected = SelectedWindow;
            if (!preserveDraft || previous == null || selected == null || previous.Handle != selected.Handle || previous.ProcessId != selected.ProcessId)
                LoadSelection();
            SetStatus(candidates.Count == 0 ? "未找到打开的应用窗口。打开目标窗口后点「刷新」。" : "找到 " + candidates.Count + " 个窗口。");
        }

        private static string ProcessDescription(WindowCandidate candidate)
        {
            return string.IsNullOrEmpty(candidate.ProcessName) ? "PID " + candidate.ProcessId : candidate.ProcessName + " · " + candidate.ProcessId;
        }

        private void LoadSelection()
        {
            if (refreshing) return;
            var selected = SelectedWindow;
            OpenLabel label = null;
            if (selected != null) labels.TryGetValue(selected.Handle, out label);
            editor.Enabled = selected != null;
            locateButton.Enabled = selected != null;
            selectedTitle.Text = selected == null ? "请先选择目标窗口" : selected.Title;
            selectedProcess.Text = selected == null ? "" : ProcessDescription(selected);
            labelInput.Text = label == null ? "" : label.Text;
            ChooseColor(label == null ? NextColor() : label.ColorIndex);
            cornerInput.SelectedIndex = label == null ? 0 : label.Corner;
            UpdateEditorButtons(label);
            ShowLabelState(label);
        }

        private int NextColor()
        {
            var inUse = new bool[Palette.Length];
            foreach (var label in labels.Values) inUse[label.ColorIndex] = true;
            int free = Array.IndexOf(inUse, false);
            return free < 0 ? labels.Count % Palette.Length : free;
        }

        private void UpdateEditorButtons(OpenLabel label)
        {
            saveButton.Text = label == null ? "贴上并查看" : "保存并查看";
            removeButton.Enabled = label != null;
        }

        private void SaveLabel()
        {
            var target = SelectedWindow;
            if (target == null) return;
            string text = labelInput.Text.Trim();

            OpenLabel label;
            if (labels.TryGetValue(target.Handle, out label) && !NativeWindows.IsAlive(label.Binding))
            {
                RemoveLabel(label);
                RefreshWindows(IntPtr.Zero, false);
                SetStatus("原窗口已关闭，请重新选择要贴标签的窗口。", true);
                return;
            }
            if (label == null)
            {
                WindowBinding binding;
                WindowBindingError error;
                if (!NativeWindows.TryBind(target, out binding, out error))
                {
                    string message;
                    if (error == WindowBindingError.AccessDenied)
                        message = "权限不足，请用和目标应用相同的权限打开本工具。";
                    else if (error == WindowBindingError.WindowClosed)
                        message = "窗口已关闭，请刷新列表后重新选择。";
                    else
                        message = "Windows 拒绝绑定，请刷新列表后重试。";
                    SetStatus(message, true);
                    labelState.Text = message;
                    labelState.ForeColor = status.ForeColor;
                    tips.SetToolTip(labelState, message);
                    return;
                }
                label = new OpenLabel(binding);
                labels.Add(target.Handle, label);
            }

            if (text.Length == 0) text = label.Text ?? NextLabelName();
            labelInput.Text = text;
            label.Text = text;
            label.ColorIndex = selectedColor;
            label.Corner = cornerInput.SelectedIndex;
            label.Badge.ApplyStyle(text, Palette[selectedColor]);
            SetRowLabel(windowList.SelectedRows[0], label);
            UpdateEditorButtons(label);
            SetStatus(paused ? "已保存「" + text + "」，标签显示已暂停。" : "已保存标签「" + text + "」。");
            if (!paused) ReturnToWindow(target);
            UpdateBadges();
        }

        private string NextLabelName()
        {
            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var label in labels.Values) assigned.Add(label.Text);
            for (int number = 0; ; number++)
            {
                string suffix = number < 26 ? ((char)('A' + number)).ToString() : (number + 1).ToString();
                string name = "窗口 " + suffix;
                if (!assigned.Contains(name)) return name;
            }
        }

        private void RemoveSelectedLabel()
        {
            var selected = SelectedWindow;
            OpenLabel label;
            if (selected == null || !labels.TryGetValue(selected.Handle, out label)) return;
            RemoveLabel(label);
            SetRowLabel(windowList.SelectedRows[0], null);
            LoadSelection();
            SetStatus("已移除标签。");
        }

        private void ReturnToWindow(WindowCandidate target)
        {
            if (NativeWindows.TryActivate(target))
            {
                Hide();
                return;
            }
            SetStatus("无法自动切换，请从任务栏切换到目标窗口。", true);
        }

        private void SetPaused(bool value)
        {
            paused = value;
            pauseCommand.Text = paused ? "恢复显示标签" : "暂停显示标签";
            pauseButton.Text = paused ? "恢复显示" : "暂停显示";
            SetStatus(paused ? "已暂停显示标签。" : "已恢复显示标签。");
            UpdateBadges();
        }

        private void UpdateBadges()
        {
            if (labels.Count == 0) return;
            if (RemoveClosedLabels() && Visible && WindowState != FormWindowState.Minimized)
                RefreshWindows(IntPtr.Zero, true);
            if (labels.Count == 0) return;
            if (paused)
            {
                foreach (var label in labels.Values) HideBadge(label, "显示已暂停");
                RefreshSelectedLabelState();
                return;
            }

            var desktop = NativeWindows.CaptureDesktop(Visible && WindowState != FormWindowState.Minimized ? Handle : IntPtr.Zero);
            foreach (var label in labels.Values)
            {
                UpdateBadge(label, desktop);
            }
            RefreshSelectedLabelState();
        }

        private void UpdateBadge(OpenLabel label, DesktopSnapshot desktop)
        {
            Rectangle target;
            if (!NativeWindows.TryGetVisibleBounds(label.Binding, out target))
            {
                HideBadge(label, "窗口已最小化或不可见");
                return;
            }

            int dpi = NativeWindows.GetDpi(label.Binding);
            for (int offset = 0; offset < CornerNames.Length; offset++)
            {
                int corner = (label.Corner + offset) % CornerNames.Length;
                var placement = label.Badge.GetPlacement(target, corner, dpi);
                if (placement.IsEmpty) continue;
                var screen = Screen.FromRectangle(placement).Bounds;
                if (!screen.Contains(placement))
                    placement = label.Badge.GetPlacement(Rectangle.Intersect(target, screen), corner, dpi);
                if (placement.IsEmpty || !screen.Contains(placement) || !NativeWindows.CanPlaceBadge(desktop, label.Binding, placement)) continue;

                if (label.Badge.TryShowAt(placement))
                    label.Presentation = "标签显示在" + CornerNames[corner];
                else
                    HideBadge(label, "Windows 未能显示浮动标签");
                return;
            }
            HideBadge(label, "窗口被遮挡或可见区域过小");
        }

        private static void HideBadge(OpenLabel label, string reason)
        {
            label.Badge.Hide();
            label.Presentation = reason;
        }

        private void RefreshSelectedLabelState()
        {
            if (!Visible) return;
            var candidate = SelectedWindow;
            OpenLabel label;
            if (candidate != null && labels.TryGetValue(candidate.Handle, out label)) ShowLabelState(label);
        }

        private bool RemoveClosedLabels()
        {
            List<OpenLabel> closed = null;
            foreach (var label in labels.Values)
            {
                if (NativeWindows.IsAlive(label.Binding)) continue;
                if (closed == null) closed = new List<OpenLabel>();
                closed.Add(label);
            }
            if (closed == null) return false;
            foreach (var label in closed) RemoveLabel(label);
            return true;
        }

        private void RemoveLabel(OpenLabel label)
        {
            labels.Remove(label.Binding.Candidate.Handle);
            label.Dispose();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);

        private sealed class OpenLabel : IDisposable
        {
            internal readonly WindowBinding Binding;
            internal readonly BadgeForm Badge = new BadgeForm();
            internal string Text;
            internal int ColorIndex;
            internal int Corner;
            internal string Presentation;

            internal OpenLabel(WindowBinding binding)
            {
                Binding = binding;
            }

            public void Dispose()
            {
                Badge.Dispose();
                NativeWindows.Release(Binding);
            }
        }
    }
}
