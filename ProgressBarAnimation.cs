using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SwitchCfwTool;

/// <summary>
/// Starts and stops a slow colour flow inside the progress bar's fill.
///
/// Done from code rather than in XAML because WPF freezes brushes declared inside a
/// ControlTemplate, and a frozen brush silently ignores animations. Building the brush here
/// keeps it mutable. The brush is the indicator's own Background, so the rounded corners of
/// the bar clip it automatically — no overlay element that could stick out at the ends.
///
/// All colours are visible at once and the band travels along the bar, matching the look of
/// Ultrahand's animated cursor.
/// </summary>
public static class ProgressBarAnimation
{
    private const string IndicatorPart = "PART_Indicator";

    public static void SetIsAnimating(ProgressBar bar, bool animate)
    {
        // The template may not be applied yet on the very first call after startup.
        if (!bar.IsLoaded)
        {
            bar.Loaded += Retry;
            return;
        }

        Apply(bar, animate);

        void Retry(object? sender, RoutedEventArgs e)
        {
            bar.Loaded -= Retry;
            Apply(bar, animate);
        }
    }

    private static void Apply(ProgressBar bar, bool animate)
    {
        bar.ApplyTemplate();
        if (bar.Template?.FindName(IndicatorPart, bar) is not Border indicator) return;

        if (!animate)
        {
            indicator.Background = BuildBrush(out _);
            return;
        }

        var brush = BuildBrush(out var shift);
        indicator.Background = brush;

        // Slides the repeating gradient sideways, so the whole colour band travels along the
        // bar with every colour visible at once — the same effect as Ultrahand's cursor,
        // rather than the bar fading from one colour to another.
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromSeconds(3.5),
            RepeatBehavior = RepeatBehavior.Forever
        });
    }

    private static LinearGradientBrush BuildBrush(out TranslateTransform shift)
    {
        shift = new TranslateTransform();

        // Ends on the starting colour so the repeated tiles meet seamlessly.
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            SpreadMethod = GradientSpreadMethod.Repeat,
            RelativeTransform = shift
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x5E, 0x8C, 0xF0), 0.33));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2D, 0xD4, 0xBF), 0.66));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 1.0));

        return brush;
    }
}
