using System.IO;
using NAudio.Wave;

namespace Mp3Editor.Audio;

public enum AudioKind { Mp3, Wav, Other }

/// <summary>
/// Abre qualquer áudio que o Windows saiba decodificar. O formato é reconhecido pelo
/// conteúdo do arquivo, não pela extensão. Áudio com mais de 2 canais vira estéreo.
/// </summary>
public sealed class AudioSource : ISampleProvider, IDisposable
{
    private readonly WaveStream _stream;
    private readonly ISampleProvider _samples;
    private readonly object _lock = new();
    private long _frame;

    private AudioSource(WaveStream stream)
    {
        _stream = stream;
        var samples = stream.ToSampleProvider();
        _samples = samples.WaveFormat.Channels > 2 ? new StereoDownmix(samples) : samples;
    }

    public WaveFormat WaveFormat => _samples.WaveFormat;

    /// <summary>Próxima posição a ser lida, em quadros (uma amostra de cada canal).</summary>
    public long Frame { get { lock (_lock) return _frame; } }

    public static AudioSource Open(string path)
    {
        switch (Detect(path))
        {
            case AudioKind.Mp3:
                // Mesma contagem de quadros usada no corte sem perda: a prévia e o
                // arquivo salvo começam no mesmo ponto, inclusive em MP3 VBR.
                return TryOpen(() => new Mp3FileReader(path)) ?? OpenWithWindows(path);
            case AudioKind.Wav:
                return TryOpen(() => new WaveFileReader(path)) ?? OpenWithWindows(path);
            default:
                return OpenWithWindows(path);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            int read = _samples.Read(buffer, offset, count);
            _frame += read / WaveFormat.Channels;
            return read;
        }
    }

    public void Seek(double seconds)
    {
        lock (_lock)
        {
            long frame = Math.Max(0, (long)Math.Round(seconds * WaveFormat.SampleRate));
            _stream.Position = frame * _stream.WaveFormat.BlockAlign;
            _frame = frame;
        }
    }

    public void Dispose()
    {
        lock (_lock) _stream.Dispose();
    }

    /// <summary>Reconhece o formato pelos primeiros bytes do arquivo.</summary>
    public static AudioKind Detect(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var head = new byte[12];
        if (file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length) return AudioKind.Other;

        if ((HasText(head, 0, "RIFF") || HasText(head, 0, "RF64")) && HasText(head, 8, "WAVE")) return AudioKind.Wav;
        if (HasText(head, 4, "ftyp") || HasText(head, 0, "OggS") || HasText(head, 0, "fLaC") || HasText(head, 0, "RIFF"))
            return AudioKind.Other;

        // Pula a etiqueta ID3 (título, capa...) para chegar ao áudio.
        bool hasId3 = HasText(head, 0, "ID3");
        if (hasId3)
        {
            long size = (head[6] & 0x7F) << 21 | (head[7] & 0x7F) << 14 | (head[8] & 0x7F) << 7 | (head[9] & 0x7F);
            file.Position = 10 + size + ((head[5] & 0x10) != 0 ? 10 : 0);
            Array.Clear(head);
            file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            if (HasText(head, 0, "fLaC")) return AudioKind.Other;
        }

        // Sincronismo MPEG: camada III é MP3; camada 0 é AAC (ADTS).
        if (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0)
            return ((head[1] >> 1) & 3) == 1 ? AudioKind.Mp3 : AudioKind.Other;

        bool mp3Extension = string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase);
        return mp3Extension ? AudioKind.Mp3 : AudioKind.Other;
    }

    private static AudioSource? TryOpen(Func<WaveStream> open)
    {
        WaveStream? stream = null;
        try
        {
            stream = open();
            return new AudioSource(stream);
        }
        catch (Exception)
        {
            stream?.Dispose(); // variante incomum: deixa o Windows tentar
            return null;
        }
    }

    private static AudioSource OpenWithWindows(string path)
    {
        // Pelo nome primeiro; se a extensão não corresponder ao conteúdo, só pelo conteúdo.
        var source = TryOpen(() => new MediaFoundationReader(path));
        if (source != null) return source;

        FileStream? file = null;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new AudioSource(new StreamMediaFoundationReader(file));
        }
        catch (Exception ex)
        {
            file?.Dispose();
            throw new NotSupportedException(
                "O Windows não conseguiu ler este arquivo como áudio. O formato pode não ser suportado ou o arquivo pode estar danificado.", ex);
        }
    }

    private static bool HasText(byte[] data, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
            if (data[offset + i] != text[i]) return false;
        return true;
    }

    /// <summary>Mistura canais extras (5.1 etc.) nos dois canais frontais.</summary>
    private sealed class StereoDownmix : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _buffer = Array.Empty<float>();

        public StereoDownmix(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            if (_buffer.Length < frames * _channels) _buffer = new float[frames * _channels];
            frames = _source.Read(_buffer, 0, frames * _channels) / _channels;

            float scale = 1f / (1f + 0.5f * (_channels - 2));
            for (int f = 0; f < frames; f++)
            {
                int i = f * _channels;
                float extra = 0;
                for (int c = 2; c < _channels; c++) extra += _buffer[i + c] * 0.5f;
                buffer[offset + f * 2] = (_buffer[i] + extra) * scale;
                buffer[offset + f * 2 + 1] = (_buffer[i + 1] + extra) * scale;
            }
            return frames * 2;
        }
    }
}
