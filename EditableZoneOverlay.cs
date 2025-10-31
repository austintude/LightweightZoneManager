using System;
using System.Drawing;
using System.Windows.Forms;

namespace LightweightZoneManager
{
    /// <summary>
    /// Editable zone overlay that allows dragging to move and resize
    /// </summary>
    public class EditableZoneOverlay : Form
    {
        protected override bool ShowWithoutActivation => true;

        private readonly string zoneNumber;
        private readonly int zoneIndex;
        private readonly ZoneManager parentManager;

        private bool isDragging = false;
        private bool isResizing = false;
        private Point dragOffset;
        private ResizeDirection resizeDirection;

        private enum ResizeDirection
        {
            None, TopLeft, TopRight, BottomLeft, BottomRight,
            Left, Right, Top, Bottom, Move
        }

        private static readonly Color[] zoneColors = new Color[]
        {
            Color.Blue, Color.Red, Color.Green, Color.Orange, Color.Purple,
            Color.Yellow, Color.Magenta, Color.Cyan, Color.Pink
        };

        public EditableZoneOverlay(Rectangle bounds, string number, int index, ZoneManager manager)
        {
            zoneNumber = number;
            zoneIndex = index;
            parentManager = manager;

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = bounds;

            int colorIndex = (int.Parse(number) - 1) % zoneColors.Length;
            this.BackColor = zoneColors[colorIndex];
            this.Opacity = 0.8;

            this.SetStyle(ControlStyles.UserMouse, true);
            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                resizeDirection = GetResizeDirection(e.Location);

                if (resizeDirection == ResizeDirection.Move)
                {
                    isDragging = true;
                    dragOffset = e.Location;
                }
                else if (resizeDirection != ResizeDirection.None)
                {
                    isResizing = true;
                    dragOffset = e.Location;
                }

                this.Capture = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point newLocation = new Point(
                    this.Location.X + e.X - dragOffset.X,
                    this.Location.Y + e.Y - dragOffset.Y
                );
                this.Location = newLocation;
            }
            else if (isResizing)
            {
                ResizeZone(e.Location);
            }
            else
            {
                ResizeDirection direction = GetResizeDirection(e.Location);
                this.Cursor = GetCursorForDirection(direction);
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (isDragging || isResizing)
            {
                parentManager.UpdateZoneFromEdit(zoneIndex, this.Bounds);
                isDragging = false;
                isResizing = false;
                this.Capture = false;
                this.Cursor = Cursors.Default;
            }
        }

        private ResizeDirection GetResizeDirection(Point point)
        {
            const int handleSize = 10;

            bool nearLeft = point.X <= handleSize;
            bool nearRight = point.X >= this.Width - handleSize;
            bool nearTop = point.Y <= handleSize;
            bool nearBottom = point.Y >= this.Height - handleSize;

            if (nearTop && nearLeft) return ResizeDirection.TopLeft;
            if (nearTop && nearRight) return ResizeDirection.TopRight;
            if (nearBottom && nearLeft) return ResizeDirection.BottomLeft;
            if (nearBottom && nearRight) return ResizeDirection.BottomRight;
            if (nearLeft) return ResizeDirection.Left;
            if (nearRight) return ResizeDirection.Right;
            if (nearTop) return ResizeDirection.Top;
            if (nearBottom) return ResizeDirection.Bottom;

            return ResizeDirection.Move;
        }

        private Cursor GetCursorForDirection(ResizeDirection direction)
        {
            switch (direction)
            {
                case ResizeDirection.TopLeft:
                case ResizeDirection.BottomRight:
                    return Cursors.SizeNWSE;
                case ResizeDirection.TopRight:
                case ResizeDirection.BottomLeft:
                    return Cursors.SizeNESW;
                case ResizeDirection.Left:
                case ResizeDirection.Right:
                    return Cursors.SizeWE;
                case ResizeDirection.Top:
                case ResizeDirection.Bottom:
                    return Cursors.SizeNS;
                case ResizeDirection.Move:
                    return Cursors.SizeAll;
                default:
                    return Cursors.Default;
            }
        }

