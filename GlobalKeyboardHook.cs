using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LightweightZoneManager
{
    /// <summary>
    /// Global keyboard hook for hotkey registration
    /// </summary>
    public class GlobalKeyboardHook : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private Window window = new Window();
        private int currentId = 0;

        public GlobalKeyboardHook()
        {
            window.KeyPressed += delegate (object sender, KeyPressedEventArgs args)
            {
                KeyDown?.Invoke(this, args);
            };
        }

        public event EventHandler<KeyPressedEventArgs> KeyDown;

        public void RegisterHotKey(HotKeyModifiers modifier, Keys key)
        {
            currentId++;
            if (!RegisterHotKey(window.Handle, currentId, (int)modifier, (int)key))
                throw new InvalidOperationException("Couldn't register the hot key.");
        }

        public void Dispose()
        {
            for (int i = currentId; i > 0; i--)
            {
                UnregisterHotKey(window.Handle, i);
            }
            window.Dispose();
        }

        private class Window : NativeWindow, IDisposable
        {
            private static int WM_HOTKEY = 0x0312;

            public Window()
            {
                this.CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                if (m.Msg == WM_HOTKEY)
                {
                    Keys key = (Keys)(((int)m.LParam >> 16) & 0xFFFF);
                    HotKeyModifiers modifier = (HotKeyModifiers)((int)m.LParam & 0xFFFF);

                    KeyPressed?.Invoke(this, new KeyPressedEventArgs() { Modifier = modifier, Key = key });
                }
            }

            public event EventHandler<KeyPressedEventArgs> KeyPressed;

            public void Dispose()
            {
                this.DestroyHandle();
            }
        }
    }

    /// <summary>
    /// Event args for hotkey presses
    /// </summary>
    public class KeyPressedEventArgs : EventArgs
    {
        public HotKeyModifiers Modifier { get; set; }
        public Keys Key { get; set; }
    }

    /// <summary>
    /// Hotkey modifier flags
    /// </summary>
    [Flags]
    public enum HotKeyModifiers : uint
    {
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8
    }
}
