using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Mp3Editor.Audio;

/// <summary>Corta o começo e o fim de um arquivo de áudio e salva o resultado.</summary>
public static class AudioTrimmer
{
    /// <summary>
    /// Salva o trecho [start, end] (em segundos) de <paramref name="input"/> em <paramref name="output"/>,
    /// com fade opcional. A entrada pode ser qualquer áudio que o Windows decodifique; a saída é .mp3 ou .wav.
    /// Retorna uma descrição curta de como o arquivo foi gerado.
    /// </summary>
    public static string Save(string input, string output, double start, double end, Fade fade = default)
    {
        if (end <= start) throw new ArgumentException("O fim do corte precisa ser depois do início.");

        bool outputIsMp3 = HasExtension(output, ".mp3");
        if (!outputIsMp3 && !HasExtension(output, ".wav"))
            throw new ArgumentException("O nome do arquivo salvo precisa terminar em .mp3 ou .wav.");

        var inputKind = AudioSource.Detect(input);
        var region = fade.IsNone ? null : new FadeRegion(start, end, fade);

        // Grava em um arquivo temporário e só substitui o destino no final,
        // para não deixar um arquivo pela metade se algo der errado.
        string temp = output + ".tmp" + Path.GetExtension(output);
        try
        {
            string description;
            if (outputIsMp3 && inputKind == AudioKind.Mp3)
            {
                TrimMp3Lossless(input, temp, start, end, region);
                description = "MP3 cortado sem recodificar (sem perda de qualidade)";
            }
            else if (!outputIsMp3 && inputKind == AudioKind.Wav && CanCopyWav(input))
            {
                TrimWavExact(input, temp, start, end, region);
                description = "WAV cortado com precisão de amostra";
            }
            else if (!outputIsMp3)
            {
                DecodeToWav(input, temp, start, end, region);
                description = "convertido para WAV 16 bits";
            }
            else
            {
                EncodeToMp3(input, temp, start, end, region);
                description = "convertido para MP3";
            }

            File.Move(temp, output, overwrite: true);
            return region == null ? description : description + ", com fade";
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// Copia os quadros (frames) MP3 que ficam dentro do trecho, sem decodificar.
    /// Não há perda de qualidade; a precisão é de um quadro (~26 ms a 44,1 kHz).
    /// No fade, só o campo de volume dos quadros envolvidos é alterado.
    /// </summary>
    private static void TrimMp3Lossless(string input, string output, double start, double end, FadeRegion? fade)
    {
        using var reader = new Mp3FileReader(input);
        using var outStream = File.Create(output);

        // Mantém as informações da música (título, artista, capa...).
        var tag = reader.Id3v2Tag?.RawData;
        if (tag != null) outStream.Write(tag, 0, tag.Length);

        reader.Position = 0;
        double time = 0;
        Mp3Frame? frame;
        while ((frame = reader.ReadNextFrame()) != null)
        {
            double duration = (double)frame.SampleCount / frame.SampleRate;
            double middle = time + duration / 2;
            if (middle >= start && middle < end)
            {
                if (fade != null && fade.Affects(time, time + 2 * duration))
                {
                    // Cada grânulo (576 amostras) se sobrepõe ao seguinte ao ser decodificado:
                    // o centro do que se ouve fica no final dele.
                    double frameStart = time, granule = 576.0 / frame.SampleRate;
                    Mp3FrameGain.Apply(frame.RawData, g => fade.GainAt(frameStart + (g + 1) * granule));
                }
                outStream.Write(frame.RawData, 0, frame.RawData.Length);
            }

            time += duration;
            if (time >= end) break;
        }
    }

    /// <summary>Corta um WAV copiando os bytes do trecho (precisão de uma amostra).</summary>
    private static void TrimWavExact(string input, string output, double start, double end, FadeRegion? fade)
    {
        using var reader = new WaveFileReader(input);
        var format = reader.WaveFormat;
        int align = format.BlockAlign;

        long startByte = Math.Clamp((long)Math.Round(start * format.SampleRate) * align, 0, reader.Length);
        long endByte = Math.Clamp((long)Math.Round(end * format.SampleRate) * align, 0, reader.Length);

        reader.Position = startByte;
        using var writer = new WaveFileWriter(output, format);

        bool isFloat = IsFloat(format);
        var buffer = new byte[align * 16384];
        long position = startByte;
        while (position < endByte)
        {
            int read = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, endByte - position));
            if (read <= 0) break;
            if (fade != null) ApplyFadeToPcm(buffer, read, format, isFloat, position / align, fade);
            writer.Write(buffer, 0, read);
            position += read;
        }
    }

