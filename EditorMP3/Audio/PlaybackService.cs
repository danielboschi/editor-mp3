using NAudio.Wave;

namespace Mp3Editor.Audio;

/// <summary>Toca um trecho do arquivo carregado (usado para a prévia).</summary>
public sealed class PlaybackService : IDisposable
{
    private AudioSource? _source;
    private WaveOutEvent? _output;
    private RangeSampleProvider? _range;
    private double _from;

    /// <summary>Disparado quando o trecho termina de tocar sozinho (não ao chamar Stop).</summary>
    public event EventHandler? PlaybackEnded;

    public bool IsActive => _output != null;
    public bool IsPaused => _output?.PlaybackState == PlaybackState.Paused;
    public bool IsPlaying => IsActive && !IsPaused;

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

    /// <summary>Passa a tocar este áudio (e assume a responsabilidade de fechá-lo).</summary>
    public void Load(AudioSource source)
    {
        Stop();
        _source?.Dispose();
        _source = source;
        _from = 0;
    }

    public void Play(double from, double to)
    {
        if (_source == null) return;
        Stop();

        _source.Seek(from);
        _from = from;

        var range = new RangeSampleProvider(_source, to);
        var output = new WaveOutEvent { DesiredLatency = 200 };
        output.PlaybackStopped += (_, _) =>
        {
            if (!ReferenceEquals(output, _output)) return; // parado por nós
            _output = null;
            _range = null;
            output.Dispose();
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        };
        output.Init(range);
        _range = range;
        _output = output;
        output.Play();
    }

    /// <summary>Muda o ponto final do que está tocando, sem interromper o som.</summary>
    public void SetEnd(double to) => _range?.SetEnd(to);

    public void Pause() => _output?.Pause();

    public void Resume() => _output?.Play();

    public void Stop()
    {
        var output = _output;
        _output = null;
        _range?.Cancel();
        _range = null;
        if (output == null) return;
        output.Stop();
        output.Dispose();
    }

    public void Dispose()
    {
        Stop();
        _source?.Dispose();
        _source = null;
    }

    /// <summary>Lê do arquivo apenas até o tempo final indicado.</summary>
    private sealed class RangeSampleProvider : ISampleProvider
    {
        private readonly AudioSource _source;
        private readonly object _lock = new();
        private long _endFrame;
        private bool _cancelled;

        public RangeSampleProvider(AudioSource source, double end)
        {
            _source = source;
            SetEnd(end);
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public void SetEnd(double end)
        {
            lock (_lock) _endFrame = (long)Math.Round(end * WaveFormat.SampleRate);
        }

        /// <summary>Depois disto o trecho não lê mais nada, mesmo que o som ainda esteja encerrando.</summary>
        public void Cancel()
        {
            lock (_lock) _cancelled = true;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                if (_cancelled) return 0;
                int channels = WaveFormat.Channels;
                long remaining = (_endFrame - _source.Frame) * channels;
                if (remaining <= 0) return 0;
                int toRead = (int)Math.Min(count - count % channels, remaining);
                return _source.Read(buffer, offset, toRead);
            }
        }
    }
}
