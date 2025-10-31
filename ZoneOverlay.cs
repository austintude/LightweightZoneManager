using System;
using System.Drawing;
using System.Windows.Forms;

namespace LightweightZoneManager
{
    /// <summary>
    /// View-only zone overlay that displays for 8 seconds
    /// </summary>
    public class ZoneOverlay : Form
    {
        protected override bool ShowWithoutActivation => true;
        private readonly string zoneNumber;

        private static readonly Color[] zoneColors = new Color[]
        {
            Color.Blue, Color.Red, Color.Green, Color.Orange, Color.Purple,
            Color.Yellow, Color.Magenta, Color.Cyan, Color.Pink
        };

        public ZoneOverlay(Rectangle bounds, string number)
        {
            zoneNumber = number;

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = bounds;

            int colorIndex = (int.Parse(number) - 1) % zoneColors.Length;
            this.BackColor = zoneColors[colorIndex];
            this.Opacity = 0.7;

            // Auto-close after 8 seconds
            var timer = new Timer();
            timer.Interval = 8000;
            timer.Tick += (s, e) => { this.Close(); timer.Dispose(); };
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Draw white border
            using (var borderPen = new Pen(Color.White, 4))
            {
                e.Graphics.DrawRectangle(borderPen, 2, 2, this.Width - 4, this.Height - 4);
            }

            // Draw zone number in center
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            using (var textBrush = new SolidBrush(Color.White))
            using (var font = new Font("Arial", 32, FontStyle.Bold))
            {
                var size = e.Graphics.MeasureString(zoneNumber, font);
                var point = new PointF((this.Width - size.Width) / 2, (this.Height - size.Height) / 2);
                var textRect = new RectangleF(point.X - 10, point.Y - 5, size.Width + 20, size.Height + 10);
                e.Graphics.FillRectangle(backgroundBrush, textRect);
                e.Graphics.DrawString(zoneNumber, font, textBrush, point);
            }

            // Draw zone info in top-left corner
            using (var infoBrush = new SolidBrush(Color.White))
            using (var infoFont = new Font("Arial", 9, FontStyle.Bold))
            {
                string info = $"Zone {zoneNumber}\n{this.Width}×{this.Height}";
                e.Graphics.DrawString(info, infoFont, infoBrush, new PointF(8, 8));
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
