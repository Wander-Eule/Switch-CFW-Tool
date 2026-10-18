using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace SwitchCfwTool.Localization;

/// <summary>One language the program can be switched to, as offered in the Language menu.</summary>
public record LanguageInfo(string Code, string Name);

/// <summary>
/// All user-facing text, looked up by key from the language files in Localization/.
///
/// Every language is a flat JSON file (key → text) embedded into the exe, so a translation
/// is a single file that can be handed to someone without any code around it. en.json is
/// the reference: every key exists there, and any key a translation leaves out falls back
/// to its English text rather than showing up blank.
///
/// Also serves as the binding source for XAML ({l:Tr Some.Key}): the indexer is what gets
/// bound, and raising "Item[]" on a switch makes every bound text refresh in place, so the
/// main window changes language without a restart.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string DefaultLanguage = "en";

    // Resources are embedded as "lang.<code>.json" (see the .csproj).
    private const string ResourcePrefix = "lang.";
    private const string ResourceSuffix = ".json";

    // Holds the language's own name for the Language menu; never shown anywhere else.
    private const string NameKey = "_meta.languageName";

    public static Loc Instance { get; } = new();

    private static readonly Dictionary<string, Dictionary<string, string>> Languages = LoadAll();
    private static Dictionary<string, string> _active = Languages.GetValueOrDefault(DefaultLanguage) ?? new();
    private static readonly Dictionary<string, string> Fallback = _active;

    /// <summary>Code of the language currently in use, e.g. "en" or "pt-BR".</summary>
    public static string Current { get; private set; } = DefaultLanguage;

    /// <summary>Raised after a switch, for text that is set from code rather than bound.</summary>
    public static event Action? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    private Loc() { }

    /// <summary>Every embedded language, English first, the rest by name.</summary>
    public static IReadOnlyList<LanguageInfo> Available => Languages
        .Select(kv => new LanguageInfo(kv.Key, kv.Value.GetValueOrDefault(NameKey) ?? kv.Key))
        .OrderBy(l => l.Code == DefaultLanguage ? 0 : 1)
        .ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>Indexer used by XAML bindings; same lookup as <see cref="T(string)"/>.</summary>
    public string this[string key] => T(key);

    /// <summary>The text for <paramref name="key"/> in the current language.</summary>
    public static string T(string key)
    {
        if (_active.TryGetValue(key, out var text)) return text;
        if (Fallback.TryGetValue(key, out text)) return text;

        // A missing key shows as itself: obviously wrong on screen, but never a crash.
        return key;
    }

    /// <summary>The text for <paramref name="key"/> with {0}, {1}, ... filled in.</summary>
    public static string T(string key, params object?[] args)
    {
        var format = T(key);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            // A translation with broken placeholders shouldn't take the program down; the
            // English text is used for that one line instead.
            try
            {
                return string.Format(CultureInfo.InvariantCulture, Fallback.GetValueOrDefault(key) ?? key, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }
    }

    /// <summary>The text for <paramref name="key"/>, or null when no language defines it.</summary>
    public static string? TryT(string key)
    {
        if (_active.TryGetValue(key, out var text)) return text;
        return Fallback.TryGetValue(key, out text) ? text : null;
    }

    /// <summary>Switches language and remembers the choice for the next start.</summary>
    public static void SetLanguage(string code)
    {
        if (!Languages.TryGetValue(code, out var strings)) return;
        if (code == Current) return;

        _active = strings;
        Current = code;
        LanguageSettings.Save(code);

        // "Item[]" is the name WPF listens for to refresh every indexer binding at once.
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    /// <summary>Applies the language saved last time. English stays in effect if none was saved.</summary>
    public static void LoadSavedLanguage()
    {
        var saved = LanguageSettings.Load();
        if (saved != null && Languages.ContainsKey(saved))
        {
            _active = Languages[saved];
            Current = saved;
        }
    }

    private static Dictionary<string, Dictionary<string, string>> LoadAll()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            // Resource names carry the assembly name in front, so the code is whatever sits
            // between "lang." and ".json" at the end of the name.
            var start = resource.LastIndexOf(ResourcePrefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0 || !resource.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase)) continue;

            var code = resource.Substring(start + ResourcePrefix.Length,
                resource.Length - start - ResourcePrefix.Length - ResourceSuffix.Length);
            if (code.Length == 0 || code.Contains('.')) continue;

            try
            {
                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream == null) continue;
                result[code] = Parse(stream);
            }
            catch
            {
                // A broken language file must not stop the program from starting — it simply
                // won't be offered. English is checked for completeness before each release.
            }
        }

        return result;
    }

    private static Dictionary<string, string> Parse(Stream stream)
    {
        // Comments are allowed so the files can carry notes for translators.
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
                strings[property.Name] = property.Value.GetString() ?? "";
        }
        return strings;
    }
}

/// <summary>
/// Remembers the chosen language between starts. Kept in AppData with the Quick Tour marker
/// and the GitHub token, so the program's own folder stays nothing but the exe and the licence.
///
/// Picking English deletes the file rather than writing "en" into it. No file means English,
/// so the two are the same thing — and it keeps the default reachable from the Language menu
/// instead of only by deleting a file somebody has to find first.
/// </summary>
internal static class LanguageSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Switch-CFW-Tool", "language.txt");

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var code = File.ReadAllText(FilePath).Trim();
            return code.Length == 0 ? null : code;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string code)
    {
        try
        {
            if (code == Loc.DefaultLanguage)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, code);
        }
        catch
        {
            // Not being able to remember the language is no reason to interrupt anything;
            // the program simply starts in the previous one next time.
        }
    }
}
