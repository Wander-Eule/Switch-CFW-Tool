using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SwitchCfwTool;

/// <summary>
/// Shown once a build finishes and a payload.bin was actually produced.
/// </summary>
public partial class PayloadNoticeDialog : Window
{
    public PayloadNoticeDialog()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close();

    private void Link_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
            Links.Open(url);
    }
}
