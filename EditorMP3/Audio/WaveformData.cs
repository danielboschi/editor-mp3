namespace Mp3Editor.Audio;

/// <summary>Resumo do áudio (picos mínimos/máximos) para desenhar a forma de onda.</summary>
public sealed class WaveformData
{
    private const int BucketsPerSecond = 400;

    public required float[] Min { get; init; }
    public required float[] Max { get; init; }
    public required double Duration { get; init; }
    public required int SampleRate { get; init; }
    public required int Channels { get; init; }

    /// <summary>
    /// Lê o áudio inteiro. A duração é contada pelas amostras decodificadas, porque a
    /// informada pelo arquivo pode ser só uma estimativa.
    /// </summary>
    public static WaveformData Load(AudioSource source)
    {
        int channels = source.WaveFormat.Channels;
        int rate = source.WaveFormat.SampleRate;
        int samplesPerBucket = Math.Max(1, rate / BucketsPerSecond) * channels;

        var min = new List<float>();
        var max = new List<float>();
        var buffer = new float[rate * channels];
        float lo = 0, hi = 0;
        int inBucket = 0;
        long total = 0;

        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            for (int i = 0; i < read; i++)
            {
                float v = buffer[i];
                if (v < lo) lo = v;
                if (v > hi) hi = v;
                if (++inBucket < samplesPerBucket) continue;
                min.Add(lo);
                max.Add(hi);
                lo = hi = 0;
                inBucket = 0;
            }
        }
        if (inBucket > 0)
        {
            min.Add(lo);
            max.Add(hi);
        }
        if (total == 0) throw new InvalidOperationException("O arquivo não contém áudio.");

        return new WaveformData
        {
            Min = min.ToArray(),
            Max = max.ToArray(),
            Duration = total / channels / (double)rate,
            SampleRate = rate,
            Channels = channels,
        };
    }
}
