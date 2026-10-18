using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SwitchCfwTool;

/// <summary>One stop on the tour: what to point at and what to say about it.</summary>
public class TourStep
{
    /// <summary>Resolved when the step is shown, so late-created elements still work.</summary>
    public required Func<FrameworkElement?> Target { get; init; }

    public required string Text { get; init; }

    /// <summary>Limits the cut-out height, for pointing at the top of a long list.</summary>
    public double? MaxHeight { get; init; }

    /// <summary>Limits the cut-out width, for pointing at one column of a wide row.</summary>
    public double? MaxWidth { get; init; }

    /// <summary>Extra space around the element, in device-independent pixels.</summary>
    public double Padding { get; init; } = 6;

    /// <summary>Horizontal spacing when it needs to differ from <see cref="Padding"/>.</summary>
    public double? PaddingX { get; init; }
}

/// <summary>
/// A guided walkthrough drawn directly into the main window.
///
/// It isn't a separate window on purpose: a real window would sit in front as its own
/// rectangle and couldn't dim the app around a cut-out. Instead a full-size layer is placed
/// over the existing content, filled with a translucent dark shape that has a hole punched
/// where the highlighted control is — so the control keeps showing through in its normal
/// colours while everything else recedes.
/// </summary>
public class QuickTour
{
    private static readonly Brush Shade = new SolidColorBrush(Color.FromArgb(0xD0, 0x0D, 0x0D, 0x1C));
    private static readonly Brush Teal = new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB8));
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xF7));

    private readonly Window _owner;
    private readonly Grid _host;
    private readonly List<TourStep> _steps = new();

    private Path? _shade;
    private Border? _ring;
    private Border? _toast;
    private TextBlock? _body;
    private TextBlock? _counter;

    private int _index = -1;

    /// <summary>Raised only when the last step was passed, not when the tour is cancelled.</summary>
    public event Action? Completed;

    public QuickTour(Window owner, Grid host)
    {
        _owner = owner;
        _host = host;

        // Recomputed whenever the window changes shape or place. Both are deferred to Loaded
        // priority, which runs after the layout pass: measuring straight from the event ran
        // while the content was still being arranged, so the target reported position (0,0)
        // and the cut-out jumped to the top-left corner.
        _owner.SizeChanged += (_, _) => QueueRefresh();
        _owner.LocationChanged += (_, _) => QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_index < 0) return;
        _host.Dispatcher.BeginInvoke(new Action(ShowCurrent), DispatcherPriority.Loaded);
    }

    public void Start(IEnumerable<TourStep> steps)
    {
        _steps.Clear();
        _steps.AddRange(steps);
        if (_steps.Count == 0) return;

        Build();
        _index = 0;
        _host.Visibility = Visibility.Visible;

        // The layer was collapsed until now, so it has no size yet and measuring against it
        // would place everything at the top-left corner. Forcing a layout pass first is what
        // makes the very first step land correctly instead of only from the second run on.
        _host.UpdateLayout();
        ShowCurrent();
    }

    public void Stop()
    {
        _index = -1;
        _host.Visibility = Visibility.Collapsed;
        _host.Children.Clear();
    }

    private void Next()
    {
        if (_index < 0) return;

        _index++;
        if (_index >= _steps.Count)
        {
            Stop();
            Completed?.Invoke();
            return;
        }

        ShowCurrent();
    }

    // ---------- visuals ----------

    private void Build()
    {
        _host.Children.Clear();
        _host.Background = Brushes.Transparent;   // needed so the layer receives clicks
        _host.Cursor = Cursors.Hand;

        _shade = new Path { Fill = Shade, IsHitTestVisible = false };
        _host.Children.Add(_shade);

        _ring = new Border
        {
            BorderBrush = Teal,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        _host.Children.Add(_ring);

        _body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text,
            FontSize = 13,
            LineHeight = 19,
            FontFamily = new FontFamily("Segoe UI")
        };

        _counter = new TextBlock
        {
            Foreground = Muted,
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 7),
            FontFamily = new FontFamily("Segoe UI")
        };

        var hint = new TextBlock
        {
            Text = Loc.T("Tour.Hint"),
            Foreground = Muted,
            FontSize = 10.5,
            Margin = new Thickness(0, 10, 0, 0),
            FontFamily = new FontFamily("Segoe UI")
        };

        var stack = new StackPanel();
        stack.Children.Add(_counter);
        stack.Children.Add(_body);
        stack.Children.Add(hint);

        _toast = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x42)),
            BorderBrush = Teal,
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 13, 16, 13),
            MaxWidth = 340,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = stack
        };
        _host.Children.Add(_toast);

        _host.MouseLeftButtonDown -= OnClick;
        _host.MouseLeftButtonDown += OnClick;

        _owner.PreviewKeyDown -= OnKey;
        _owner.PreviewKeyDown += OnKey;
    }

    private void OnClick(object sender, MouseButtonEventArgs e) => Next();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_index < 0) return;

        if (e.Key == Key.Escape) { Stop(); e.Handled = true; }
        else if (e.Key is Key.Enter or Key.Space or Key.Right) { Next(); e.Handled = true; }
    }

    private void ShowCurrent()
    {
        if (_shade == null || _ring == null || _toast == null || _body == null || _counter == null) return;

        var step = _steps[_index];

        // Guarantees the target's position is current before it's read. Safe here because
        // every path into this method is already deferred past the layout pass.
        _host.UpdateLayout();

        var size = _host.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            // Still no size (can happen on the very first frame): retry once layout settles.
            _host.Dispatcher.BeginInvoke(new Action(ShowCurrent), DispatcherPriority.Loaded);
            return;
        }

        var hole = Measure(step, size);

        // Outer rectangle plus the cut-out, combined with the even-odd rule: the overlap of
        // the two shapes stays unfilled, which is what leaves the control visible.
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, size.Width, size.Height)));
        group.Children.Add(new RectangleGeometry(hole, 10, 10));
        _shade.Data = group;

        _ring.Margin = new Thickness(hole.X, hole.Y, 0, 0);
        _ring.Width = hole.Width;
        _ring.Height = hole.Height;

        _counter.Text = Loc.T("Tour.Counter", _index + 1, _steps.Count);
        _body.Text = step.Text;

        PlaceToast(hole, size);
        Fade(_toast);
        Fade(_ring);
    }

    /// <summary>Bounds of the highlighted element in the overlay's own coordinates.</summary>
    private Rect Measure(TourStep step, Size hostSize)
    {
        var target = step.Target();

        if (target is not { IsVisible: true } || target.ActualWidth <= 0)
        {
            // Nothing to point at — centre the cut-out so the text still has a home.
            return new Rect(hostSize.Width / 2 - 60, hostSize.Height / 2 - 30, 120, 60);
        }

        var origin = target.TransformToVisual(_host).Transform(new Point(0, 0));
        var width = step.MaxWidth is { } maxW ? Math.Min(target.ActualWidth, maxW) : target.ActualWidth;
        var height = step.MaxHeight is { } maxH ? Math.Min(target.ActualHeight, maxH) : target.ActualHeight;

        var padX = step.PaddingX ?? step.Padding;

        var rect = new Rect(
            origin.X - padX,
            origin.Y - step.Padding,
            width + padX * 2,
            height + step.Padding * 2);

        // Keep it inside the window, otherwise the ring can be drawn off-screen.
        return Rect.Intersect(rect, new Rect(0, 0, hostSize.Width, hostSize.Height));
    }

    private void PlaceToast(Rect hole, Size hostSize)
    {
        if (_toast == null) return;

        _toast.Measure(new Size(340, double.PositiveInfinity));
        var toast = _toast.DesiredSize;

        const double gap = 14;

        // Below the cut-out where there's room, above it otherwise.
        var top = hole.Bottom + gap;
        if (top + toast.Height > hostSize.Height - 10)
            top = hole.Top - gap - toast.Height;

        top = Math.Clamp(top, 10, Math.Max(10, hostSize.Height - toast.Height - 10));

        // Aligned to the element, then pulled back inside the window if it would overflow.
        var left = hole.X;
        if (left + toast.Width > hostSize.Width - 10)
            left = hostSize.Width - toast.Width - 10;
        left = Math.Max(10, left);

        _toast.Margin = new Thickness(left, top, 0, 0);
    }

    private static void Fade(UIElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(180)
        });
    }
}
