using System;
using System.Windows.Forms;

namespace WindowLabels
{
    internal sealed class WindowPickerButton : Button
    {
        private bool picking;

        internal event Action<WindowCandidate> WindowPicked;

        internal WindowPickerButton()
        {
            Text = "拖到目标窗口";
            AccessibleDescription = "按住按钮，把鼠标拖到应用窗口后松开。也可以从窗口列表选择。";
            TabStop = false;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            Capture = true;
            picking = Capture;
            if (picking)
                Cursor.Current = Cursors.Cross;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !picking)
            {
                base.OnMouseUp(e);
                return;
            }

            var screenPoint = Cursor.Position;
            StopPicking();
            base.OnMouseUp(e);
            Capture = false;

            var handler = WindowPicked;
            if (handler != null)
                handler(NativeWindows.GetCandidateAt(screenPoint));
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (picking && !Capture)
                StopPicking();
            base.OnMouseCaptureChanged(e);
        }

        private void StopPicking()
        {
            picking = false;
            Cursor.Current = Cursor;
        }
    }
}
