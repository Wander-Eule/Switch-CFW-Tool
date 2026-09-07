using System;
using System.Diagnostics;
using System.Windows.Navigation;

namespace SwitchCfwTool;

/// <summary>
/// Opens links in whatever the system considers responsible for them. Shared because the
/// same handler was previously copied verbatim into three windows, which meant any fix had
/// to be made three times.
/// </summary>
public static class Links
{
    /// <summary>Handler for a Hyperlink's RequestNavigate event.</summary>
    public static void Navigate(object sender, RequestNavigateEventArgs e)
    {
        Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    public static void Open(string url)
    {
        try
        {
            // UseShellExecute hands the address to the registered application — which lets a
            // Discord invite open in the desktop client if it's installed, and falls back to
            // the browser otherwise.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Nothing sensible to do if the shell refuses; not worth interrupting the user.
        }
    }
}
