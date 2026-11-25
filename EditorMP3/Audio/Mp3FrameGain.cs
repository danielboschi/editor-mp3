namespace Mp3Editor.Audio;

/// <summary>
/// Muda o volume de um quadro MP3 (camada III) sem decodificar: altera só o campo
/// "global_gain" de cada grânulo, em passos de 1,5 dB. O áudio comprimido não é tocado.
/// </summary>
public static class Mp3FrameGain
{
    private const double StepDb = 1.50514997831991; // 20·log10(2^(1/4))

    /// <param name="frame">Quadro completo, modificado no próprio vetor.</param>
    /// <param name="gainOfGranule">Volume (0 a 1) de cada grânulo do quadro.</param>
    public static void Apply(byte[] frame, Func<int, double> gainOfGranule)
    {
        if (frame.Length < 4 || ((frame[1] >> 1) & 3) != 1)
            throw new NotSupportedException("O fade sem recodificar só funciona em MP3 (camada III).");

        bool mpeg1 = ((frame[1] >> 3) & 3) == 3;
        bool mono = frame[3] >> 6 == 3;
        bool hasCrc = (frame[1] & 1) == 0;
        int channels = mono ? 1 : 2;

        int side = hasCrc ? 6 : 4;
        int sideLength = mpeg1 ? (mono ? 17 : 32) : (mono ? 9 : 17);
        if (frame.Length < side + sideLength) return;

        // Início do primeiro grânulo dentro das informações laterais.
        int bit = side * 8 + (mpeg1 ? 9 + (mono ? 5 : 3) + 4 * channels : 8 + (mono ? 1 : 2));
        int granules = mpeg1 ? 2 : 1;
        int blockBits = mpeg1 ? 59 : 63;

        for (int granule = 0; granule < granules; granule++)
        {
            int steps = ToSteps(gainOfGranule(granule));
            for (int c = 0; c < channels; c++, bit += blockBits)
            {
                int position = bit + 12 + 9; // depois de part2_3_length e big_values
                WriteByte(frame, position, Math.Max(0, ReadByte(frame, position) - steps));
            }
        }

        if (hasCrc)
        {
            ushort crc = Crc16(frame.AsSpan(2, 2), 0xFFFF);
            crc = Crc16(frame.AsSpan(side, sideLength), crc);
            frame[4] = (byte)(crc >> 8);
            frame[5] = (byte)crc;
        }
    }

    /// <summary>CRC-16 usado no MP3 (polinômio 0x8005).</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data, ushort crc)
    {
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x8005 : crc << 1);
        }
        return crc;
    }

    private static int ToSteps(double gain) =>
        gain <= 1e-6 ? 255 : Math.Clamp((int)Math.Round(-20 * Math.Log10(gain) / StepDb), 0, 255);

    private static int ReadByte(byte[] data, int bit)
    {
        int i = bit >> 3, shift = 8 - (bit & 7);
        int word = data[i] << 8 | data[i + 1];
        return (word >> shift) & 0xFF;
    }

    private static void WriteByte(byte[] data, int bit, int value)
    {
        int i = bit >> 3, shift = 8 - (bit & 7);
        int word = data[i] << 8 | data[i + 1];
        word = (word & ~(0xFF << shift)) | (value << shift);
        data[i] = (byte)(word >> 8);
        data[i + 1] = (byte)word;
    }
}
