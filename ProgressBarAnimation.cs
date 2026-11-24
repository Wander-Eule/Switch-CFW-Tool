using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SwitchCfwTool;

/// <summary>
/// Starts and stops a slow colour flow inside the progress bar's fill.
/// </summary>
public static class ProgressBarAnimation
{
    private const double TileWidth = 200.0;
    private const double PeriodSeconds = 3.5;

    private static readonly Dictionary<ProgressBar, DispatcherTimer> ActiveTimers = new();

    public static void SetIsAnimating(ProgressBar bar, bool animate)
    {
        // The template may not be applied yet on the very first call after startup.
        if (!bar.IsLoaded)
        {
            bar.Loaded += Retry;
            return;
        }

        Apply(bar, animate);

        void Retry(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            bar.Loaded -= Retry;
            Apply(bar, animate);
        }
    }

    private static void Apply(ProgressBar bar, bool animate)
    {
        StopTimer(bar);

        var brush = BuildBrush(out var shift);
        bar.Foreground = brush;

        if (!animate) return;

        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = (stopwatch.Elapsed.TotalSeconds % PeriodSeconds) / PeriodSeconds;
            shift.X = t * TileWidth;
        };

        void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            bar.DetachedFromVisualTree -= OnDetached;
            StopTimer(bar);
        }

        bar.DetachedFromVisualTree += OnDetached;
        ActiveTimers[bar] = timer;
        timer.Start();
    }

    private static void StopTimer(ProgressBar bar)
    {
        if (ActiveTimers.Remove(bar, out var existing))
            existing.Stop();
    }

    private static LinearGradientBrush BuildBrush(out TranslateTransform shift)
    {
        shift = new TranslateTransform();

        // Ends on the starting colour so the repeated tiles meet seamlessly.
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(TileWidth, 0, RelativeUnit.Absolute),
            SpreadMethod = GradientSpreadMethod.Repeat,
            Transform = shift
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x5E, 0x8C, 0xF0), 0.33));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2D, 0xD4, 0xBF), 0.66));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 1.0));

        return brush;
    }
}
