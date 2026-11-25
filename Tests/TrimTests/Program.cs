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

// ---------- Fade in / fade out ----------

List<byte[]> Frames(string path)
{
    var list = new List<byte[]>();
    using var r = new Mp3FileReader(path);
    Mp3Frame? f;
    while ((f = r.ReadNextFrame()) != null) list.Add(f.RawData);
    return list;
}

// Volume médio (RMS) a cada 50 ms. Se "windows", decodifica com o Media Foundation.
double[] Envelope(string path, bool windows = false)
{
    float[] all;
    int step;
    if (windows)
    {
        using var m = new MediaFoundationReader(path);
        var sp = m.ToSampleProvider();
        var list = new List<float>();
        var buf = new float[8192];
        int n;
        while ((n = sp.Read(buf, 0, buf.Length)) > 0) list.AddRange(buf.AsSpan(0, n).ToArray());
        all = list.ToArray();
        step = sp.WaveFormat.SampleRate / 20 * sp.WaveFormat.Channels;
    }
    else
    {
        using var s = AudioSource.Open(path);
        all = ReadAll(s);
        step = s.WaveFormat.SampleRate / 20 * s.WaveFormat.Channels;
    }
    return Enumerable.Range(0, all.Length / step)
        .Select(i => Math.Sqrt(all.AsSpan(i * step, step).ToArray().Average(v => (double)v * v))).ToArray();
}

// Compara o volume do arquivo com fade ao mesmo corte sem fade.
void CheckEnvelope(string name, string plain, string faded, Fade fade, double toleranceDb, bool windows = false)
{
    double[] a = Envelope(plain, windows), b = Envelope(faded, windows);
    var region = new FadeRegion(start, end, fade);
    double worst = 0;
    int compared = 0;
    for (int i = 0; i < Math.Min(a.Length, b.Length) - 2; i++)
    {
        double power = 0;
        for (int k = 0; k < 20; k++) { double g = region.GainAt(start + (i + (k + 0.5) / 20) * 0.05); power += g * g / 20; }
        double gain = Math.Sqrt(power);
        if (gain < 0.1 || a[i] < 0.01) continue;
        worst = Math.Max(worst, Math.Abs(20 * Math.Log10(b[i] / a[i] / gain)));
        compared++;
    }
    bool silentEnds = (fade.In <= 0 || b[0] < a[5] * 0.02) && (fade.Out <= 0 || b[Math.Min(109, Math.Min(a.Length, b.Length) - 1)] < a[5] * 0.05);
    Check(name, worst <= toleranceDb && compared > 50 && silentEnds && Math.Abs(a.Length - b.Length) <= 1,
        $"maior desvio={worst:F2} dB em {compared} janelas, pontas em silêncio={silentEnds}");
}

void CheckFade(string name, string source, string ext, double toleranceDb)
{
    string plain = Path.Combine(dir, "fade-sem-" + name.Replace(' ', '-') + ext), faded = Path.Combine(dir, "fade-com-" + name.Replace(' ', '-') + ext);
    var both = new Fade(2, 1.5);
    AudioTrimmer.Save(source, plain, start, end);
    Console.WriteLine("   -> " + AudioTrimmer.Save(source, faded, start, end, both));
    CheckEnvelope($"fade {name} → {ext}", plain, faded, both, toleranceDb);
}

CheckFade("MP3", mp3, ".mp3", 1.6);
CheckFade("MP3", mp3, ".wav", 0.2);
CheckFade("WAV 16 bits", wav, ".wav", 0.1);
CheckFade("WAV", wav, ".mp3", 1.6);
CheckFade("M4A", m4a, ".mp3", 1.6);
CheckFade("M4A", m4a, ".wav", 0.2);
CheckFade("6 canais", surround, ".wav", 0.1);

// WAV em outras resoluções mantém o formato original.
string wav24 = Path.Combine(dir, "tom24.wav"), wav32f = Path.Combine(dir, "tom32f.wav"), wav8 = Path.Combine(dir, "tom8.wav"),
       wavExt = Path.Combine(dir, "tomext.wav"), mono = Path.Combine(dir, "tom-mono.mp3");
