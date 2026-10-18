using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Navigation;
using SwitchCfwTool.Services;

namespace SwitchCfwTool;

public partial class GitHubLoginWindow : Window
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x4A, 0xDE, 0x9E));
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(0x9A, 0x9A, 0xB8));

    private readonly GitHubService _github;

    public GitHubLoginWindow(GitHubService github)
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
        _github = github;

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_github.IsAuthenticated)
        {
            StatusText.Text = Loc.T("GitHub.Status.LoggedIn");
            StatusText.Foreground = SuccessBrush;
        }
        else
        {
            StatusText.Text = Loc.T("GitHub.Status.LoggedOut");
            StatusText.Foreground = MutedBrush;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password.Trim();
        if (string.IsNullOrEmpty(token))
        {
            MessageDialog.Show(this, Loc.T("GitHub.Title"), Loc.T("GitHub.NoToken.Headline"),
                Loc.T("GitHub.NoToken.Message"));
            return;
        }

        _github.UseToken(token);
        GitHubTokenStore.Save(token);
        TokenBox.Password = "";
        UpdateStatus();
    }

    private void LogOut_Click(object sender, RoutedEventArgs e)
    {
        _github.UseToken(null);
        GitHubTokenStore.Clear();
        TokenBox.Password = "";
        UpdateStatus();
    }

private void Links_Navigate(object sender, RequestNavigateEventArgs e) => Links.Navigate(sender, e);
}
