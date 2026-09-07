using System.Windows;

namespace SwitchCfwTool;

/// <summary>
/// Single-button notice styled like the rest of the app. Replaces MessageBox.Show, which
/// always renders in the system's own light chrome and looked out of place against the
/// dark theme.
/// </summary>
public partial class MessageDialog : Window
{
    public MessageDialog(string title, string headline, string message)
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);

        Title = title;
        HeadlineText.Text = headline;
        MessageText.Text = message;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Convenience wrapper so call sites read like the MessageBox they replace.</summary>
    public static void Show(Window owner, string title, string headline, string message)
        => new MessageDialog(title, headline, message) { Owner = owner }.ShowDialog();
}
