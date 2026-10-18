using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SwitchCfwTool.Services;

/// <summary>One value the theming UI exposes.</summary>
public class ThemeField
{
    public required string Key { get; init; }

    /// <summary>Name shown in the list, from "Theme.Field.&lt;Key&gt;" in the language files.</summary>
    public string Label => Loc.T("Theme.Field." + Key);

    /// <summary>Alpha keys are 0–15 in Ultrahand, colour keys are RRGGBB hex.</summary>
    public bool IsAlpha { get; init; }

    /// <summary>
    /// Starts a new heading in the list; the text is "Theme.Group.&lt;GroupKey&gt;" in the
    /// language files. Null = continues the heading above.
    /// </summary>
    public string? GroupKey { get; init; }

    /// <summary>Heading shown above this field in the list.</summary>
    public string? Group => GroupKey == null ? null : Loc.T("Theme.Group." + GroupKey);
}

/// <summary>
/// Reads and writes Ultrahand theme files.
///
/// Important detail that is easy to get wrong: the animated borders are driven by the
/// "s2_" keys, not by "highlight_color_*". The latter only colours the static hint boxes.
/// Editing the wrong family leaves the borders looking untouched, which is exactly what
/// happened before this was corrected.
///
/// The full key set is always written out. Ultrahand falls back to its own stock values for
/// anything missing, so a partially written file mixes custom and built-in colours instead
/// of simply looking unstyled.
/// </summary>
public static class UltrahandTheme
{
    public const string RelativeFolder = "config/ultrahand/themes";

    /// <summary>
    /// Animated borders come in four colours plus a deeper variant of the third and fourth.
    /// The deep variants are mirrored from their base on save rather than being exposed
    /// separately, which keeps the list workable at the cost of a slightly flatter animation.
    /// </summary>
    private static readonly string[] DeepMirrors = { "3", "4" };

    // "s2_alt_highlight_color_" deliberately isn't here: it isn't in EditableFields, so
    // `values` never carries a user-edited base for it — mirroring it on every save would
    // just overwrite Ultrahand's own distinct stock "_deep" shades with a flat copy of the
    // (untouched) base colour.
    private static readonly string[] AnimatedBorderPrefixes =
    {
        "s2_highlight_color_",
        "s2_widget_border_color_",
        "s2_table_border_color_",
    };

    public static readonly List<ThemeField> EditableFields = new()
    {
        new ThemeField { GroupKey = "General", Key = "text_color" },
        new ThemeField { Key = "bg_color" },
        new ThemeField { Key = "bg_alpha", IsAlpha = true },
        new ThemeField { Key = "header_text_color" },
        new ThemeField { Key = "header_separator_color" },
        new ThemeField { Key = "separator_color" },

        new ThemeField { GroupKey = "Logo", Key = "logo_color_1" },
        new ThemeField { Key = "logo_color_2" },
        new ThemeField { Key = "dynamic_logo_color_1" },
        new ThemeField { Key = "dynamic_logo_color_2" },
        new ThemeField { Key = "version_text_color" },

        new ThemeField { GroupKey = "SelectedItem", Key = "selection_text_color" },
        new ThemeField { Key = "selection_bg_color" },
        new ThemeField { Key = "selection_bg_alpha", IsAlpha = true },
        new ThemeField { Key = "s2_highlight_color_1" },
        new ThemeField { Key = "s2_highlight_color_2" },
        new ThemeField { Key = "s2_highlight_color_3" },
        new ThemeField { Key = "s2_highlight_color_4" },

        new ThemeField { GroupKey = "StatusWidget", Key = "clock_color" },
        new ThemeField { Key = "battery_color" },
        new ThemeField { Key = "temperature_color" },
        new ThemeField { Key = "s2_widget_border_color_1" },
        new ThemeField { Key = "s2_widget_border_color_2" },
        new ThemeField { Key = "s2_widget_border_color_3" },
        new ThemeField { Key = "s2_widget_border_color_4" },

        new ThemeField { GroupKey = "Toggles", Key = "s2_toggle_on_color" },
        new ThemeField { Key = "s2_toggle_off_color" },
        new ThemeField { Key = "s2_toggle_circle_color" },
        new ThemeField { Key = "on_text_color" },
        new ThemeField { Key = "off_text_color" },

        new ThemeField { GroupKey = "RadioButtons", Key = "s2_radio_selected_color" },
        new ThemeField { Key = "s2_radio_inner_color" },
        new ThemeField { Key = "s2_radio_ring_color" },

        new ThemeField { GroupKey = "Sliders", Key = "trackbar_full_color" },
        new ThemeField { Key = "trackbar_empty_color" },
        new ThemeField { Key = "trackbar_malleable_slider_color" },
        new ThemeField { Key = "trackbar_slider_color" },
        new ThemeField { Key = "trackbar_slider_malleable_color" },
        new ThemeField { Key = "trackbar_slider_border_color" },

        new ThemeField { GroupKey = "InfoBoxes", Key = "table_bg_color" },
        new ThemeField { Key = "table_bg_alpha", IsAlpha = true },
        new ThemeField { Key = "table_section_text_color" },
        new ThemeField { Key = "table_info_text_color" },
        new ThemeField { Key = "s2_table_border_color_1" },
        new ThemeField { Key = "s2_table_border_color_2" },
        new ThemeField { Key = "s2_table_border_color_3" },
        new ThemeField { Key = "s2_table_border_color_4" },

        new ThemeField { GroupKey = "HintBoxes", Key = "highlight_color_1" },
        new ThemeField { Key = "highlight_color_2" },
        new ThemeField { Key = "highlight_color_3" },
        new ThemeField { Key = "highlight_color_4" },

        new ThemeField { GroupKey = "BottomBar", Key = "bottom_text_color" },
        new ThemeField { Key = "bottom_button_color" },
        new ThemeField { Key = "bottom_separator_color" },
    };

