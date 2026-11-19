using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace FixMate.App.Controls;

public sealed class LiveLineChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IEnumerable),
        typeof(LiveLineChart),
        new FrameworkPropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(LiveLineChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumValueProperty = DependencyProperty.Register(
        nameof(MaximumValue),
        typeof(double),
        typeof(LiveLineChart),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AutoScaleProperty = DependencyProperty.Register(
        nameof(AutoScale),
        typeof(bool),
        typeof(LiveLineChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _notifyingValues;

    public LiveLineChart()
    {
        Loaded += (_, _) => SubscribeToValues();
        Unloaded += (_, _) => UnsubscribeFromValues();
    }

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double MaximumValue
    {
        get => (double)GetValue(MaximumValueProperty);
        set => SetValue(MaximumValueProperty, value);
    }

    public bool AutoScale
    {
        get => (bool)GetValue(AutoScaleProperty);
        set => SetValue(AutoScaleProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 165, 177, 194)), 1);
        for (var index = 1; index <= 3; index++)
        {
            var y = bounds.Height * index / 4;
            drawingContext.DrawLine(gridPen, new Point(0, y), new Point(bounds.Width, y));
        }

        if (Values is null)
        {
            return;
        }

        var points = Values.Cast<object?>()
            .Select(value => value is double number ? number : 0d)
            .ToArray();
        if (points.Length < 2)
        {
            return;
        }

        var maximum = AutoScale
            ? Math.Max(1, points.Max())
            : MaximumValue > 0 ? MaximumValue : 1;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index < points.Length; index++)
            {
                var x = bounds.Width * index / (points.Length - 1);
                var normalized = Math.Clamp(points[index] / maximum, 0, 1);
                var y = bounds.Height * (1 - normalized);
                if (index == 0)
                {
                    context.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    context.LineTo(new Point(x, y), true, false);
                }
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, new Pen(Stroke, 2), geometry);
    }

    private static void OnValuesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var chart = (LiveLineChart)dependencyObject;
        chart.UnsubscribeFromValues();
        chart._notifyingValues = e.NewValue as INotifyCollectionChanged;
        chart.SubscribeToValues();
        chart.InvalidateVisual();
    }

    private void SubscribeToValues()
    {
        if (IsLoaded && _notifyingValues is not null)
        {
            _notifyingValues.CollectionChanged += OnCollectionChanged;
        }
    }

    private void UnsubscribeFromValues()
    {
        if (_notifyingValues is not null)
        {
            _notifyingValues.CollectionChanged -= OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }
}
