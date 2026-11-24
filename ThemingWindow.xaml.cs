using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SwitchCfwTool.Services;

namespace SwitchCfwTool;

/// <summary>One editable row: the theme key plus its current value, shown with a swatch.</summary>
public class ThemeFieldItem : INotifyPropertyChanged
{
    public required ThemeField Field { get; init; }
    public string Label => Field.Label;

    public string? Group => Field.Group;
    public bool GroupVisibility => Field.Group != null;

    private string _value = "FFFFFF";
    public string Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(Swatch)); OnPropertyChanged(nameof(Display)); }
    }

    public string Display => Field.IsAlpha ? Value : "#" + Value;

    public IBrush Swatch => Field.IsAlpha
        ? new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB8))
        : new SolidColorBrush(ThemingWindow.ParseHex(Value));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One bundled wallpaper in the picker.</summary>
public class WallpaperItem : INotifyPropertyChanged
{
    public required string Resource { get; init; }
    public required string Label { get; init; }
    public required Bitmap Preview { get; init; }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class ThemingWindow : Window
{
    private readonly List<WallpaperItem> _wallpapers = new();

    private readonly Dictionary<string, string> _values = UltrahandTheme.CreateDefault();
    private readonly List<ThemeFieldItem> _items = new();
    private ThemeFieldItem? _editing;

    // Guards the slider handler while values are being loaded into the sliders, so writing
    // the UI doesn't immediately write back a rounded value.
    private bool _loadingField;

    // Built in code, not XAML, so the gradient stops can be rewritten and rotated while the
    // window is open.
    private readonly LinearGradientBrush _cursorBrush;
    private readonly LinearGradientBrush _widgetBrush;
    private readonly LinearGradientBrush _tableBrush;
    private readonly LinearGradientBrush _knobBrush;
    private readonly RotateTransform _cursorRotate;
    private readonly RotateTransform _widgetRotate;
    private readonly RotateTransform _tableRotate;
    private readonly RotateTransform _knobRotate;
    private readonly SolidColorBrush _logoBrush = new(Colors.White);
    private Color _logoColor1 = Colors.White;
    private Color _logoColor2 = Colors.White;
    private readonly DispatcherTimer _animTimer;
    private readonly Stopwatch _animClock = Stopwatch.StartNew();

    private Bitmap? _wallpaper;

    /// <summary>Destination shared by both tabs; read from the field at the top of the window.</summary>
    private string SdRoot => (SdPathBox.Text ?? "").Trim();

    public ThemingWindow()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);

        foreach (var field in UltrahandTheme.EditableFields)
        {
            _items.Add(new ThemeFieldItem
            {
                Field = field,
                Value = _values.TryGetValue(field.Key, out var v) ? v : "FFFFFF"
            });
        }

        FieldList.ItemsSource = _items;

        _cursorBrush = BuildRotatingBorder(out _cursorRotate);
        _widgetBrush = BuildRotatingBorder(out _widgetRotate);
        _tableBrush = BuildRotatingBorder(out _tableRotate);
        _knobBrush = BuildRotatingBorder(out _knobRotate);

        PvSelection.BorderBrush = _cursorBrush;
        PvWidget.BorderBrush = _widgetBrush;
        PvTable.BorderBrush = _tableBrush;
        PvTrackKnob.BorderBrush = _knobBrush;
        PvLogo.Foreground = _logoBrush;

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += (_, _) => StepPreviewAnimations();
        _animTimer.Start();
        Closed += (_, _) => _animTimer.Stop();

