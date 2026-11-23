using System.Globalization;

namespace Mp3Editor;

/// <summary>Conversão entre segundos e texto no formato mm:ss.fff.</summary>
public static class TimeFormat
{
    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : t.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    /// <summary>Rótulo curto para a régua de tempo.</summary>
    public static string FormatShort(double seconds, double step)
    {
        long ms = (long)Math.Round(Math.Max(0, seconds) * 1000);
        long m = ms / 60000;
        long rem = ms % 60000;
        return step < 1
            ? $"{m}:{(rem / 1000.0).ToString("00.00", CultureInfo.InvariantCulture)}"
            : $"{m}:{(rem / 1000).ToString("00", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Aceita "ss", "ss.fff", "mm:ss.fff" ou "h:mm:ss.fff" (vírgula ou ponto).</summary>
    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length > 3) return false;

        if (!double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double sec) || sec < 0)
            return false;

        double total = sec;
        double multiplier = 60;
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int v)) return false;
            total += v * multiplier;
            multiplier *= 60;
        }

        seconds = total;
        return true;
    }
}
