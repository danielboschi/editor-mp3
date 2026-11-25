using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mp3Editor.Audio;

namespace Mp3Editor.Controls;

/// <summary>
/// Forma de onda com as linhas de início/fim do corte e o cursor de reprodução.
/// As linhas de corte só se movem quando a própria linha (ou sua alça) é arrastada;
/// clicar ou arrastar em qualquer outro ponto move o cursor.
/// </summary>
/// <remarks>
/// Desempenho: a onda é desenhada uma única vez em um bitmap (refeito só quando o
/// tamanho ou o arquivo muda). Linhas e cursor são visuais separados, apenas
/// deslocados por cima; mover uma linha não redesenha a onda.
/// </remarks>
public sealed class WaveformControl : FrameworkElement
{
    public const double MinGap = 0.05;      // segundos mínimos entre início e fim

    private const double RulerHeight = 24;
    private const double GrabDistance = 6;  // pixels para "pegar" uma linha
    private const double HandleWidth = 12;
    private const double HandleHeight = 20;

    private const int BackgroundPixel = unchecked((int)0xFF1E1E24);
    private const int WavePixel = unchecked((int)0xFF4FA3FF);
    private const int CenterPixel = unchecked((int)0xFF3A3A44);

    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)));
    private static readonly Brush RulerBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x33)));
    private static readonly Brush DimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x14)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xAA)));
    private static readonly Brush StartBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0x8C)));
    private static readonly Brush EndBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C)));
    private static readonly Pen TickPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x6A)), 1));
    private static readonly Brush FadeBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x90, 0x10, 0x10, 0x14)));
    private static readonly Pen FadePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x57)), 1.5));
    private static readonly Typeface RulerTypeface = new("Segoe UI");

    private enum DragTarget { None, Start, End, Playhead }

    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _startVisual = new();
    private readonly DrawingVisual _endVisual = new();
    private readonly DrawingVisual _playheadVisual = new();

    private WaveformData? _data;
    private BitmapSource? _bitmap;
    private double _start;
    private double _end;
    private double _playhead;
    private Fade _fade;       // como pedido pelo usuário
    private Fade _drawnFade;  // como foi desenhado (encurtado se não couber no trecho)
    private DragTarget _drag;
    private double _dragOffset; // distância entre o clique e a linha, para ela não "pular"

    public WaveformControl()
    {
        _visuals = new VisualCollection(this) { _endVisual, _startVisual, _playheadVisual };
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    /// <summary>O trecho escolhido mudou.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>O usuário está arrastando o cursor (tempo em segundos).</summary>
    public event EventHandler<double>? PlayheadDragging;

    /// <summary>O usuário soltou o cursor na posição escolhida (tempo em segundos).</summary>
    public event EventHandler<double>? PlayheadMoved;

    public double Start => _start;
    public double End => _end;
    public double Duration => _data?.Duration ?? 0;
    /// <summary>Fades mostrados no gráfico (o áudio é tratado em outro lugar).</summary>
    public Fade Fade
    {
        get => _fade;
        set
        {
            _fade = value;
            PlaceOverlays();
        }
    }

    public bool IsDraggingPlayhead => _drag == DragTarget.Playhead;

    /// <summary>Posição do cursor em segundos. Ignorada enquanto o usuário o arrasta.</summary>
    public double Playhead
    {
        get => _playhead;
        set
        {
            if (IsDraggingPlayhead) return;
            MovePlayhead(value);
        }
    }

    public void SetData(WaveformData? data)
    {
        _data = data;
        _bitmap = null;
        _drag = DragTarget.None;
        _playhead = 0;
        InvalidateVisual();
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
        PlaceOverlays();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------- Desenho ----------

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _bitmap = null;
        InvalidateVisual();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _bitmap = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));

        double waveH = h - RulerHeight;
        if (_data == null || w < 2 || waveH < 2)
        {
            foreach (DrawingVisual v in _visuals) v.RenderOpen().Close();
            return;
        }

        _bitmap ??= BuildBitmap(_data, w, waveH);
        dc.DrawImage(_bitmap, new Rect(0, 0, w, waveH));
        DrawRuler(dc, w, waveH);
        DrawOverlays(w, waveH);
        PlaceOverlays();
    }

    /// <summary>Desenha a onda em um bitmap: uma coluna (mín → máx) por pixel da tela.</summary>
    private BitmapSource BuildBitmap(WaveformData data, double w, double waveH)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int columns = Math.Max(1, (int)Math.Round(w * dpi.DpiScaleX));
        int rows = Math.Max(1, (int)Math.Round(waveH * dpi.DpiScaleY));
        double mid = rows / 2.0;

        var pixels = new int[columns * rows];
        Array.Fill(pixels, BackgroundPixel);
        Array.Fill(pixels, CenterPixel, rows / 2 * columns, columns);

        int n = data.Min.Length;
        for (int x = 0; x < columns; x++)
        {
            int i0 = (int)((long)x * n / columns);
            int i1 = Math.Max(i0 + 1, (int)((long)(x + 1) * n / columns));
            float min = 0, max = 0;
            for (int i = i0; i < i1 && i < n; i++)
            {
                if (data.Min[i] < min) min = data.Min[i];
                if (data.Max[i] > max) max = data.Max[i];
            }

            int top = Math.Clamp((int)(mid - max * mid * 0.95), 0, rows - 1);
            int bottom = Math.Clamp((int)(mid - min * mid * 0.95), top, rows - 1);
            for (int y = top, p = top * columns + x; y <= bottom; y++, p += columns)
                pixels[p] = WavePixel;
        }

        var bitmap = BitmapSource.Create(columns, rows, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
            PixelFormats.Bgr32, null, pixels, columns * sizeof(int));
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Desenha linhas e cursor em x = 0; a posição vem do deslocamento de cada visual.</summary>
    private void DrawOverlays(double w, double waveH)
    {
        _drawnFade = _fade.FitTo(_end - _start);

        // A parte que será removida fica escurecida junto com a própria linha.
        using (var dc = _startVisual.RenderOpen())
        {
            dc.DrawRectangle(DimBrush, null, new Rect(-w, 0, w, waveH));
            DrawFade(dc, TimeToX(_drawnFade.In), waveH);
            dc.DrawRectangle(StartBrush, null, new Rect(-1, 0, 2, waveH));
            dc.DrawRectangle(StartBrush, null, new Rect(0, 0, HandleWidth, HandleHeight));
        }
        using (var dc = _endVisual.RenderOpen())
        {
            dc.DrawRectangle(DimBrush, null, new Rect(0, 0, w, waveH));
            DrawFade(dc, -TimeToX(_drawnFade.Out), waveH);
            dc.DrawRectangle(EndBrush, null, new Rect(-1, 0, 2, waveH));
            dc.DrawRectangle(EndBrush, null, new Rect(-HandleWidth, 0, HandleWidth, HandleHeight));
        }
        using (var dc = _playheadVisual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(-0.75, 0, 1.5, waveH));
            var grip = new StreamGeometry();
            using (var ctx = grip.Open())
            {
                ctx.BeginFigure(new Point(0, waveH - 2), true, true);
                ctx.LineTo(new Point(6, waveH + 8), false, false);
                ctx.LineTo(new Point(-6, waveH + 8), false, false);
            }
            grip.Freeze();
            dc.DrawGeometry(Brushes.White, null, grip);
        }
    }

    /// <summary>
    /// Curva do fade a partir da linha de corte (x = 0) até <paramref name="width"/>
    /// (negativo para o fade out). A parte do som que é abaixada fica escurecida.
    /// </summary>
    private static void DrawFade(DrawingContext dc, double width, double waveH)
    {
        if (Math.Abs(width) < 1) return;

        const int steps = 48;
        double mid = waveH / 2;
        foreach (int side in new[] { -1, 1 })
        {
            double edge = mid + side * mid;
            var shade = new StreamGeometry();
            var curve = new StreamGeometry();
            using (var s = shade.Open())
            using (var c = curve.Open())
            {
                s.BeginFigure(new Point(0, edge), true, true);
                c.BeginFigure(new Point(0, mid), false, false);
                for (int i = 0; i <= steps; i++)
                {
                    double x = (double)i / steps;
                    var p = new Point(width * x, mid + side * mid * Audio.Fade.Curve(x));
                    s.LineTo(p, false, false);
                    c.LineTo(p, true, true);
                }
            }
            shade.Freeze();
            curve.Freeze();
            dc.DrawGeometry(FadeBrush, null, shade);
            dc.DrawGeometry(null, FadePen, curve);
        }
    }

    private void PlaceOverlays()
    {
        // Os fades só são redesenhados quando o tamanho deles muda.
        double waveH = ActualHeight - RulerHeight;
        if (_bitmap != null && waveH >= 2 && _drawnFade != _fade.FitTo(_end - _start))
            DrawOverlays(ActualWidth, waveH);

        _startVisual.Offset = new Vector(SnapX(_start), 0);
        _endVisual.Offset = new Vector(SnapX(_end), 0);
        _playheadVisual.Offset = new Vector(SnapX(_playhead), 0);
    }

    private void MovePlayhead(double time)
    {
        _playhead = Math.Clamp(time, 0, Duration);
        _playheadVisual.Offset = new Vector(SnapX(_playhead), 0);
    }

    private double SnapX(double time)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return Math.Round(TimeToX(time) * scale) / scale;
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
        for (int i = 0; ; i++)
        {
            double t = i * step;
            if (t > duration + 1e-9) break;
            double x = TimeToX(t);
            dc.DrawLine(TickPen, new Point(x, top), new Point(x, top + 6));
            var text = new FormattedText(TimeFormat.FormatShort(t, step), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, RulerTypeface, 11, TextBrush, dpi);
            double tx = Math.Min(x + 3, w - text.Width - 2);
            dc.DrawText(text, new Point(tx, top + 5));
        }
    }

    private double TimeToX(double t) => Duration > 0 ? t / Duration * ActualWidth : 0;

    private double XToTime(double x) =>
        ActualWidth > 0 ? Math.Clamp(x / ActualWidth * Duration, 0, Duration) : 0;

    // ---------- Mouse ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_data == null) return;

        Focus(); // tira o foco das caixas de tempo
        PointerDown(e.GetPosition(this));
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_data == null) return;

        var p = e.GetPosition(this);
        if (_drag != DragTarget.None)
            PointerMove(p);
        else
            Cursor = HitTestTarget(p) == DragTarget.Playhead ? Cursors.Hand : Cursors.SizeWE;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        PointerUp();
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        PointerUp();
    }

    internal void PointerDown(Point p)
    {
        _drag = HitTestTarget(p);
        _dragOffset = _drag switch
        {
            DragTarget.Start => TimeToX(_start) - p.X,
            DragTarget.End => TimeToX(_end) - p.X,
            _ => 0,
        };
        if (_drag == DragTarget.Playhead) PointerMove(p);
    }

    internal void PointerMove(Point p)
    {
        double t = XToTime(p.X + _dragOffset);
        switch (_drag)
        {
            case DragTarget.Start:
                SetSelection(Math.Min(t, _end - MinGap), _end);
                break;
            case DragTarget.End:
                SetSelection(_start, Math.Max(t, _start + MinGap));
                break;
            case DragTarget.Playhead:
                MovePlayhead(t);
                PlayheadDragging?.Invoke(this, t);
                break;
        }
    }

    internal void PointerUp()
    {
        bool moved = IsDraggingPlayhead;
        _drag = DragTarget.None;
        if (moved) PlayheadMoved?.Invoke(this, _playhead);
    }

    /// <summary>Só a linha ou a alça pega o corte; o resto do gráfico é do cursor.</summary>
    private DragTarget HitTestTarget(Point p)
    {
        double ds = p.X - TimeToX(_start);
        double de = p.X - TimeToX(_end);
        bool inHandle = p.Y <= HandleHeight;
        bool onStart = Math.Abs(ds) <= GrabDistance || (inHandle && ds >= 0 && ds <= HandleWidth);
        bool onEnd = Math.Abs(de) <= GrabDistance || (inHandle && de <= 0 && de >= -HandleWidth);

        if (onStart && onEnd) return Math.Abs(ds) < Math.Abs(de) ? DragTarget.Start : DragTarget.End;
        if (onStart) return DragTarget.Start;
        if (onEnd) return DragTarget.End;
        return DragTarget.Playhead;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