        private void ResizeZone(Point mousePoint)
        {
            Rectangle newBounds = this.Bounds;

            switch (resizeDirection)
            {
                case ResizeDirection.TopLeft:
                    newBounds = new Rectangle(
                        this.Location.X + mousePoint.X - dragOffset.X,
                        this.Location.Y + mousePoint.Y - dragOffset.Y,
                        this.Width - (mousePoint.X - dragOffset.X),
                        this.Height - (mousePoint.Y - dragOffset.Y)
                    );
                    break;
                case ResizeDirection.TopRight:
                    newBounds = new Rectangle(
                        this.Location.X,
                        this.Location.Y + mousePoint.Y - dragOffset.Y,
                        mousePoint.X,
                        this.Height - (mousePoint.Y - dragOffset.Y)
                    );
                    break;
                case ResizeDirection.BottomLeft:
                    newBounds = new Rectangle(
                        this.Location.X + mousePoint.X - dragOffset.X,
                        this.Location.Y,
                        this.Width - (mousePoint.X - dragOffset.X),
                        mousePoint.Y
                    );
                    break;
                case ResizeDirection.BottomRight:
                    newBounds = new Rectangle(
                        this.Location.X,
                        this.Location.Y,
                        mousePoint.X,
                        mousePoint.Y
                    );
                    break;
                case ResizeDirection.Left:
                    newBounds = new Rectangle(
                        this.Location.X + mousePoint.X - dragOffset.X,
                        this.Location.Y,
                        this.Width - (mousePoint.X - dragOffset.X),
                        this.Height
                    );
                    break;
                case ResizeDirection.Right:
                    newBounds = new Rectangle(
                        this.Location.X,
                        this.Location.Y,
                        mousePoint.X,
                        this.Height
                    );
                    break;
                case ResizeDirection.Top:
                    newBounds = new Rectangle(
                        this.Location.X,
                        this.Location.Y + mousePoint.Y - dragOffset.Y,
                        this.Width,
                        this.Height - (mousePoint.Y - dragOffset.Y)
                    );
                    break;
                case ResizeDirection.Bottom:
                    newBounds = new Rectangle(
                        this.Location.X,
                        this.Location.Y,
                        this.Width,
                        mousePoint.Y
                    );
                    break;
            }

            // Minimum size constraints
            if (newBounds.Width >= 50 && newBounds.Height >= 30)
            {
                this.Bounds = newBounds;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Draw border
            using (var borderPen = new Pen(Color.White, 3))
            {
                e.Graphics.DrawRectangle(borderPen, 1, 1, this.Width - 2, this.Height - 2);
            }

            // Draw resize handles
            const int handleSize = 8;
            using (var handleBrush = new SolidBrush(Color.White))
            {
                // Corner handles
                e.Graphics.FillRectangle(handleBrush, 0, 0, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width - handleSize, 0, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, 0, this.Height - handleSize, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width - handleSize, this.Height - handleSize, handleSize, handleSize);

                // Edge handles
                e.Graphics.FillRectangle(handleBrush, this.Width / 2 - handleSize / 2, 0, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width / 2 - handleSize / 2, this.Height - handleSize, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, 0, this.Height / 2 - handleSize / 2, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width - handleSize, this.Height / 2 - handleSize / 2, handleSize, handleSize);
            }

            // Draw zone number and instructions
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            using (var textBrush = new SolidBrush(Color.White))
            using (var font = new Font("Arial", 24, FontStyle.Bold))
            using (var smallFont = new Font("Arial", 9, FontStyle.Bold))
            {
                var size = e.Graphics.MeasureString(zoneNumber, font);
                var point = new PointF((this.Width - size.Width) / 2, (this.Height - size.Height) / 2);

                var textRect = new RectangleF(point.X - 10, point.Y - 5, size.Width + 20, size.Height + 10);
                e.Graphics.FillRectangle(backgroundBrush, textRect);

                e.Graphics.DrawString(zoneNumber, font, textBrush, point);

                string instructions = "Drag to move • Drag corners/edges to resize";
                e.Graphics.DrawString(instructions, smallFont, textBrush, new PointF(10, this.Height - 25));
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeConstants.WS_EX_LAYERED;
                // Intentionally NOT transparent here because we need to interact with it
                cp.ExStyle |= NativeConstants.WS_EX_NOACTIVATE;
                return cp;
            }
        }
    }
}
