using System;
using System.Drawing;
using System.Windows.Forms;

namespace LightweightZoneManager
{
    /// <summary>
    /// Zone overlay used during drag-and-drop operations with highlight support
    /// </summary>
    public class DragZoneOverlay : Form
    {
        protected override bool ShowWithoutActivation => true;

        private readonly string zoneNumber;
        private readonly int zoneIndex;
        private bool isHighlighted = false;

        private static readonly Color[] normalColors = new Color[]
        {
            Color.LightBlue, Color.LightCoral, Color.LightGreen, Color.Orange,
            Color.Plum, Color.Khaki, Color.Orchid, Color.LightCyan, Color.Pink
        };

        private static readonly Color[] highlightColors = new Color[]
        {
            Color.Blue, Color.Red, Color.Green, Color.DarkOrange, Color.Purple,
            Color.Gold, Color.Magenta, Color.Cyan, Color.HotPink
        };

        public DragZoneOverlay(Rectangle bounds, string number, int index)
        {
            zoneNumber = number;
            zoneIndex = index;

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = bounds;

            int colorIndex = (int.Parse(number) - 1) % normalColors.Length;
            this.BackColor = normalColors[colorIndex];
            this.Opacity = 0.6;

            this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        }

        /// <summary>
        /// Set whether this zone should be highlighted (mouse is over it)
        /// </summary>
        public void SetHighlighted(bool highlighted)
        {
            if (isHighlighted != highlighted)
            {
                isHighlighted = highlighted;

                int colorIndex = (int.Parse(zoneNumber) - 1) % normalColors.Length;
                this.BackColor = highlighted ? highlightColors[colorIndex] : normalColors[colorIndex];
                this.Opacity = highlighted ? 0.8 : 0.6;
                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Draw border (thicker when highlighted)
            int borderWidth = isHighlighted ? 6 : 3;
            using (var borderPen = new Pen(Color.White, borderWidth))
            {
                int offset = borderWidth / 2;
                e.Graphics.DrawRectangle(borderPen, offset, offset, this.Width - borderWidth, this.Height - borderWidth);
            }

            // Draw zone number (larger when highlighted)
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            using (var textBrush = new SolidBrush(Color.White))
            using (var font = new Font("Arial", isHighlighted ? 36 : 28, FontStyle.Bold))
            {
                var size = e.Graphics.MeasureString(zoneNumber, font);
                var point = new PointF((this.Width - size.Width) / 2, (this.Height - size.Height) / 2);

                var textRect = new RectangleF(point.X - 10, point.Y - 5, size.Width + 20, size.Height + 10);
                e.Graphics.FillRectangle(backgroundBrush, textRect);
                e.Graphics.DrawString(zoneNumber, font, textBrush, point);
            }

            // Draw instruction text when highlighted
            if (isHighlighted)
            {
                using (var textBrush = new SolidBrush(Color.White))
                using (var font = new Font("Arial", 12, FontStyle.Bold))
                {
                    string instruction = "Release to snap window here";
                    var size = e.Graphics.MeasureString(instruction, font);
                    var point = new PointF((this.Width - size.Width) / 2, this.Height - 30);

                    using (var backgroundBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                    {
                        var textRect = new RectangleF(point.X - 5, point.Y - 2, size.Width + 10, size.Height + 4);
                        e.Graphics.FillRectangle(backgroundBrush, textRect);
                    }

                    e.Graphics.DrawString(instruction, font, textBrush, point);
                }
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeConstants.WS_EX_LAYERED;
                cp.ExStyle |= NativeConstants.WS_EX_TRANSPARENT;
                cp.ExStyle |= NativeConstants.WS_EX_NOACTIVATE;
                return cp;
            }
        }
    }
}
