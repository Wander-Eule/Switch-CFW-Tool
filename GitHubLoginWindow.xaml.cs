using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SwitchCfwTool.Services;

namespace SwitchCfwTool;

public partial class GitHubLoginWindow : Window
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x4A, 0xDE, 0x9E));
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(0x9A, 0x9A, 0xB8));

    private readonly GitHubService _github = null!;

    public GitHubLoginWindow()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
    }

    public GitHubLoginWindow(GitHubService github) : this()
    {
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

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        var token = (TokenBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(token))
        {
            await MessageDialog.Show(this, Loc.T("GitHub.Title"), Loc.T("GitHub.NoToken.Headline"),
                Loc.T("GitHub.NoToken.Message"));
            return;
        }

        _github.UseToken(token);
        GitHubTokenStore.Save(token);
        TokenBox.Text = "";
        UpdateStatus();
    }

    private void LogOut_Click(object? sender, RoutedEventArgs e)
    {
        _github.UseToken(null);
        GitHubTokenStore.Clear();
        TokenBox.Text = "";
        UpdateStatus();
    }

    private void Link_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
            Links.Open(url);
    }
}
