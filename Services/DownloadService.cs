using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;

namespace SwitchCfwTool.Services;

public class DownloadProgress
{
    public string ToolId { get; init; } = "";
    public string Stage { get; init; } = ""; // "downloading", "extracting", "done", "error"
    public string Message { get; init; } = "";
}

/// <summary>
/// Downloads a file from a URL and, if it's an archive, extracts it into a staging folder.
/// One staging subfolder per tool, so SdCardBuilder can later decide what goes where.
/// </summary>
public class DownloadService
{
    // Static, not per-instance: a new DownloadService is created for every build and every
    // Manage CFW install/reinstall, and HttpClient is meant to be shared for the app's
    // lifetime rather than new'd-and-dropped — each one keeps its own connection pool alive
    // until finalized, which piles up handles/sockets over a long session otherwise.
    private static readonly HttpClient _http = new();
    private readonly string _stagingRoot;

    public DownloadService(string stagingRoot)
    {
        _stagingRoot = stagingRoot;
        Directory.CreateDirectory(_stagingRoot);
    }

    public async Task<string> DownloadAndStageAsync(
        string toolId,
        string url,
        string fileName,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var toolStagingDir = Path.Combine(_stagingRoot, toolId);
        Directory.CreateDirectory(toolStagingDir);
        var destFile = Path.Combine(toolStagingDir, fileName);

        progress?.Report(new DownloadProgress { ToolId = toolId, Stage = "downloading", Message = fileName });

        using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            using var fs = File.Create(destFile);
            await resp.Content.CopyToAsync(fs, ct);
        }

        // If it's a zip, extract next to it and return the extraction folder instead.
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(new DownloadProgress { ToolId = toolId, Stage = "extracting", Message = fileName });
            var extractDir = Path.Combine(toolStagingDir, "extracted");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);
            ExtractZipCrossPlatform(destFile, extractDir);

            progress?.Report(new DownloadProgress { ToolId = toolId, Stage = "done", Message = fileName });
            return extractDir;
        }

        progress?.Report(new DownloadProgress { ToolId = toolId, Stage = "done", Message = fileName });
        // Not an archive (e.g. a single .nro/.bin) — the staging dir itself contains the file.
        return toolStagingDir;
    }

    /// <summary>
    /// Extracts a zip archive while normalizing Windows-style backslash separators so archives
    /// authored on Windows unpack into proper subdirectories on Linux and macOS as well.
    /// </summary>
    private static void ExtractZipCrossPlatform(string archivePath, string destinationDir)
    {
        var destRoot = Path.GetFullPath(destinationDir);
        if (!destRoot.EndsWith(Path.DirectorySeparatorChar))
            destRoot += Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            var isDirectory = normalized.EndsWith('/');
            var relative = normalized.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(relative)) continue;

            var fullPath = Path.GetFullPath(Path.Combine(destRoot, relative));
            if (!fullPath.StartsWith(destRoot, StringComparison.Ordinal))
                throw new IOException($"Zip entry escapes destination directory: {entry.FullName}");

            if (isDirectory)
            {
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                entry.ExtractToFile(fullPath, overwrite: true);
            }
        }
    }
}
