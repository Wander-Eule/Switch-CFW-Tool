using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace SwitchCfwTool;

/// <summary>
/// Colours the native title bar on Windows to match the window body, so the app reads as one
/// surface instead of a dark window wearing a light system caption. On macOS and Linux,
/// Avalonia's dark theme variant handles the native chrome automatically.
/// </summary>
internal static class TitleBarTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    private const int GWL_STYLE = -16;
    private const long WS_MAXIMIZEBOX = 0x00010000;
    private const long WS_MINIMIZEBOX = 0x00020000;
    private const uint SWP_FRAMECHANGED = 0x0020, SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001,
                       SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        if (window.TryGetPlatformHandle()?.Handle is { } handle && handle != IntPtr.Zero)
            ApplyCore(handle);
        else
            window.Opened += (_, _) =>
            {
                if (window.TryGetPlatformHandle()?.Handle is { } h && h != IntPtr.Zero)
                    ApplyCore(h);
            };

        // Fixed-size dialogs only ever had a close button (WPF's ResizeMode="NoResize"). Avalonia
        // 11 greys out maximise but still shows both buttons, so they are taken out of the
        // native window style once the window is up — doing it earlier gets overwritten when
        // Avalonia applies its own style on show.
        if (!window.CanResize)
            window.Opened += (_, _) =>
            {
                if (window.TryGetPlatformHandle()?.Handle is { } h && h != IntPtr.Zero)
                    HideMinMaxButtons(h);
            };
    }

    private static void HideMinMaxButtons(IntPtr hwnd)
    {
        try
        {
            var style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style & ~(WS_MINIMIZEBOX | WS_MAXIMIZEBOX)));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch
        {
            // Cosmetic as well — the dialog works the same with the buttons showing.
        }
    }

    private static void ApplyCore(IntPtr hwnd)
    {
        try
        {
            var darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            // Matches the top-left stop of WindowBackground so the caption blends into the body.
            var caption = ToColorRef(0x1F, 0x1F, 0x3D);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

            var text = ToColorRef(0xEC, 0xEC, 0xF7);
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));

            var border = ToColorRef(0x2A, 0x2A, 0x4E);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch
        {
            // Purely cosmetic — never let a missing/renamed DWM attribute take the app down.
        }
    }

    /// <summary>DWM expects COLORREF (0x00BBGGRR), not the ARGB order used elsewhere.</summary>
    private static int ToColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
}
