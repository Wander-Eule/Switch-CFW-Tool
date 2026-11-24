using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Path = Avalonia.Controls.Shapes.Path;

namespace SwitchCfwTool;

/// <summary>One stop on the tour: what to point at and what to say about it.</summary>
public class TourStep
{
    /// <summary>Resolved when the step is shown, so late-created elements still work.</summary>
    public required Func<Control?> Target { get; init; }

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
/// </summary>
public class QuickTour
{
    private static readonly IBrush Shade = new SolidColorBrush(Color.FromArgb(0xD0, 0x0D, 0x0D, 0x1C));
    private static readonly IBrush Teal = new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB8));
    private static readonly IBrush Text = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xF7));
    private static readonly FontFamily UiFont = new("Inter, Segoe UI, Arial, sans-serif");

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

        _host.SizeChanged += (_, _) => QueueRefresh();
        _owner.PositionChanged += (_, _) => QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_index < 0) return;
        Dispatcher.UIThread.Post(ShowCurrent, DispatcherPriority.Loaded);
    }

    public void Start(IEnumerable<TourStep> steps)
    {
        _steps.Clear();
        _steps.AddRange(steps);
        if (_steps.Count == 0) return;

        Build();
        _index = 0;
        _host.IsVisible = true;

        // The layer was collapsed until now, so it has no size yet and measuring against it
        // would place everything at the top-left corner. Forcing a layout pass first is what
        // makes the very first step land correctly instead of only from the second run on.
        _host.UpdateLayout();
        ShowCurrent();
    }

    public void Stop()
    {
        _index = -1;
        _host.IsVisible = false;
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
        _host.Cursor = new Cursor(StandardCursorType.Hand);

        _shade = new Path { Fill = Shade, IsHitTestVisible = false };
        _host.Children.Add(_shade);

        _ring = new Border
        {
            BorderBrush = Teal,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }
            }
        };
        _host.Children.Add(_ring);

        _body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text,
            FontSize = 13,
            LineHeight = 19,
            FontFamily = UiFont
        };

        _counter = new TextBlock
        {
            Foreground = Muted,
            FontSize = 10.5,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 7),
            FontFamily = UiFont
        };

        var hint = new TextBlock
        {
            Text = Loc.T("Tour.Hint"),
            Foreground = Muted,
            FontSize = 10.5,
            Margin = new Thickness(0, 10, 0, 0),
            FontFamily = UiFont
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
            Child = stack,
            Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }
            }
        };
        _host.Children.Add(_toast);

        _host.PointerPressed -= OnClick;
        _host.PointerPressed += OnClick;

        _owner.RemoveHandler(InputElement.KeyDownEvent, OnKey);
        _owner.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    private void OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
            Next();
    }

    private void OnKey(object? sender, KeyEventArgs e)
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

        var size = _host.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            Dispatcher.UIThread.Post(ShowCurrent, DispatcherPriority.Loaded);
            return;
        }

        var hole = Measure(step, size);

        _shade.Data = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, size.Width, size.Height)),
            new RectangleGeometry(hole));

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

        if (target is not { IsVisible: true } || target.Bounds.Width <= 0)
        {
            // Nothing to point at — centre the cut-out so the text still has a home.
            return new Rect(hostSize.Width / 2 - 60, hostSize.Height / 2 - 30, 120, 60);
        }

        var origin = target.TranslatePoint(new Point(0, 0), _host) ?? new Point(0, 0);
        var width = step.MaxWidth is { } maxW ? Math.Min(target.Bounds.Width, maxW) : target.Bounds.Width;
        var height = step.MaxHeight is { } maxH ? Math.Min(target.Bounds.Height, maxH) : target.Bounds.Height;

        var padX = step.PaddingX ?? step.Padding;

        var rect = new Rect(
            origin.X - padX,
            origin.Y - step.Padding,
            width + padX * 2,
            height + step.Padding * 2);

        return rect.Intersect(new Rect(0, 0, hostSize.Width, hostSize.Height));
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

    private static void Fade(Visual element)
    {
        element.Opacity = 0;
        Dispatcher.UIThread.Post(() => element.Opacity = 1, DispatcherPriority.Render);
    }
}
