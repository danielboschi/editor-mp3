using NAudio.Wave;

namespace Mp3Editor.Audio;

/// <summary>Resumo do áudio (picos mínimos/máximos) para desenhar a forma de onda.</summary>
public sealed class WaveformData
{
    public required float[] Min { get; init; }
    public required float[] Max { get; init; }
    public required double Duration { get; init; }
    public required int SampleRate { get; init; }
    public required int Channels { get; init; }

    public static WaveformData Load(string path, int buckets)
    {
        using var reader = new AudioFileReader(path);
        int channels = reader.WaveFormat.Channels;

        long totalFrames = Math.Max(1, reader.Length / sizeof(float) / channels);
        long framesPerBucket = Math.Max(1, totalFrames / buckets);
        int count = (int)Math.Ceiling(totalFrames / (double)framesPerBucket);

        var min = new float[count];
        var max = new float[count];

        var buffer = new float[reader.WaveFormat.SampleRate * channels];
        long frameIndex = 0;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                int bucket = (int)Math.Min(count - 1, (frameIndex + i / channels) / framesPerBucket);
                float v = buffer[i];
                if (v < min[bucket]) min[bucket] = v;
                if (v > max[bucket]) max[bucket] = v;
            }
            frameIndex += read / channels;
        }

        return new WaveformData
        {
            Min = min,
            Max = max,
            Duration = reader.TotalTime.TotalSeconds,
            SampleRate = reader.WaveFormat.SampleRate,
            Channels = channels,
        };
    }
}