    /// <summary>The complete key set with Ultrahand's stock values as the starting point.</summary>
    public static Dictionary<string, string> CreateDefault() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["bad_ram_text_color"] = "FF0000",
        ["banner_version_text_color"] = "AAAAAA",
        ["battery_charging_color"] = "00FF00",
        ["battery_color"] = "FFFF45",
        ["battery_low_color"] = "FF0000",
        ["bg_alpha"] = "4",
        ["bg_color"] = "000000",
        ["bottom_button_color"] = "545C80",
        ["bottom_separator_color"] = "545C80",
        ["bottom_text_color"] = "EEEEFF",
        ["click_alpha"] = "10",
        ["click_color"] = "960314",
        ["click_text_color"] = "FFFFFF",
        ["clock_color"] = "1CFC45",
        ["default_overlay_color"] = "FFFFFF",
        ["default_package_color"] = "FFFFFF",
        ["default_script_color"] = "FF33FF",
        ["disable_colorful_logo"] = "false",
        ["disable_selection_bg"] = "true",
        ["dynamic_logo_color_1"] = "00E669",
        ["dynamic_logo_color_2"] = "8080EA",
        ["header_separator_color"] = "545C80",
        ["header_text_color"] = "EEEEFF",
        ["healthy_ram_text_color"] = "00FF00",
        ["highlight_color_1"] = "2288CC",
        ["highlight_color_2"] = "88FFFF",
        ["highlight_color_3"] = "FFFF45",
        ["highlight_color_4"] = "F7253E",
        ["info_text_color"] = "FFFFFF",
        ["inprogress_text_color"] = "FFFF45",
        ["invalid_text_color"] = "FF0000",
        ["invert_bg_click_color"] = "false",
        ["logo_color_1"] = "FFFFFF",
        ["logo_color_2"] = "FF0000",
        ["neutral_ram_text_color"] = "FFAA00",
        ["notification_text_color"] = "FFFFFF",
        ["notification_time_color"] = "AAAAAA",
        ["notification_title_color"] = "FFFFFF",
        ["off_text_color"] = "AAAAAA",
        ["on_text_color"] = "00FFDD",
        ["overlay_text_color"] = "FFFFFF",
        ["overlay_version_text_color"] = "AAAAAA",
        ["package_text_color"] = "FFFFFF",
        ["package_version_text_color"] = "AAAAAA",
        ["progress_alpha"] = "7",
        ["progress_color"] = "253EF7",
        ["s2_alt_highlight_color_1"] = "F7913E",
        ["s2_alt_highlight_color_2"] = "FFFF45",
        ["s2_alt_highlight_color_3"] = "FFD7A0",
        ["s2_alt_highlight_color_3_deep"] = "F7913E",
        ["s2_alt_highlight_color_4"] = "FFFFC0",
        ["s2_alt_highlight_color_4_deep"] = "FFFF45",
        ["s2_highlight_color_1"] = "0066FF",
        ["s2_highlight_color_2"] = "8877FF",
        ["s2_highlight_color_3"] = "EEFFFF",
        ["s2_highlight_color_3_deep"] = "AAEEFF",
        ["s2_highlight_color_4"] = "FFDDDD",
        ["s2_highlight_color_4_deep"] = "FFBBBB",
        ["s2_radio_inner_color"] = "FFFFFF",
        ["s2_radio_inprogress_color"] = "9933EE",
        ["s2_radio_ring_color"] = "666666",
        ["s2_radio_selected_color"] = "0066EE",
        ["s2_table_border_color_1"] = "7755FF",
        ["s2_table_border_color_2"] = "6644FF",
        ["s2_table_border_color_3"] = "779999",
        ["s2_table_border_color_3_deep"] = "665577",
        ["s2_table_border_color_4"] = "AA9988",
        ["s2_table_border_color_4_deep"] = "775555",
        ["s2_toggle_circle_color"] = "FFFFFF",
        ["s2_toggle_off_color"] = "555555",
        ["s2_toggle_on_color"] = "0066EE",
        ["s2_widget_border_color_1"] = "7755FF",
        ["s2_widget_border_color_2"] = "FCFC6E",
        ["s2_widget_border_color_3"] = "5AF291",
        ["s2_widget_border_color_3_deep"] = "5A91F2",
        ["s2_widget_border_color_4"] = "F2B7ED",
        ["s2_widget_border_color_4_deep"] = "F2915A",
        ["scrollbar_color"] = "555555",
        ["scrollbar_wall_color"] = "AAAAAA",
        ["selection_bg_alpha"] = "11",
        ["selection_bg_color"] = "000000",
        ["selection_star_color"] = "FFFF45",
        ["selection_text_color"] = "73BBFF",
        ["selection_value_text_color"] = "FF7777",
        ["separator_alpha"] = "15",
        ["separator_color"] = "404040",
        ["star_color"] = "FFFF45",
        ["table_bg_alpha"] = "14",
        ["table_bg_color"] = "2F2F30",
        ["table_border_color"] = "555555",
        ["table_info_text_color"] = "FFFFFF",
        ["table_section_text_color"] = "FFE940",
        ["temperature_color"] = "FFFFFF",
        ["text_color"] = "FFFFFF",
        ["text_separator_color"] = "545C80",
        ["top_separator_color"] = "404040",
        ["trackbar_color"] = "555555",
        ["trackbar_empty_color"] = "404040",
        ["trackbar_full_color"] = "1EFC22",
        ["trackbar_malleable_slider_color"] = "1EFC22",
        ["trackbar_slider_border_color"] = "505050",
        ["trackbar_slider_color"] = "545C80",
        ["trackbar_slider_malleable_color"] = "73BBFF",
        ["ult_overlay_text_color"] = "9ED0FF",
        ["ult_overlay_version_text_color"] = "00FFDD",
        ["ult_package_text_color"] = "9ED0FF",
        ["ult_package_version_text_color"] = "00FFDD",
        ["unfocused_color"] = "666666",
        ["version_text_color"] = "AAAAAA",
        ["warning_text_color"] = "FFFF45",
        ["widget_backdrop_alpha"] = "15",
        ["widget_backdrop_color"] = "000000",
        ["widget_border_color"] = "555555",
    };

    public static void Save(string sdRoot, string themeName, Dictionary<string, string> values)
    {
        var folder = Path.Combine(sdRoot, RelativeFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);

        var safeName = string.Concat(themeName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "custom";

        var complete = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

        // Keeps the deeper shades in step with the colours actually edited; leaving them at
        // stock would blend built-in blues into an otherwise custom border animation.
        foreach (var prefix in AnimatedBorderPrefixes)
        {
            foreach (var index in DeepMirrors)
            {
                if (complete.TryGetValue(prefix + index, out var baseColor))
                    complete[prefix + index + "_deep"] = baseColor;
            }
        }

        var lines = new List<string> { "[theme]" };
        lines.AddRange(complete.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                               .Select(kv => $"{kv.Key}={kv.Value}"));

        File.WriteAllLines(Path.Combine(folder, safeName + ".ini"), lines);
    }
}
