using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mp3Editor.Audio;

namespace Mp3Editor.Controls;

/// <summary>
/// Desenha a forma de onda com dois marcadores arrastáveis (início e fim do corte)
/// e a posição de reprodução.
/// </summary>
public sealed class WaveformControl : FrameworkElement
{
    private const double RulerHeight = 24;
    private const double MinGap = 0.05;   // segundos mínimos entre início e fim
    private const double GrabDistance = 8; // pixels

    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)));
    private static readonly Brush RulerBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x33)));
    private static readonly Brush DimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x14)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xAA)));
    private static readonly Brush StartBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0x8C)));
    private static readonly Brush EndBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C)));
    private static readonly Pen WavePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xFF)), 1));
    private static readonly Pen CenterPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x44)), 1));
    private static readonly Pen TickPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x6A)), 1));
    private static readonly Pen StartPen = Freeze(new Pen(StartBrush, 2));
    private static readonly Pen EndPen = Freeze(new Pen(EndBrush, 2));
    private static readonly Pen PlayheadPen = Freeze(new Pen(Brushes.White, 1.5));

    private enum DragTarget { None, Start, End }

    private WaveformData? _data;
    private double _start;
    private double _end;
    private double? _playhead;
    private DragTarget _drag;

    public event EventHandler? SelectionChanged;

    public double Start => _start;
    public double End => _end;
    public double Duration => _data?.Duration ?? 0;

    /// <summary>Posição de reprodução em segundos, ou null para esconder.</summary>
    public double? Playhead
    {
        get => _playhead;
        set { _playhead = value; InvalidateVisual(); }
    }

    public void SetData(WaveformData? data)
    {
        _data = data;
        _playhead = null;
        SetSelection(0, Duration);
    }

    public void SetSelection(double start, double end)
    {
        double duration = Duration;
        start = Math.Clamp(start, 0, duration);
        end = Math.Clamp(end, 0, duration);
        if (end < start) (start, end) = (end, start);

        _start = start;
        _end = end;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private double TimeToX(double t) => Duration > 0 ? t / Duration * ActualWidth : 0;

    private double XToTime(double x) =>
        ActualWidth > 0 ? Math.Clamp(x / ActualWidth * Duration, 0, Duration) : 0;

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));
        if (_data == null || w < 2 || h <= RulerHeight) return;

        double waveH = h - RulerHeight;
        double mid = waveH / 2;
        dc.DrawLine(CenterPen, new Point(0, mid), new Point(w, mid));

        // Forma de onda: uma linha vertical (mín → máx) por coluna de pixel.
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            int n = _data.Min.Length;
            int columns = (int)w;
            for (int px = 0; px < columns; px++)
            {
                int i0 = (int)((double)px / columns * n);
                int i1 = Math.Max(i0 + 1, (int)((double)(px + 1) / columns * n));
                float min = 0, max = 0;
                for (int i = i0; i < i1 && i < n; i++)
                {
                    if (_data.Min[i] < min) min = _data.Min[i];
                    if (_data.Max[i] > max) max = _data.Max[i];
                }
                double y1 = mid - max * mid * 0.95;
                double y2 = mid - min * mid * 0.95;
                if (y2 - y1 < 1) y2 = y1 + 1;
                ctx.BeginFigure(new Point(px + 0.5, y1), false, false);
                ctx.LineTo(new Point(px + 0.5, y2), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, WavePen, geometry);

        // Partes que serão removidas ficam escurecidas.
        double xs = TimeToX(_start), xe = TimeToX(_end);
        if (xs > 0) dc.DrawRectangle(DimBrush, null, new Rect(0, 0, xs, waveH));
        if (xe < w) dc.DrawRectangle(DimBrush, null, new Rect(xe, 0, w - xe, waveH));

        // Marcadores com "alças" para arrastar.
        dc.DrawLine(StartPen, new Point(xs, 0), new Point(xs, waveH));
        dc.DrawRectangle(StartBrush, null, new Rect(xs, 0, 12, 20));
        dc.DrawLine(EndPen, new Point(xe, 0), new Point(xe, waveH));
        dc.DrawRectangle(EndBrush, null, new Rect(xe - 12, 0, 12, 20));

        if (_playhead is double p)
        {
            double xp = TimeToX(p);
            dc.DrawLine(PlayheadPen, new Point(xp, 0), new Point(xp, waveH));
        }

        DrawRuler(dc, w, waveH);
    }

    private void DrawRuler(DrawingContext dc, double w, double top)
    {
        dc.DrawRectangle(RulerBrush, null, new Rect(0, top, w, RulerHeight));

        double duration = Duration;
        if (duration <= 0) return;

        double[] steps = { 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800 };
        double step = steps[^1];
        foreach (var s in steps)
        {
            if (s / duration * w >= 80) { step = s; break; }
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        for (int i = 0; ; i++)
        {
            double t = i * step;
            if (t > duration + 1e-9) break;
            double x = TimeToX(t);
            dc.DrawLine(TickPen, new Point(x, top), new Point(x, top + 6));
            var text = new FormattedText(TimeFormat.FormatShort(t, step), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 11, TextBrush, dpi);
            double tx = Math.Min(x + 3, w - text.Width - 2);
            dc.DrawText(text, new Point(tx, top + 5));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_data == null) return;

        double x = e.GetPosition(this).X;
        double ds = Math.Abs(x - TimeToX(_start));
        double de = Math.Abs(x - TimeToX(_end));
        // Clicar em qualquer lugar move o marcador mais próximo.
        _drag = ds < de || (ds == de && x < TimeToX(_start)) ? DragTarget.Start : DragTarget.End;
        CaptureMouse();
        ApplyDrag(x);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_data == null) return;

        double x = e.GetPosition(this).X;
        if (_drag != DragTarget.None)
        {
            ApplyDrag(x);
            return;
        }

        bool near = Math.Abs(x - TimeToX(_start)) <= GrabDistance || Math.Abs(x - TimeToX(_end)) <= GrabDistance;
        Cursor = near ? Cursors.SizeWE : Cursors.Hand;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _drag = DragTarget.None;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _drag = DragTarget.None;
    }

    private void ApplyDrag(double x)
    {
        double t = XToTime(x);
        if (_drag == DragTarget.Start)
            SetSelection(Math.Min(t, _end - MinGap), _end);
        else if (_drag == DragTarget.End)
            SetSelection(_start, Math.Max(t, _start + MinGap));
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
