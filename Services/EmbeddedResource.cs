using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SwitchCfwTool.Services;

/// <summary>
/// Locates one of the assets embedded into the exe via a &lt;LogicalName&gt; in the .csproj.
/// Centralised because BundledAssetsService (hekate_ipl.ini + boot icons) and
/// UltrahandWallpaper (the bundled .rgba files) each independently looked resources up the
/// same way.
/// </summary>
internal static class EmbeddedResource
{
    /// <summary>
    /// Opens the resource whose LogicalName is <paramref name="logicalName"/>. The caller
    /// owns the returned stream and must dispose it.
    /// </summary>
    public static Stream Open(string logicalName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Embedded resources with a LogicalName are exposed as "<AssemblyName>.<LogicalName>".
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(logicalName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Embedded resource '{logicalName}' not found.");

        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Could not open embedded resource '{logicalName}'.");
    }
}
