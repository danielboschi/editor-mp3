using NAudio.Wave;

namespace Mp3Editor.Audio;

/// <summary>Toca um trecho do arquivo carregado (usado para a prévia).</summary>
public sealed class PlaybackService : IDisposable
{
    private AudioFileReader? _reader;
    private WaveOutEvent? _output;
    private double _from;

    /// <summary>Disparado quando o trecho termina de tocar sozinho (não ao chamar Stop).</summary>
    public event EventHandler? PlaybackEnded;

    public bool IsActive => _output != null;
    public bool IsPaused => _output?.PlaybackState == PlaybackState.Paused;

    /// <summary>Posição que está sendo ouvida agora, em segundos.</summary>
    public double Position
    {
        get
        {
            var o = _output;
            if (o == null) return _from;
            return _from + o.GetPosition() / (double)o.OutputWaveFormat.AverageBytesPerSecond;
        }
    }

    public void Load(string path)
    {
        Stop();
        _reader?.Dispose();
        _reader = null;
        _reader = new AudioFileReader(path);
    }

    public void Play(double from, double to)
    {
        if (_reader == null) return;
        Stop();

        _reader.CurrentTime = TimeSpan.FromSeconds(from);
        _from = from;

        var output = new WaveOutEvent { DesiredLatency = 200 };
        output.PlaybackStopped += (_, _) =>
        {
            if (!ReferenceEquals(output, _output)) return; // parado por nós
            _output = null;
            output.Dispose();
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        };
        output.Init(new RangeSampleProvider(_reader, to));
        _output = output;
        output.Play();
    }

    public void Pause() => _output?.Pause();

    public void Resume() => _output?.Play();

    public void Stop()
    {
        var output = _output;
        _output = null;
        if (output == null) return;
        output.Stop();
        output.Dispose();
    }

    public void Dispose()
    {
        Stop();
        _reader?.Dispose();
        _reader = null;
    }

    /// <summary>Lê do arquivo apenas até o tempo final indicado.</summary>
    private sealed class RangeSampleProvider : ISampleProvider
    {
        private readonly AudioFileReader _reader;
        private readonly long _endByte;

        public RangeSampleProvider(AudioFileReader reader, double end)
        {
            _reader = reader;
            long endByte = (long)(end * reader.WaveFormat.AverageBytesPerSecond);
            _endByte = endByte - endByte % reader.WaveFormat.BlockAlign;
        }

        public WaveFormat WaveFormat => _reader.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            long remaining = (_endByte - _reader.Position) / sizeof(float);
            if (remaining <= 0) return 0;
            int toRead = (int)Math.Min(count, remaining);
            toRead -= toRead % WaveFormat.Channels;
            return toRead <= 0 ? 0 : _reader.Read(buffer, offset, toRead);
        }
    }
}
