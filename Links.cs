using System.Diagnostics;

namespace SwitchCfwTool;

/// <summary>
/// Opens links in whatever the system considers responsible for them. Shared because the
/// same handler was previously copied verbatim into three windows, which meant any fix had
/// to be made three times.
/// </summary>
public static class Links
{
    public static void Open(string url)
    {
        try
        {
            // UseShellExecute hands the address to whatever application is registered for it,
            // which is the browser for ordinary web links across Windows, macOS, and Linux.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Nothing sensible to do if the shell refuses; not worth interrupting the user.
        }
    }
}
