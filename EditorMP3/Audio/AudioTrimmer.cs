using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Mp3Editor.Audio;

/// <summary>Corta o começo e o fim de um arquivo de áudio e salva o resultado.</summary>
public static class AudioTrimmer
{
    /// <summary>
    /// Salva o trecho [start, end] (em segundos) de <paramref name="input"/> em <paramref name="output"/>.
    /// O formato de saída é escolhido pela extensão (.mp3 ou .wav).
    /// Retorna uma descrição curta de como o arquivo foi gerado.
    /// </summary>
    public static string Save(string input, string output, double start, double end)
    {
        if (end <= start) throw new ArgumentException("O fim do corte precisa ser depois do início.");

        bool inputIsMp3 = HasExtension(input, ".mp3");
        bool inputIsWav = HasExtension(input, ".wav");
        bool outputIsMp3 = HasExtension(output, ".mp3");

        // Grava em um arquivo temporário e só substitui o destino no final,
        // para não deixar um arquivo pela metade se algo der errado.
        string temp = output + ".tmp" + Path.GetExtension(output);
        try
        {
            string description;
            if (outputIsMp3 && inputIsMp3)
            {
                TrimMp3Lossless(input, temp, start, end);
                description = "MP3 cortado sem recodificar (sem perda de qualidade)";
            }
            else if (!outputIsMp3 && inputIsWav)
            {
                TrimWavExact(input, temp, start, end);
                description = "WAV cortado com precisão de amostra";
            }
            else if (!outputIsMp3)
            {
                DecodeToWav(input, temp, start, end);
                description = "convertido para WAV 16 bits";
            }
            else
            {
                EncodeToMp3(input, temp, start, end);
                description = "codificado em MP3 192 kbps";
            }

            File.Move(temp, output, overwrite: true);
            return description;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// Copia os quadros (frames) MP3 que ficam dentro do trecho, sem decodificar.
    /// Não há perda de qualidade; a precisão é de um quadro (~26 ms a 44,1 kHz).
    /// </summary>
    public static void TrimMp3Lossless(string input, string output, double start, double end)
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
                outStream.Write(frame.RawData, 0, frame.RawData.Length);

            time += duration;
            if (time >= end) break;
        }
    }

    /// <summary>Corta um WAV copiando os bytes do trecho (precisão de uma amostra).</summary>
    public static void TrimWavExact(string input, string output, double start, double end)
    {
        using var reader = new WaveFileReader(input);
        var format = reader.WaveFormat;
        int align = format.BlockAlign;

        long startByte = Math.Clamp((long)(start * format.AverageBytesPerSecond), 0, reader.Length);
        long endByte = Math.Clamp((long)(end * format.AverageBytesPerSecond), 0, reader.Length);
        startByte -= startByte % align;
        endByte -= endByte % align;

        reader.Position = startByte;
        using var writer = new WaveFileWriter(output, format);

        var buffer = new byte[align * 16384];
        long remaining = endByte - startByte;
        while (remaining > 0)
        {
            int read = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0) break;
            writer.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    /// <summary>Decodifica o trecho e salva como WAV 16 bits.</summary>
    public static void DecodeToWav(string input, string output, double start, double end)
    {
        using var reader = new AudioFileReader(input);
        WaveFileWriter.CreateWaveFile16(output, Range(reader, start, end));
    }

    /// <summary>Decodifica o trecho e codifica em MP3 com o codificador do Windows.</summary>
    public static void EncodeToMp3(string input, string output, double start, double end)
    {
        MediaFoundationApi.Startup();
        using var reader = new AudioFileReader(input);
        MediaFoundationEncoder.EncodeToMp3(Range(reader, start, end).ToWaveProvider16(), output, 192000);
    }

    private static ISampleProvider Range(AudioFileReader reader, double start, double end)
    {
        reader.CurrentTime = TimeSpan.FromSeconds(start);
        return new OffsetSampleProvider(reader) { Take = TimeSpan.FromSeconds(end - start) };
    }

    private static bool HasExtension(string path, string ext) =>
        string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase);
}
