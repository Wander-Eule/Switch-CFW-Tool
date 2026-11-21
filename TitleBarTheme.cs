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

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

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
