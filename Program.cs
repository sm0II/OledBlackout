using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Program
{
    // ----- Low-level hooks -----
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private static IntPtr _kbHook = IntPtr.Zero;
    private static IntPtr _mouseHook = IntPtr.Zero;

    private static LowLevelProc _kbProc = KbHookCallback;
    private static LowLevelProc _mouseProc = MouseHookCallback;

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    // ----- Configurable timer -----
    // default: 200 ms (0.2 seconds)
    private static int GracePeriodMs = 2000;
    private static DateTime _activationTime;

    // ----- Overlay form covering a monitor -----
    private class OverlayForm : Form
    {
        public OverlayForm(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            BackColor = Color.Black;
            TopMost = true;
            ShowInTaskbar = false;
            Opacity = 1.0;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TryFocusWindow();
            Cursor.Hide();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Cursor.Show();
            base.OnFormClosed(e);
        }

        private void TryFocusWindow()
        {
            try
            {
                Activate();
                Focus();
            }
            catch { /* ignore */ }
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Allow user to override grace period via command-line argument
        // Example: BlackoutOverlay.exe 5000   (5 seconds)
        if (args.Length > 0 && int.TryParse(args[0], out int ms))
        {
            GracePeriodMs = Math.Max(0, ms);
        }

        _activationTime = DateTime.Now;

        // Create one black overlay window per monitor
        var forms = new List<Form>();
        foreach (var screen in Screen.AllScreens)
        {
            var form = new OverlayForm(screen.Bounds);
            forms.Add(form);
            form.Show();
        }

        // Install global hooks so ANY input anywhere will exit (after grace period)
        using var current = Process.GetCurrentProcess();
        using var module = current.MainModule!;
        IntPtr hMod = GetModuleHandle(module.ModuleName);

        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);

        Application.ApplicationExit += (_, __) =>
        {
            if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
            if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        };

        Application.Run();
    }

    private static IntPtr KbHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
        // If user presses ESC, force exit immediately
        if (vkCode == (int)Keys.Escape)
        {
            Application.Exit();
        }


            if ((DateTime.Now - _activationTime).TotalMilliseconds >= GracePeriodMs)
            {
                Application.Exit();
            }
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            if ((DateTime.Now - _activationTime).TotalMilliseconds >= GracePeriodMs)
            {
                Application.Exit();
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }
}