    /// <summary>Aplica o fade direto nos bytes, mantendo a resolução original do WAV.</summary>
    private static void ApplyFadeToPcm(byte[] data, int count, WaveFormat format, bool isFloat, long firstFrame, FadeRegion fade)
    {
        double rate = format.SampleRate;
        int align = format.BlockAlign, frames = count / align;
        if (!fade.Affects(firstFrame / rate, (firstFrame + frames) / rate)) return;

        int size = format.BitsPerSample / 8;
        for (int f = 0; f < frames; f++)
        {
            double gain = fade.GainAt((firstFrame + f) / rate);
            for (int i = f * align; i < (f + 1) * align; i += size)
            {
                var s = data.AsSpan(i, size);
                if (isFloat && size == 4) BitConverter.TryWriteBytes(s, (float)(BitConverter.ToSingle(s) * gain));
                else if (isFloat) BitConverter.TryWriteBytes(s, BitConverter.ToDouble(s) * gain);
                else if (size == 1) s[0] = (byte)Math.Round(128 + (s[0] - 128) * gain); // 8 bits é sem sinal
                else if (size == 2) BitConverter.TryWriteBytes(s, (short)Math.Round(BitConverter.ToInt16(s) * gain));
                else if (size == 4) BitConverter.TryWriteBytes(s, (int)Math.Round(BitConverter.ToInt32(s) * gain));
                else
                {
                    int v = s[0] | s[1] << 8 | (sbyte)s[2] << 16; // 24 bits
                    v = (int)Math.Round(v * gain);
                    s[0] = (byte)v;
                    s[1] = (byte)(v >> 8);
                    s[2] = (byte)(v >> 16);
                }
            }
        }
    }

    /// <summary>Decodifica o trecho e salva como WAV 16 bits.</summary>
    private static void DecodeToWav(string input, string output, double start, double end, FadeRegion? fade)
    {
        using var source = AudioSource.Open(input);
        WaveFileWriter.CreateWaveFile16(output, Range(source, start, end, fade));
    }

    /// <summary>Decodifica o trecho e codifica em MP3 com o codificador do Windows.</summary>
    private static void EncodeToMp3(string input, string output, double start, double end, FadeRegion? fade)
    {
        MediaFoundationApi.Startup();
        using var source = AudioSource.Open(input);
        var samples = Range(source, start, end, fade);

        // O codificador MP3 só aceita algumas taxas de amostragem.
        if (samples.WaveFormat.SampleRate is not (32000 or 44100 or 48000))
            samples = new WdlResamplingSampleProvider(samples, 44100);

        MediaFoundationEncoder.EncodeToMp3(samples.ToWaveProvider16(), output, 192000);
    }

    private static ISampleProvider Range(AudioSource source, double start, double end, FadeRegion? fade)
    {
        source.Seek(start);
        ISampleProvider samples = fade == null ? source : new FadeSampleProvider(source, fade);
        return new OffsetSampleProvider(samples) { Take = TimeSpan.FromSeconds(end - start) };
    }

    /// <summary>WAV comprimido (ADPCM etc.) não pode ser cortado por cópia direta.</summary>
    private static bool CanCopyWav(string input)
    {
        using var reader = new WaveFileReader(input);
        var f = reader.WaveFormat;
        if (f.BlockAlign != f.Channels * (f.BitsPerSample / 8)) return false;
        return f.Encoding switch
        {
            WaveFormatEncoding.Pcm => f.BitsPerSample is 8 or 16 or 24 or 32,
            WaveFormatEncoding.IeeeFloat => f.BitsPerSample is 32 or 64,
            WaveFormatEncoding.Extensible => ExtensibleTag(f) switch
            {
                WaveFormatEncoding.Pcm => f.BitsPerSample is 8 or 16 or 24 or 32,
                WaveFormatEncoding.IeeeFloat => f.BitsPerSample is 32 or 64,
                _ => false,
            },
            _ => false,
        };
    }

    private static bool IsFloat(WaveFormat f) =>
        f.Encoding == WaveFormatEncoding.IeeeFloat ||
        (f.Encoding == WaveFormatEncoding.Extensible && ExtensibleTag(f) == WaveFormatEncoding.IeeeFloat);

    /// <summary>Formato real de um WAV "extensible": os 2 primeiros bytes do subformato.</summary>
    private static WaveFormatEncoding ExtensibleTag(WaveFormat f) =>
        f is WaveFormatExtraData { ExtraSize: >= 22 } extra
            ? (WaveFormatEncoding)BitConverter.ToUInt16(extra.ExtraData, 6)
            : WaveFormatEncoding.Unknown;

    private static bool HasExtension(string path, string ext) =>
        string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase);

    private sealed class FadeSampleProvider : ISampleProvider
    {
        private readonly AudioSource _source;
        private readonly FadeRegion _fade;

        public FadeSampleProvider(AudioSource source, FadeRegion fade)
        {
            _source = source;
            _fade = fade;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            long frame = _source.Frame;
            int read = _source.Read(buffer, offset, count);
            _fade.Apply(buffer, offset, read, WaveFormat.Channels, frame, WaveFormat.SampleRate);
            return read;
        }
    }
}
