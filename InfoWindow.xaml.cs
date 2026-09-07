using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using SwitchCfwTool.Data;

namespace SwitchCfwTool;

public partial class InfoWindow : Window
{
    private static readonly SolidColorBrush TealText = new(Color.FromRgb(0x2D, 0xD4, 0xBF));
    private static readonly SolidColorBrush MutedText = new(Color.FromRgb(0x9A, 0x9A, 0xB8));

    public InfoWindow()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
        BuildCredits();
    }

    /// <summary>
    /// Builds one line per tool. The credit line is assembled in code rather than bound in
    /// XAML because each recognised creator name has to become its own clickable link, and
    /// a single line can mention several people (original author plus forkers).
    /// </summary>
    private void BuildCredits()
    {
        // Longest names first so a name that contains another isn't matched in pieces.
        var pattern = string.Join("|", CreatorLinks.ByName.Keys
            .OrderByDescending(n => n.Length)
            .Select(Regex.Escape));
        var nameRegex = new Regex(pattern);

        foreach (var tool in ToolCatalog.All)
        {
            AddCreditLine(tool.DisplayName, tool.Author, nameRegex);

            // Companions are separate projects by other people, so they get their own
            // credit line rather than being folded into the parent tool's.
            foreach (var companion in tool.Companions)
                AddCreditLine(companion.DisplayName, companion.Author, nameRegex);
        }

        // Not a downloaded tool, so it isn't in the catalog — the bundled Hekate boot icons
        // (Assets/bootloader_res) are from github.com/NicholeMattera/Hekate-Icons.
        AddCreditLine("Icons", "NicholeMattera", nameRegex);
    }

    private void AddCreditLine(string displayName, string author, Regex nameRegex)
    {
        var line = new TextBlock { Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
        line.Inlines.Add(new Run(displayName) { FontWeight = FontWeights.SemiBold });
        line.Inlines.Add(new Run("  -  ") { Foreground = MutedText });

        var position = 0;
        foreach (Match match in nameRegex.Matches(author))
        {
            if (match.Index > position)
                line.Inlines.Add(new Run(author[position..match.Index]) { Foreground = MutedText });

            if (CreatorLinks.ByName.TryGetValue(match.Value, out var url))
            {
                var link = new Hyperlink(new Run(match.Value)) { NavigateUri = new Uri(url), Foreground = TealText };
                link.RequestNavigate += Links.Navigate;
                line.Inlines.Add(link);
            }
            else
            {
                line.Inlines.Add(new Run(match.Value) { Foreground = TealText });
            }

            position = match.Index + match.Length;
        }

        if (position < author.Length)
            line.Inlines.Add(new Run(author[position..]) { Foreground = MutedText });

        CreditsPanel.Children.Add(line);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

private void Links_Navigate(object sender, RequestNavigateEventArgs e) => Links.Navigate(sender, e);
}
