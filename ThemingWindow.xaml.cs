using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SwitchCfwTool.Services;

namespace SwitchCfwTool;

/// <summary>One editable row: the theme key plus its current value, shown with a swatch.</summary>
public class ThemeFieldItem : INotifyPropertyChanged
{
    public required ThemeField Field { get; init; }
    public string Label => Field.Label;

    public string? Group => Field.Group;
    public Visibility GroupVisibility => Field.Group == null ? Visibility.Collapsed : Visibility.Visible;

    private string _value = "FFFFFF";
    public string Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(Swatch)); OnPropertyChanged(nameof(Display)); }
    }

    public string Display => Field.IsAlpha ? Value : "#" + Value;

    public Brush Swatch => Field.IsAlpha
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
    public required BitmapSource Preview { get; init; }

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

    // Built in code, not XAML: WPF freezes brushes declared in markup, and a frozen brush
    // ignores animations.
    private readonly LinearGradientBrush _cursorBrush;
    private readonly LinearGradientBrush _widgetBrush;
    private readonly LinearGradientBrush _tableBrush;
    private readonly LinearGradientBrush _knobBrush;
    private readonly SolidColorBrush _logoBrush = new(Colors.White);

    private BitmapSource? _wallpaper;

    /// <summary>Destination shared by both tabs; read from the field at the top of the window.</summary>
    private string SdRoot => SdPathBox.Text.Trim();

    public ThemingWindow(string? initialPath)
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);

        if (!string.IsNullOrWhiteSpace(initialPath))
            SdPathBox.Text = initialPath;

        foreach (var field in UltrahandTheme.EditableFields)
        {
            _items.Add(new ThemeFieldItem
            {
                Field = field,
                Value = _values.TryGetValue(field.Key, out var v) ? v : "FFFFFF"
            });
        }

        FieldList.ItemsSource = _items;

        // Slightly different periods so the three frames don't march in lockstep.
        _cursorBrush = BuildRotatingBorder(4.5);
        _widgetBrush = BuildRotatingBorder(5.5);
        _tableBrush = BuildRotatingBorder(6.5);
        _knobBrush = BuildRotatingBorder(4.0);

        PvSelection.BorderBrush = _cursorBrush;
        PvWidget.BorderBrush = _widgetBrush;
        PvTable.BorderBrush = _tableBrush;
        PvTrackKnob.BorderBrush = _knobBrush;
        PvLogo.Foreground = _logoBrush;

        SelectField(_items[0]);
        RefreshPreview();
        LoadWallpapers();
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

    private void Wallpaper_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not WallpaperItem item) return;
        if (!EnsureSdPath()) return;

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
            MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.Error.ApplyWallpaper"), ex.Message);
        }
    }

    /// <summary>
    /// The border effect Ultrahand uses: all highlight colours are present in the frame at
    /// once as a gradient, and the whole gradient rotates so they appear to travel around
    /// the outline. Rotating the brush is what produces that — fading between the colours
    /// one at a time, as an earlier version did, looks nothing like it.
    ///
    /// The stop list closes on its starting colour so there's no seam where the gradient
    /// wraps, and the rotation runs continuously; only the stop colours are rewritten when
    /// the sliders move, which keeps the motion from stuttering on every adjustment.
    /// </summary>
    private static LinearGradientBrush BuildRotatingBorder(double secondsPerTurn)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };

        for (var i = 0; i < 5; i++)
            brush.GradientStops.Add(new GradientStop(Colors.White, i / 4.0));

        var rotate = new RotateTransform(0, 0.5, 0.5);
        brush.RelativeTransform = rotate;

        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(secondsPerTurn),
            RepeatBehavior = RepeatBehavior.Forever
        });

        return brush;
    }

    // ---------- editing ----------

    private void Field_Click(object sender, RoutedEventArgs e)
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
            RgbPanel.Visibility = Visibility.Collapsed;
            AlphaPanel.Visibility = Visibility.Visible;
            ASlider.Value = int.TryParse(item.Value, out var a) ? Math.Clamp(a, 0, 15) : 13;
        }
        else
        {
            RgbPanel.Visibility = Visibility.Visible;
            AlphaPanel.Visibility = Visibility.Collapsed;
            var c = ParseHex(item.Value);
            RSlider.Value = c.R;
            GSlider.Value = c.G;
            BSlider.Value = c.B;
        }

        _loadingField = false;
    }

    private void Channel_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
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
        PvRadioRing2.Fill = System.Windows.Media.Brushes.Transparent;

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

        // The logo alternates between two colours rather than carrying a moving gradient.
        var animation = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new LinearColorKeyFrame(ParseHex(Get("dynamic_logo_color_1")), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearColorKeyFrame(ParseHex(Get("dynamic_logo_color_2")), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.6))));
        animation.KeyFrames.Add(new LinearColorKeyFrame(ParseHex(Get("dynamic_logo_color_1")), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.2))));
        animation.Duration = TimeSpan.FromSeconds(3.2);
        _logoBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
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

    private void BrowseSd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Common.SelectSdRoot") };
        if (dialog.ShowDialog() != true) return;

        SdPathBox.Text = dialog.FolderName;

        // A different card can already carry a wallpaper, so the ticks are re-read rather
        // than left showing the previous card's state.
        RefreshWallpaperState();
    }

    private bool EnsureSdPath()
    {
        if (!string.IsNullOrWhiteSpace(SdRoot) && Directory.Exists(SdRoot)) return true;

        MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Common.NoSdCard.Headline"),
            Loc.T("Theming.NoSdCard.Message"));
        return false;
    }

    private void SaveTheme_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSdPath()) return;

        try
        {
            var name = ThemeNameBox.Text.Trim();
            UltrahandTheme.Save(SdRoot, name, _values);

            MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.Saved.Headline"),
                Loc.T("Theming.Saved.Message", $"{UltrahandTheme.RelativeFolder}/{name}.ini"));
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.Error.SaveTheme"), ex.Message);
        }
    }

    // ---------- wallpaper ----------

    private void ChooseWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Theming.Hekate.PickerTitle"),
            Filter = Loc.T("Theming.Hekate.FilterImages") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|"
                   + Loc.T("Theming.Hekate.FilterAll") + "|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(dialog.FileName);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;   // release the file again straight away
            bitmap.EndInit();

            _wallpaper = bitmap;
            WallpaperPreview.Source = bitmap;
            WallpaperHint.Visibility = Visibility.Collapsed;

            WallpaperStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB8));
            WallpaperStatus.Text = Loc.T("Theming.Hekate.ImageInfo", bitmap.PixelWidth, bitmap.PixelHeight);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.Error.LoadImage"), ex.Message);
        }
    }

    private void ApplyWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSdPath()) return;

        if (_wallpaper == null)
        {
            MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.NoImage.Headline"),
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
            MessageDialog.Show(this, Loc.T("Theming.Title"), Loc.T("Theming.Error.WriteWallpaper"), ex.Message);
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
