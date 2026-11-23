// Testes automáticos do corte: gera um tom de 10 s, corta de 2,0 s a 7,5 s
// em todas as combinações de formato e confere a duração do resultado.
using Mp3Editor;
using Mp3Editor.Audio;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

string dir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "mp3editor-tests");
Directory.CreateDirectory(dir);
int failures = 0;

void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    if (!ok) failures++;
}

double DurationOf(string path)
{
    using var r = new AudioFileReader(path);
    return r.TotalTime.TotalSeconds;
}

void CheckDuration(string name, string path, double expected, double tolerance)
{
    double d = DurationOf(path);
    Check(name, Math.Abs(d - expected) <= tolerance, $"duração={d:F3}s esperado={expected:F3}s ±{tolerance}");
}

// Fonte: tom de 440 Hz, 10 s, estéreo.
string wav = Path.Combine(dir, "tom.wav");
string mp3 = Path.Combine(dir, "tom.mp3");
var tone = new SignalGenerator(44100, 2) { Type = SignalGeneratorType.Sin, Frequency = 440, Gain = 0.3 }
    .Take(TimeSpan.FromSeconds(10));
WaveFileWriter.CreateWaveFile16(wav, tone);
MediaFoundationApi.Startup();
using (var r = new WaveFileReader(wav)) MediaFoundationEncoder.EncodeToMp3(r, mp3, 192000);
CheckDuration("fonte MP3", mp3, 10, 0.1);

const double start = 2.0, end = 7.5, expected = end - start;

string mp3ToMp3 = Path.Combine(dir, "mp3-para-mp3.mp3");
Console.WriteLine("   -> " + AudioTrimmer.Save(mp3, mp3ToMp3, start, end));
CheckDuration("MP3 → MP3 (sem perda)", mp3ToMp3, expected, 0.03);

// Os quadros do resultado devem ser cópias idênticas dos originais.
{
    var original = new List<byte[]>();
    using (var r = new Mp3FileReader(mp3)) { Mp3Frame? f; while ((f = r.ReadNextFrame()) != null) original.Add(f.RawData); }
    var trimmed = new List<byte[]>();
    using (var r = new Mp3FileReader(mp3ToMp3)) { Mp3Frame? f; while ((f = r.ReadNextFrame()) != null) trimmed.Add(f.RawData); }
    int first = original.FindIndex(o => o.AsSpan().SequenceEqual(trimmed[0]));
    bool identical = first >= 0 && first + trimmed.Count <= original.Count &&
                     trimmed.Select((t, i) => t.AsSpan().SequenceEqual(original[first + i])).All(x => x);
    Check("MP3 → MP3 quadros idênticos", identical, $"{trimmed.Count} quadros copiados a partir do quadro {first}");
}

string wavToWav = Path.Combine(dir, "wav-para-wav.wav");
Console.WriteLine("   -> " + AudioTrimmer.Save(wav, wavToWav, start, end));
CheckDuration("WAV → WAV (exato)", wavToWav, expected, 0.001);

string mp3ToWav = Path.Combine(dir, "mp3-para-wav.wav");
Console.WriteLine("   -> " + AudioTrimmer.Save(mp3, mp3ToWav, start, end));
CheckDuration("MP3 → WAV", mp3ToWav, expected, 0.01);

string wavToMp3 = Path.Combine(dir, "wav-para-mp3.mp3");
Console.WriteLine("   -> " + AudioTrimmer.Save(wav, wavToMp3, start, end));
CheckDuration("WAV → MP3", wavToMp3, expected, 0.1);

// Sobrescrever um arquivo existente deve funcionar.
AudioTrimmer.Save(mp3, mp3ToMp3, 1, 3);
CheckDuration("sobrescrever destino", mp3ToMp3, 2, 0.03);

// Leitura de tempos digitados.
Check("TimeFormat 01:23,5", TimeFormat.TryParse("01:23,5", out double t1) && Math.Abs(t1 - 83.5) < 1e-9, t1.ToString());
Check("TimeFormat 1:00:00.250", TimeFormat.TryParse("1:00:00.250", out double t2) && Math.Abs(t2 - 3600.25) < 1e-9, t2.ToString());
Check("TimeFormat inválido", !TimeFormat.TryParse("abc", out _), "");
Check("TimeFormat formato", TimeFormat.Format(83.5) == "01:23.500", TimeFormat.Format(83.5));

Console.WriteLine(failures == 0 ? "\nTodos os testes passaram." : $"\n{failures} teste(s) falharam.");
return failures == 0 ? 0 : 1;