        SelectField(_items[0]);
        RefreshPreview();
        LoadWallpapers();
    }

    public ThemingWindow(string? initialPath) : this()
    {
        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            SdPathBox.Text = initialPath;
            RefreshWallpaperState();
        }
    }

    private void LoadWallpapers()
    {
        foreach (var (resource, labelKey) in UltrahandWallpaper.Bundled)
        {
            try
            {
                _wallpapers.Add(new WallpaperItem
                {
                    Resource = resource,
                    Label = Loc.T(labelKey),
                    Preview = UltrahandWallpaper.LoadPreview(resource)
                });
            }
            catch
            {
                // A wallpaper that fails to load shouldn't take the whole window with it.
            }
        }

        WallpaperList.ItemsSource = _wallpapers;
        RefreshWallpaperState();
    }

    /// <summary>Marks whichever bundled wallpaper the selected card currently uses.</summary>
    private void RefreshWallpaperState()
    {
        string? active = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(SdRoot) && Directory.Exists(SdRoot))
                active = UltrahandWallpaper.DetectActive(SdRoot);
        }
        catch
        {
            // Unreadable card: show everything as unselected rather than failing.
        }

        foreach (var item in _wallpapers)
            item.IsActive = item.Resource == active;
    }

    private async void Wallpaper_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not WallpaperItem item) return;
        if (!await EnsureSdPathAsync()) return;

        try
        {
            if (item.IsActive)
            {
                // Clicking the active one turns the wallpaper off again.
                UltrahandWallpaper.Clear(SdRoot);
                item.IsActive = false;
                return;
            }

            UltrahandWallpaper.Apply(SdRoot, item.Resource);

            // Only one can be active, so the others clear themselves.
            foreach (var other in _wallpapers)
                other.IsActive = other == item;
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.Error.ApplyWallpaper"), ex.Message);
        }
    }

    private static LinearGradientBrush BuildRotatingBorder(out RotateTransform rotate)
    {
        rotate = new RotateTransform(0);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            TransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Transform = rotate
        };

        for (var i = 0; i < 5; i++)
            brush.GradientStops.Add(new GradientStop(Colors.White, i / 4.0));

        return brush;
    }

    private void StepPreviewAnimations()
    {
        var s = _animClock.Elapsed.TotalSeconds;
        _cursorRotate.Angle = (s % 4.5) / 4.5 * 360.0;
        _widgetRotate.Angle = (s % 5.5) / 5.5 * 360.0;
        _tableRotate.Angle = (s % 6.5) / 6.5 * 360.0;
        _knobRotate.Angle = (s % 4.0) / 4.0 * 360.0;

        // Ping-pong linearly between dynamic_logo_color_1 and dynamic_logo_color_2 every 3.2s.
        var phase = (s % 3.2) / 1.6;
        var t = phase <= 1.0 ? phase : 2.0 - phase;
        _logoBrush.Color = Color.FromRgb(
            (byte)Math.Round(_logoColor1.R + (_logoColor2.R - _logoColor1.R) * t),
            (byte)Math.Round(_logoColor1.G + (_logoColor2.G - _logoColor1.G) * t),
            (byte)Math.Round(_logoColor1.B + (_logoColor2.B - _logoColor1.B) * t));
    }

    // ---------- editing ----------

    private void Field_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ThemeFieldItem item) SelectField(item);
    }

    private void SelectField(ThemeFieldItem item)
    {
        _editing = item;
        EditingLabel.Text = item.Label;
        _loadingField = true;

        if (item.Field.IsAlpha)
        {
            RgbPanel.IsVisible = false;
            AlphaPanel.IsVisible = true;
            ASlider.Value = int.TryParse(item.Value, out var a) ? Math.Clamp(a, 0, 15) : 13;
        }
        else
        {
            RgbPanel.IsVisible = true;
            AlphaPanel.IsVisible = false;
            var c = ParseHex(item.Value);
            RSlider.Value = c.R;
            GSlider.Value = c.G;
            BSlider.Value = c.B;
        }

        _loadingField = false;
    }

    private void Channel_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loadingField || _editing == null) return;

        _editing.Value = _editing.Field.IsAlpha
            ? ((int)ASlider.Value).ToString()
            : $"{(int)RSlider.Value:X2}{(int)GSlider.Value:X2}{(int)BSlider.Value:X2}";

        _values[_editing.Field.Key] = _editing.Value;
        RefreshPreview();
    }

    // ---------- preview ----------

    private string Get(string key) => _values.TryGetValue(key, out var v) ? v : "FFFFFF";

    private SolidColorBrush Brush(string key) => new(ParseHex(Get(key)));

    /// <summary>Ultrahand alpha keys run 0–15; mapped onto the 0–255 the preview needs.</summary>
    private byte Alpha(string key) => (byte)(int.TryParse(Get(key), out var a) ? Math.Clamp(a, 0, 15) * 17 : 255);

    private void RefreshPreview()
    {
        var bg = ParseHex(Get("bg_color"));
        PreviewRoot.Background = new SolidColorBrush(Color.FromArgb(Alpha("bg_alpha"), bg.R, bg.G, bg.B));

        // header
        PvHeadDivider.Background = Brush("header_separator_color");
        PvClock.Foreground = Brush("clock_color");
        PvTemp.Foreground = Brush("temperature_color");
        PvBattery.Foreground = Brush("battery_color");
        PvHeaderSep.Background = Brush("header_separator_color");

        // section headers
        foreach (var bar in new[] { PvSec1Bar, PvSec2Bar, PvSec3Bar })
            bar.Background = Brush("header_text_color");
        foreach (var text in new[] { PvSec1Text, PvSec2Text, PvSec3Text })
            text.Foreground = Brush("header_text_color");

        // selected row
        var sel = ParseHex(Get("selection_bg_color"));
        PvSelection.Background = new SolidColorBrush(Color.FromArgb(Alpha("selection_bg_alpha"), sel.R, sel.G, sel.B));
        PvSelectedText.Foreground = Brush("selection_text_color");
        PvVersion.Foreground = Brush("version_text_color");

        // toggles
        PvToggleOffTrack.Background = Brush("s2_toggle_off_color");
        PvToggleOnTrack.Background = Brush("s2_toggle_on_color");
        PvToggleOnTrack2.Background = Brush("s2_toggle_on_color");
        foreach (var knob in new[] { PvToggleOffKnob, PvToggleOnKnob, PvToggleOnKnob2 })
            knob.Background = Brush("s2_toggle_circle_color");

        // plain rows
        PvItem2.Foreground = Brush("text_color");
        PvItem3.Foreground = Brush("text_color");
        PvRadioItem1.Foreground = Brush("text_color");
        PvRadioItem2.Foreground = Brush("text_color");

        // trackbar
        PvTrackLabel.Foreground = Brush("text_color");
        PvTrackValue.Foreground = Brush("on_text_color");
        PvTrackEmpty.Background = Brush("trackbar_empty_color");
        PvTrackFull.Background = Brush("trackbar_malleable_slider_color");
        PvTrackKnob.Background = Brush("trackbar_slider_malleable_color");

        // table
        var table = ParseHex(Get("table_bg_color"));
        PvTable.Background = new SolidColorBrush(Color.FromArgb(Alpha("table_bg_alpha"), table.R, table.G, table.B));
        PvTableL1.Foreground = Brush("table_section_text_color");
        PvTableL2.Foreground = Brush("table_section_text_color");
        PvTableV1.Foreground = Brush("table_info_text_color");
        PvTableV2.Foreground = Brush("table_info_text_color");

        // radio list
        PvRadioCode1.Foreground = Brush("on_text_color");
        PvRadioCode2.Foreground = Brush("on_text_color");
        PvRadioRing1.Stroke = Brush("s2_radio_selected_color");
        PvRadioRing1.Fill = Brush("s2_radio_selected_color");
        PvRadioDot1.Fill = Brush("s2_radio_inner_color");
        PvRadioRing2.Stroke = Brush("s2_radio_ring_color");
        PvRadioRing2.Fill = Brushes.Transparent;

        // bottom hints
        PvBackBadge.Background = Brush("bottom_button_color");
        PvOkBadge.Background = Brush("bottom_button_color");
        PvBackText.Foreground = Brush("bottom_text_color");
        PvOkText.Foreground = Brush("bottom_text_color");

        RefreshAnimations();
    }

    /// <summary>
    /// Rewrites the gradient colours in place; the rotation keeps running.
    ///
    /// Each animated border has its own key family — the selection, the status widget and the
    /// info boxes are separately themeable in Ultrahand, and driving them all from one set
    /// would misrepresent what the saved file does.
    /// </summary>
    private void RefreshAnimations()
    {
        ApplyStops(_cursorBrush, Highlights("s2_highlight_color_"));
        ApplyStops(_widgetBrush, Highlights("s2_widget_border_color_"));
        ApplyStops(_tableBrush, Highlights("s2_table_border_color_"));

        // Which key drives the ring around the slider knob while it is focused isn't
        // documented; the selection colours are the closest match to how it looks in use.
        ApplyStops(_knobBrush, Highlights("s2_highlight_color_"));

        _logoColor1 = ParseHex(Get("dynamic_logo_color_1"));
        _logoColor2 = ParseHex(Get("dynamic_logo_color_2"));
        StepPreviewAnimations();
    }

    private Color[] Highlights(string prefix) => new[]
    {
        ParseHex(Get(prefix + "1")),
        ParseHex(Get(prefix + "2")),
        ParseHex(Get(prefix + "3")),
        ParseHex(Get(prefix + "4")),
    };

    private static void ApplyStops(LinearGradientBrush brush, IReadOnlyList<Color> colors)
    {
        for (var i = 0; i < brush.GradientStops.Count; i++)
            brush.GradientStops[i].Color = colors[i % colors.Count];
    }

    // ---------- saving ----------

    private async void BrowseSd_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.T("Common.SelectSdRoot"),
            AllowMultiple = false
        });
        if (folders.Count == 0) return;

        SdPathBox.Text = folders[0].Path.LocalPath;

        // A different card can already carry a wallpaper, so the ticks are re-read rather
        // than left showing the previous card's state.
        RefreshWallpaperState();
    }

    private async Task<bool> EnsureSdPathAsync()
    {
        if (!string.IsNullOrWhiteSpace(SdRoot) && Directory.Exists(SdRoot)) return true;

        await MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Common.NoSdCard.Headline"),
            Loc.T("Theming.NoSdCard.Message"));
        return false;
    }

    private async void SaveTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureSdPathAsync()) return;

        try
        {
            var name = (ThemeNameBox.Text ?? "").Trim();
            UltrahandTheme.Save(SdRoot, name, _values);

            await MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.Saved.Headline"),
                Loc.T("Theming.Saved.Message", $"{UltrahandTheme.RelativeFolder}/{name}.ini"));
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.Error.SaveTheme"), ex.Message);
        }
    }

    // ---------- wallpaper ----------

    private async void ChooseWallpaper_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Theming.Hekate.PickerTitle"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Loc.T("Theming.Hekate.FilterImages"))
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.tif", "*.tiff", "*.webp" }
                },
                FilePickerFileTypes.All
            }
        });
        if (files.Count == 0) return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            var bitmap = new Bitmap(stream);

            _wallpaper = bitmap;
            WallpaperPreview.Source = bitmap;
            WallpaperHint.IsVisible = false;

            WallpaperStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB8));
            WallpaperStatus.Text = Loc.T("Theming.Hekate.ImageInfo", bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.Error.LoadImage"), ex.Message);
        }
    }

    private async void ApplyWallpaper_Click(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureSdPathAsync()) return;

        if (_wallpaper == null)
        {
            await MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.NoImage.Headline"),
                Loc.T("Theming.NoImage.Message"));
            return;
        }

        try
        {
            var target = Path.Combine(SdRoot, "bootloader", "res", "background.bmp");
            HekateWallpaper.Save(_wallpaper, target);

            WallpaperStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x9E));
            WallpaperStatus.Text = Loc.T("Theming.Hekate.Saved");
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.Error.WriteWallpaper"), ex.Message);
        }
    }

    /// <summary>Parses an Ultrahand RRGGBB value, falling back to white on anything unexpected.</summary>
    public static Color ParseHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6
            || !byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return Colors.White;
        }
        return Color.FromRgb(r, g, b);
    }
}
