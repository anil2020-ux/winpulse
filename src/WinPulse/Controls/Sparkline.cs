using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace WinPulse.Controls;

/// <summary>Lightweight area sparkline drawn with DirectX-friendly WPF geometry.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IEnumerable<double>),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _observed;

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var sparkline = (Sparkline)d;
        if (sparkline._observed is not null)
        {
            sparkline._observed.CollectionChanged -= sparkline.OnCollectionChanged;
        }

        sparkline._observed = e.NewValue as INotifyCollectionChanged;
        if (sparkline._observed is not null)
        {
            sparkline._observed.CollectionChanged += sparkline.OnCollectionChanged;
        }

        sparkline.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        var values = Values?.ToList();
        if (values is null || values.Count < 2)
        {
            return;
        }

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var maximum = MaxValue > 0 ? MaxValue : Math.Max(1, values.Max());
        var step = width / (values.Count - 1);

        var lineGeometry = new StreamGeometry();
        using (var geometry = lineGeometry.Open())
        {
            geometry.BeginFigure(new Point(0, Scale(values[0], height, maximum)), false, false);
            for (var index = 1; index < values.Count; index++)
            {
                geometry.LineTo(new Point(index * step, Scale(values[index], height, maximum)), true, false);
            }
        }

        lineGeometry.Freeze();

        if (Fill is not null)
        {
            var areaGeometry = new StreamGeometry();
            using (var geometry = areaGeometry.Open())
            {
                geometry.BeginFigure(new Point(0, height), true, true);
                geometry.LineTo(new Point(0, Scale(values[0], height, maximum)), true, false);
                for (var index = 1; index < values.Count; index++)
                {
                    geometry.LineTo(new Point(index * step, Scale(values[index], height, maximum)), true, false);
                }

                geometry.LineTo(new Point((values.Count - 1) * step, height), true, false);
            }

            areaGeometry.Freeze();
            context.DrawGeometry(Fill, null, areaGeometry);
        }

        var pen = new Pen(Stroke, 1.8)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        context.DrawGeometry(null, pen, lineGeometry);
    }

    private static double Scale(double value, double height, double maximum)
    {
        var normalized = Math.Clamp(value / maximum, 0, 1);
        return height - normalized * (height - 2) - 1;
    }
}
