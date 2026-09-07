using System.Windows;
using System.Windows.Navigation;

namespace SwitchCfwTool;

/// <summary>
/// Shown once a build finishes and a payload.bin was actually produced. What has to happen
/// with that file differs completely between an RCM-jig console and a modchipped one, and
/// getting it wrong is a common first stumble — so it's spelled out at the moment the file
/// appears rather than left to the log.
/// </summary>
public partial class PayloadNoticeDialog : Window
{
    public PayloadNoticeDialog()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();

private void Links_Navigate(object sender, RequestNavigateEventArgs e) => Links.Navigate(sender, e);
}
