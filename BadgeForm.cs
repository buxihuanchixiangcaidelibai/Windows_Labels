using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowLabels
{
    internal sealed class BadgeForm : Form
    {
        private const int WmNcHitTest = 0x0084;
        private const int WmMouseActivate = 0x0021;
        private const int HtTransparent = -1;
        private const int MaNoActivate = 3;
        private const uint LayerAlpha = 0x00000002;
        private const uint NoActivate = 0x0010;
        private const uint NoOwnerZOrder = 0x0200;

        private static readonly IntPtr TopmostWindow = new IntPtr(-1);
        private static readonly Color TextColor = Color.FromArgb(17, 23, 34);

        private readonly StringFormat textFormat = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };

        private string label = "窗口标签";
        private Color accent = Color.FromArgb(90, 190, 245);
        private Font badgeFont;
        private int badgeDpi = 96;
        private int preferredWidth;
        private bool layerReady;

        internal BadgeForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Blend(accent, Color.White, .12f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= NativeWindows.ExtendedTransparent | NativeWindows.ExtendedToolWindow
                    | NativeWindows.ExtendedLayered | NativeWindows.ExtendedNoActivate;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // WS_EX_LAYERED needs explicit composition attributes before the window can be seen.
            layerReady = SetLayeredWindowAttributes(Handle, 0, 255, LayerAlpha);
        }

        internal bool TryShowAt(Rectangle placement)
        {
            if (placement.Width <= 0 || placement.Height <= 0)
                return false;

            IntPtr handle = Handle;
            if (!layerReady)
                return false;

            if (!Visible || Bounds != placement)
            {
                if (!PositionWindow(handle, placement))
                    return false;
                if (!Visible) Show();
                // Showing on another DPI may deliver a suggested window position.
                if (Bounds != placement && !PositionWindow(handle, placement))
                    return false;
            }

            return IsWindowVisible(handle);
        }

        internal void ApplyStyle(string label, Color color)
        {
            label = label ?? string.Empty;
            if (this.label != label)
            {
                this.label = label;
                preferredWidth = 0;
            }

            accent = color.GetBrightness() < .36f ? Blend(color, Color.White, .55f) : color;
            BackColor = Blend(accent, Color.White, .12f);
            Invalidate();
        }

        internal Rectangle GetPlacement(Rectangle targetBounds, int corner, int dpi)
        {
            UpdateDpi(dpi > 0 ? dpi : 96);
            int margin = ScalePixels(8);
            int height = ScalePixels(28);
            int minimumWidth = ScalePixels(52);
            if (targetBounds.Width < minimumWidth + 2 * margin || targetBounds.Height < height + 2 * margin)
                return Rectangle.Empty;

            bool alignRight = corner == 0 || corner == 2;
            bool alignTop = corner == 0 || corner == 1;
            int horizontalInset = margin;
            int verticalInset = margin;

            if (corner == 0 && targetBounds.Width >= ScalePixels(500))
            {
                horizontalInset = ScalePixels(targetBounds.Width >= ScalePixels(900) ? 218 : 144);
                verticalInset = ScalePixels(4);
            }
            else if (alignTop)
            {
                verticalInset = Math.Min(ScalePixels(32), targetBounds.Height - height - margin);
            }

            int width = Math.Min(MeasurePreferredWidth(), targetBounds.Width - horizontalInset - margin);
            int x = alignRight ? targetBounds.Right - width - horizontalInset : targetBounds.Left + horizontalInset;
            int y = alignTop ? targetBounds.Top + verticalInset : targetBounds.Bottom - height - verticalInset;
            return new Rectangle(x, y, width, height);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateShape();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (badgeFont == null || ClientSize.Width < 2 || ClientSize.Height < 2)
                return;

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            float strokeWidth = Math.Max(1f, badgeDpi / 96f);
            RectangleF outline = new RectangleF(
                strokeWidth / 2, strokeWidth / 2, ClientSize.Width - strokeWidth, ClientSize.Height - strokeWidth);
            using (GraphicsPath path = RoundedRectangle(outline, ScalePixels(7)))
            using (Pen pen = new Pen(Blend(TextColor, accent, .55f), strokeWidth))
                graphics.DrawPath(pen, path);

            int dotSize = ScalePixels(8);
            using (SolidBrush brush = new SolidBrush(TextColor))
                graphics.FillEllipse(brush, ScalePixels(10), (ClientSize.Height - dotSize) / 2f, dotSize, dotSize);

            int textLeft = ScalePixels(25);
            RectangleF textBounds = new RectangleF(textLeft, 0,
                ClientSize.Width - textLeft - ScalePixels(9), ClientSize.Height);
            using (SolidBrush brush = new SolidBrush(TextColor))
                graphics.DrawString(label, badgeFont, brush, textBounds, textFormat);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmNcHitTest)
            {
                message.Result = new IntPtr(HtTransparent);
                return;
            }

            if (message.Msg == WmMouseActivate)
            {
                message.Result = new IntPtr(MaNoActivate);
                return;
            }

            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (badgeFont != null)
                    badgeFont.Dispose();
                textFormat.Dispose();
            }

            base.Dispose(disposing);
        }

        private void UpdateDpi(int dpi)
        {
            if (badgeFont != null && badgeDpi == dpi)
                return;

            if (badgeFont != null)
                badgeFont.Dispose();
            badgeDpi = dpi;
            badgeFont = new Font("Microsoft YaHei UI", 14f * badgeDpi / 96f, FontStyle.Bold, GraphicsUnit.Pixel);
            preferredWidth = 0;
            UpdateShape();
            Invalidate();
        }

        private int MeasurePreferredWidth()
        {
            if (preferredWidth > 0)
                return preferredWidth;

            using (Bitmap bitmap = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                SizeF textSize = graphics.MeasureString(label, badgeFont, ScalePixels(300), textFormat);
                preferredWidth = Math.Max(ScalePixels(64), Math.Min(ScalePixels(300),
                    (int)Math.Ceiling(textSize.Width) + ScalePixels(37)));
            }

            return preferredWidth;
        }

        private void UpdateShape()
        {
            if (ClientSize.Width < 2 || ClientSize.Height < 2)
                return;

            using (GraphicsPath shape = RoundedRectangle(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), ScalePixels(7)))
                Region = new Region(shape);
        }

        private int ScalePixels(int pixels)
        {
            return (int)Math.Round(pixels * badgeDpi / 96f);
        }

        private static bool PositionWindow(IntPtr handle, Rectangle placement)
        {
            return SetWindowPos(handle, TopmostWindow, placement.X, placement.Y,
                placement.Width, placement.Height, NoActivate | NoOwnerZOrder);
        }

        private static GraphicsPath RoundedRectangle(RectangleF rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Blend(Color background, Color foreground, float foregroundWeight)
        {
            return Color.FromArgb(
                (int)Math.Round(background.R + (foreground.R - background.R) * foregroundWeight),
                (int)Math.Round(background.G + (foreground.G - background.G) * foregroundWeight),
                (int)Math.Round(background.B + (foreground.B - background.B) * foregroundWeight));
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr handle, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);
    }
}