ISampleProvider Tone(int ch) => new SignalGenerator(44100, ch) { Frequency = 440, Gain = 0.3 }.Take(TimeSpan.FromSeconds(10));
WaveFileWriter.CreateWaveFile(wav24, new SampleToWaveProvider24(Tone(2)));
WaveFileWriter.CreateWaveFile(wav32f, new SampleToWaveProvider(Tone(2)));
using (var w8 = new WaveFileWriter(wav8, new WaveFormat(44100, 8, 1)))
{
    var buf = new float[4410];
    var t = Tone(1);
    int n;
    while ((n = t.Read(buf, 0, buf.Length)) > 0) for (int i = 0; i < n; i++) w8.WriteByte((byte)Math.Round(128 + buf[i] * 127));
}
using (var r = new WaveFileReader(wav24))
using (var we = new WaveFileWriter(wavExt, new WaveFormatExtensible(44100, 24, 2)))
{
    var all = new byte[r.Length];
    r.ReadExactly(all);
    we.Write(all, 0, all.Length);
}
foreach (var (name, file) in new[] { ("24 bits", wav24), ("32 bits float", wav32f), ("8 bits", wav8), ("extensible", wavExt) })
{
    CheckFade(name, file, ".wav", name == "8 bits" ? 0.6 : 0.1);
    using var a = new WaveFileReader(file);
    using var b = new WaveFileReader(Path.Combine(dir, "fade-com-" + name.Replace(' ', '-') + ".wav"));
    Check($"fade {name} mantém o formato", a.WaveFormat.Encoding == b.WaveFormat.Encoding && a.WaveFormat.BitsPerSample == b.WaveFormat.BitsPerSample, b.WaveFormat.ToString());
}
WaveFileWriter.CreateWaveFile16(Path.Combine(dir, "mono44.wav"), Tone(1));
using (var r = new WaveFileReader(Path.Combine(dir, "mono44.wav"))) MediaFoundationEncoder.EncodeToMp3(r, mono, 128000);
using (var r = new Mp3FileReader(mono)) Check("fonte mono é mesmo mono", r.Mp3WaveFormat.Channels == 1, r.Mp3WaveFormat.ToString());
CheckFade("MP3 mono", mono, ".mp3", 1.6);

// O miolo do MP3 continua idêntico: só os quadros dos fades mudam, e só no volume.
{
    string plain = Path.Combine(dir, "fade-sem-MP3.mp3"), faded = Path.Combine(dir, "fade-com-MP3.mp3");
    var a = Frames(plain);
    var b = Frames(faded);
    var changed = Enumerable.Range(0, Math.Min(a.Count, b.Count)).Where(i => !a[i].AsSpan().SequenceEqual(b[i])).ToList();
    int lastIn = (int)Math.Ceiling(2 * 44100 / 1152.0) + 2, firstOut = a.Count - (int)Math.Ceiling(1.5 * 44100 / 1152.0) - 3;
    Check("fade MP3: miolo idêntico ao original", a.Count == b.Count && changed.Count > 0 && changed.All(i => i <= lastIn || i >= firstOut),
        $"{changed.Count} de {a.Count} quadros alterados");
    Check("fade MP3: áudio comprimido intacto nos quadros alterados", changed.All(i => a[i].AsSpan(36 + 2).SequenceEqual(b[i].AsSpan(36 + 2))), "");
    CheckEnvelope("fade MP3 no decodificador do Windows", plain, faded, new Fade(2, 1.5), 1.8, windows: true);
    Console.WriteLine($"   quadros com CRC: {a.Count(f => (f[1] & 1) == 0)} de {a.Count}");
}

string onlyIn = Path.Combine(dir, "fade-so-in.mp3"), onlyOut = Path.Combine(dir, "fade-so-out.mp3"), none = Path.Combine(dir, "fade-nenhum.mp3");
AudioTrimmer.Save(mp3, onlyIn, start, end, new Fade(2, 0));
AudioTrimmer.Save(mp3, onlyOut, start, end, new Fade(0, 1.5));
AudioTrimmer.Save(mp3, none, start, end, default);
CheckEnvelope("só fade in", none, onlyIn, new Fade(2, 0), 1.6);
CheckEnvelope("só fade out", none, onlyOut, new Fade(0, 1.5), 1.6);
Check("sem fade o arquivo é igual ao corte normal", File.ReadAllBytes(none).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dir, "fade-sem-MP3.mp3"))), "");

var fitted = new Fade(6, 3).FitTo(3);
Check("fades maiores que o trecho são encurtados", Math.Abs(fitted.In - 2) < 1e-9 && Math.Abs(fitted.Out - 1) < 1e-9, fitted.ToString());
Check("CRC-16 (valor conhecido)", Mp3FrameGain.Crc16("123456789"u8, 0xFFFF) == 0xAEE7, "");

// Leitura de tempos digitados.
Check("TimeFormat 01:23,5", TimeFormat.TryParse("01:23,5", out double t1) && Math.Abs(t1 - 83.5) < 1e-9, t1.ToString());
Check("TimeFormat 1:00:00.250", TimeFormat.TryParse("1:00:00.250", out double t2) && Math.Abs(t2 - 3600.25) < 1e-9, t2.ToString());
Check("TimeFormat inválido", !TimeFormat.TryParse("abc", out _), "");
Check("TimeFormat formato", TimeFormat.Format(83.5) == "01:23.500", TimeFormat.Format(83.5));

Console.WriteLine(failures == 0 ? "\nTodos os testes passaram." : $"\n{failures} teste(s) falharam.");
return failures == 0 ? 0 : 1;
