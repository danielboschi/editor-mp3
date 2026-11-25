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

// ---------- Outros formatos de entrada ----------

void CheckOther(string name, string source, AudioKind kind)
{
    Check($"{name}: reconhecido como {kind}", AudioSource.Detect(source) == kind, "");
    using (var s = AudioSource.Open(source))
    {
        double d = WaveformData.Load(s).Duration;
        Check($"{name}: abrir", Math.Abs(d - 10) < 0.15, $"duração={d:F3}s");
    }
    string toMp3 = Path.Combine(dir, name.Replace(' ', '-') + "-saida.mp3");
    Console.WriteLine("   -> " + AudioTrimmer.Save(source, toMp3, start, end));
    CheckDuration($"{name} → MP3", toMp3, expected, 0.1);
    string toWav = Path.ChangeExtension(toMp3, ".wav");
    Console.WriteLine("   -> " + AudioTrimmer.Save(source, toWav, start, end));
    CheckDuration($"{name} → WAV", toWav, expected, 0.01);
}

string m4a = Path.Combine(dir, "tom.m4a"), wma = Path.Combine(dir, "tom.wma");
using (var r = new WaveFileReader(wav)) MediaFoundationEncoder.EncodeToAac(r, m4a, 192000);
using (var r = new WaveFileReader(wav)) MediaFoundationEncoder.EncodeToWma(r, wma, 192000);
CheckOther("M4A", m4a, AudioKind.Other);
CheckOther("WMA", wma, AudioKind.Other);

// Extensão errada: o conteúdo é que vale.
string fakeMp3 = Path.Combine(dir, "na-verdade-m4a.mp3"), fakeWav = Path.Combine(dir, "na-verdade-mp3.wav"),
       noExt = Path.Combine(dir, "sem-extensao");
File.Copy(m4a, fakeMp3, true);
File.Copy(mp3, fakeWav, true);
File.Copy(m4a, noExt, true);
CheckOther("M4A chamado .mp3", fakeMp3, AudioKind.Other);
CheckOther("MP3 chamado .wav", fakeWav, AudioKind.Mp3);
CheckOther("M4A sem extensao", noExt, AudioKind.Other);

// Taxa baixa, mono (gravador de voz) e 6 canais.
string voice = Path.Combine(dir, "voz.wav"), surround = Path.Combine(dir, "surround.wav");
WaveFileWriter.CreateWaveFile16(voice, new SignalGenerator(8000, 1) { Frequency = 440, Gain = 0.3 }.Take(TimeSpan.FromSeconds(10)));
WaveFileWriter.CreateWaveFile16(surround, new SignalGenerator(48000, 6) { Frequency = 440, Gain = 0.3 }.Take(TimeSpan.FromSeconds(10)));
CheckOther("WAV 8 kHz mono", voice, AudioKind.Wav);
CheckOther("WAV 6 canais", surround, AudioKind.Wav);

// Arquivo que não é áudio: mensagem clara em vez de travar.
string text = Path.Combine(dir, "texto.txt");
File.WriteAllText(text, "isto não é um áudio, é só um texto qualquer");
string error = "";
try { AudioSource.Open(text).Dispose(); } catch (NotSupportedException ex) { error = ex.Message; }
Check("arquivo que não é áudio é recusado", error.Length > 0, error);
string extError = "";
try { AudioTrimmer.Save(mp3, Path.Combine(dir, "x.m4a"), 1, 2); } catch (ArgumentException ex) { extError = ex.Message; }
Check("salvar em extensão não suportada é recusado", extError.Length > 0, extError);

// ---------- Posição exata (mover o cursor) ----------

float[] ReadAll(AudioSource s, int max = int.MaxValue)
{
    var all = new List<float>();
    var buf = new float[8192];
    int n;
    while (all.Count < max && (n = s.Read(buf, 0, buf.Length)) > 0) all.AddRange(buf.AsSpan(0, n).ToArray());
    return all.ToArray();
}

void CheckSeek(string name, string path)
{
    using var s = AudioSource.Open(path);
    var all = ReadAll(s);
    int ch = s.WaveFormat.Channels, rate = s.WaveFormat.SampleRate;
    double worst = 0;
    foreach (double t in new[] { 15.3, 2.7, 18.01, 9.99, 0.0 })
    {
        s.Seek(t);
        var part = ReadAll(s, rate * ch / 4);
        int at = (int)Math.Round(t * rate) * ch;
        for (int i = 0; i < Math.Min(part.Length, rate * ch / 4); i++) worst = Math.Max(worst, Math.Abs(part[i] - all[at + i]));
    }
    Check($"mover cursor: {name}", worst < 0.02, $"maior diferença={worst:F4}");
}

// Varredura de frequência: cada instante soa diferente, então qualquer desvio aparece.
string sweepWav = Path.Combine(dir, "varredura.wav"), lo = Path.Combine(dir, "v96.mp3"), hi = Path.Combine(dir, "v320.mp3"),
       vbr = Path.Combine(dir, "varredura-vbr.mp3");
WaveFileWriter.CreateWaveFile16(sweepWav, new SignalGenerator(44100, 2)
    { Type = SignalGeneratorType.Sweep, Frequency = 200, FrequencyEnd = 3000, SweepLengthSecs = 20, Gain = 0.4 }.Take(TimeSpan.FromSeconds(20)));
using (var r = new WaveFileReader(sweepWav)) MediaFoundationEncoder.EncodeToMp3(r, lo, 96000);
using (var r = new WaveFileReader(sweepWav)) MediaFoundationEncoder.EncodeToMp3(r, hi, 320000);
using (var o = File.Create(vbr))
{
    // Metade a 96 kbps e metade a 320 kbps: um MP3 de taxa variável.
    int half = 10 * 44100 / 1152, i = 0;
    using var a = new Mp3FileReader(lo);
    using var b = new Mp3FileReader(hi);
    Mp3Frame? f;
    while (i++ < half && (f = a.ReadNextFrame()) != null) { b.ReadNextFrame(); o.Write(f.RawData); }
    while ((f = b.ReadNextFrame()) != null) o.Write(f.RawData);
}
CheckSeek("WAV", sweepWav);
CheckSeek("MP3 320 kbps", hi);
CheckSeek("MP3 VBR", vbr);

// Leitura de tempos digitados.
Check("TimeFormat 01:23,5", TimeFormat.TryParse("01:23,5", out double t1) && Math.Abs(t1 - 83.5) < 1e-9, t1.ToString());
Check("TimeFormat 1:00:00.250", TimeFormat.TryParse("1:00:00.250", out double t2) && Math.Abs(t2 - 3600.25) < 1e-9, t2.ToString());
Check("TimeFormat inválido", !TimeFormat.TryParse("abc", out _), "");
Check("TimeFormat formato", TimeFormat.Format(83.5) == "01:23.500", TimeFormat.Format(83.5));

Console.WriteLine(failures == 0 ? "\nTodos os testes passaram." : $"\n{failures} teste(s) falharam.");
return failures == 0 ? 0 : 1;
