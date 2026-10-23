using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SwitchCfwTool.Models;

/// <summary>
/// Only the parts of a GitHub release this app actually reads. The API returns a great deal
/// more; System.Text.Json ignores properties that aren't declared here, so leaving the rest
/// out costs nothing and keeps it obvious what is relied on.
/// </summary>
public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = new();

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}
