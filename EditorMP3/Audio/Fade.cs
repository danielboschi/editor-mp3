namespace Mp3Editor.Audio;

/// <summary>Duração, em segundos, do fade in (começo) e do fade out (final). Zero desliga.</summary>
public readonly record struct Fade(double In, double Out)
{
    public bool IsNone => In <= 0 && Out <= 0;

    /// <summary>Encurta os fades, na mesma proporção, se não couberem no trecho.</summary>
    public Fade FitTo(double length)
    {
        double total = Math.Max(0, In) + Math.Max(0, Out);
        double scale = total > length && total > 0 ? Math.Max(0, length) / total : 1;
        return new Fade(Math.Max(0, In) * scale, Math.Max(0, Out) * scale);
    }

    /// <summary>Curva suave (meia onda de cosseno): 0 → silêncio, 1 → volume normal.</summary>
    public static double Curve(double x) => 0.5 - 0.5 * Math.Cos(Math.PI * Math.Clamp(x, 0, 1));
}

/// <summary>Fades aplicados ao trecho [Start, End] do áudio, em segundos.</summary>
public sealed class FadeRegion
{
    public FadeRegion(double start, double end, Fade fade)
    {
        Start = start;
        End = end;
        Fade = fade.FitTo(end - start);
    }

    public double Start { get; }
    public double End { get; }
    public Fade Fade { get; }

    /// <summary>Volume (0 a 1) no instante indicado do áudio original.</summary>
    public double GainAt(double time)
    {
        double gain = 1;
        if (Fade.In > 0 && time < Start + Fade.In) gain *= Fade.Curve((time - Start) / Fade.In);
        if (Fade.Out > 0 && time > End - Fade.Out) gain *= Fade.Curve((End - time) / Fade.Out);
        return gain;
    }

    /// <summary>Verdadeiro se algum ponto do intervalo tem o volume alterado.</summary>
    public bool Affects(double from, double to) =>
        (Fade.In > 0 && from < Start + Fade.In) || (Fade.Out > 0 && to > End - Fade.Out);

    /// <summary>Aplica o volume às amostras; <paramref name="firstFrame"/> é a posição da primeira no áudio original.</summary>
    public void Apply(float[] buffer, int offset, int count, int channels, long firstFrame, int sampleRate)
    {
        int frames = count / channels;
        if (!Affects(firstFrame / (double)sampleRate, (firstFrame + frames) / (double)sampleRate)) return;

        for (int f = 0, i = offset; f < frames; f++)
        {
            float gain = (float)GainAt((firstFrame + f) / (double)sampleRate);
            for (int c = 0; c < channels; c++) buffer[i++] *= gain;
        }
    }
}
