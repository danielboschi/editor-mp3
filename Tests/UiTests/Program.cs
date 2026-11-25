// Testes da tela: abre a janela de verdade, simula cliques e arrastes no gráfico e
// confere os botões. O áudio tocado é quase inaudível.
// Uso: dotnet run --project Tests\UiTests [-- pasta-para-imagens]
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mp3Editor;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

public static class Program
{
    private static int _failures;

    [STAThread]
    public static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "mp3editor-ui-tests");
        Directory.CreateDirectory(dir);
        string wav = Path.Combine(dir, "ui.wav");
        if (!File.Exists(wav)) MakeAudio(wav);

        var app = new App();
        app.InitializeComponent();
        app.StartupUri = new Uri("pack://application:,,,/EditorMP3;component/MainWindow.xaml");
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            try
            {
                await Run((MainWindow)app.MainWindow, wav, dir);
            }
            catch (Exception ex)
            {
                Check("execução sem erros", false, ex.ToString());
            }
            app.Shutdown();
        });
        app.Run();
        Console.WriteLine(_failures == 0 ? "\nTodos os testes passaram." : $"\n{_failures} teste(s) falharam.");
        return _failures == 0 ? 0 : 1;
    }

    private static async Task Run(MainWindow w, string wav, string dir)
    {
        var wf = w.Waveform;
        Check("filtro tem 'Todos os arquivos' e o padrão continua sendo áudio",
            MainWindow.OpenFilter.EndsWith("|Todos os arquivos (*.*)|*.*") && MainWindow.OpenFilter.StartsWith("Arquivos de áudio"));
        Check("sem arquivo, Reproduzir fica desabilitado", !w.PlayPauseButton.IsEnabled);

        await w.LoadFileAsync(wav);
        await Task.Delay(200);
        double d = wf.Duration;
        Check("arquivo carregado", Near(d, 20, 0.01), $"{d:F3}s");

        double X(double t) => t / d * wf.ActualWidth;
        const double y = 120; // longe das alças
        void Drag(double from, double to, double atY = y)
        {
            wf.PointerDown(new Point(from, atY));
            for (int i = 1; i <= 5; i++) wf.PointerMove(new Point(from + (to - from) * i / 5, atY));
            wf.PointerUp();
        }

        // ----- linhas de corte x cursor -----
        Drag(X(8), X(8));
        Check("clicar no meio move só o cursor", Near(wf.Playhead, 8) && wf.Start == 0 && Near(wf.End, d), State(wf));
        Drag(X(8), X(12));
        Check("arrastar no meio move só o cursor", Near(wf.Playhead, 12) && wf.Start == 0 && Near(wf.End, d), State(wf));
        Drag(0, X(4));
        Check("arrastar a linha do início", Near(wf.Start, 4) && Near(wf.Playhead, 12), State(wf));
        Drag(X(d), X(16));
        Check("arrastar a linha do fim", Near(wf.End, 16) && Near(wf.Playhead, 12), State(wf));
        Drag(X(4) + 10, X(5) + 10, 5);
        Check("arrastar pela alça do topo, sem pulo", Near(wf.Start, 5, 0.03), State(wf));
        Drag(X(5) + 10, X(7));
        Check("ao lado da linha (fora da alça) move o cursor", Near(wf.Start, 5, 0.03) && Near(wf.Playhead, 7), State(wf));
        Drag(X(2), X(2));
        Check("clicar na área escurecida move só o cursor", Near(wf.Playhead, 2) && Near(wf.Start, 5, 0.03), State(wf));
        Drag(X(5), X(19));
        Check("a linha do início não passa da linha do fim", wf.Start < wf.End && Near(wf.End, 16), State(wf));
        wf.SetSelection(5, 16);
        await Task.Delay(100);
        Check("textos acompanham as linhas", w.StartBox.Text == "00:05.000" && w.EndBox.Text == "00:16.000", w.StartBox.Text + " " + w.EndBox.Text);

        // ----- Reproduzir / Pausar -----
        Check("rótulo inicial", Label(w).Contains("Reproduzir"), Label(w));
        Drag(X(2), X(2));
        Click(w.PlayPauseButton);
        Check("Reproduzir vira Pausar", Label(w).Contains("Pausar"), Label(w));
        await Task.Delay(900);
        Check("toca a partir do cursor", wf.Playhead is > 2.3 and < 3.2, State(wf));
        Drag(X(10), X(13));
        await Task.Delay(700);
        Check("mover o cursor tocando muda a posição", wf.Playhead is > 13.1 and < 14 && Label(w).Contains("Pausar"), State(wf));
        Click(w.PlayPauseButton);
        Check("Pausar vira Reproduzir", Label(w).Contains("Reproduzir"), Label(w));
        await Task.Delay(150);
        double paused = wf.Playhead;
        await Task.Delay(400);
        Check("pausado, o cursor fica parado", wf.Playhead == paused, State(wf));
        Click(w.PlayPauseButton);
        await Task.Delay(500);
        Check("continua de onde parou", wf.Playhead > paused + 0.1 && wf.Playhead < paused + 1, State(wf));

        // Arrastando enquanto toca, o player não puxa o cursor de volta.
        wf.PointerDown(new Point(X(1), y));
        await Task.Delay(300);
        Check("durante o arraste o cursor obedece ao mouse", Near(wf.Playhead, 1), State(wf));
        Save(w, Path.Combine(dir, "tela-tocando.png"));
        wf.PointerUp();
        Click(w.PlayPauseButton);
        Drag(X(9), X(9));
        await Task.Delay(100);
        Check("pausado, clicar muda a posição", Near(wf.Playhead, 9) && Label(w).Contains("Reproduzir"), State(wf));
        Click(w.PlayPauseButton);
        await Task.Delay(500);
        Check("e continua do novo ponto", wf.Playhead is > 9.05 and < 10, State(wf));
        Click(w.StopButton);
        Check("Parar volta para a linha do início", Near(wf.Playhead, 5) && !w.StopButton.IsEnabled && Label(w).Contains("Reproduzir"), State(wf));

        // ----- Ouvir resultado -----
        Click(w.PlayResultButton);
        Check("Ouvir resultado também mostra Pausar", Label(w).Contains("Pausar"), Label(w));
        Drag(X(14.8), X(14.8));
        await Task.Delay(1900);
        Check("o resultado termina na linha do fim", Label(w).Contains("Reproduzir") && Near(wf.Playhead, 5), State(wf));
        Click(w.PlayResultButton);
        await Task.Delay(300);
        Drag(X(16), X(6)); // encurta o fim enquanto toca
        await Task.Delay(1500);
        Check("encurtar o fim enquanto ouve o resultado vale na hora", Label(w).Contains("Reproduzir"), State(wf));
        wf.SetSelection(5, 16);

        // ----- Marcar aqui -----
        Drag(X(7), X(7));
        Click(w.StartHereButton);
        Check("Marcar início aqui usa o cursor parado", Near(wf.Start, 7), State(wf));
        Drag(X(12), X(12));
        Click(w.EndHereButton);
        Check("Marcar fim aqui usa o cursor parado", Near(wf.End, 12), State(wf));
        Drag(X(18), X(18));
        Click(w.StartHereButton);
        Check("Marcar início depois do fim é recusado", Near(wf.Start, 7) && Near(wf.End, 12), w.StatusText.Text);

        // ----- Fade (opcional) -----
        wf.SetSelection(5, 16);
        Check("fade começa desligado", wf.Fade.IsNone && w.FadeInCheck.IsChecked == false && w.FadeOutCheck.IsChecked == false && !w.FadeInBox.IsEnabled);
        Click(w.PlayResultButton);
        Check("sem a opção, o resultado toca sem fade", PlayingFade(w) == null);
        w.FadeInCheck.IsChecked = true;
        Check("ligar o fade in vale na hora, com 2 s", PlayingFade(w) is { Fade: { In: 2, Out: 0 }, Start: 5, End: 16 } && w.FadeInBox.IsEnabled && !w.FadeOutBox.IsEnabled);
        w.FadeOutCheck.IsChecked = true;
        SetBox(w.FadeOutBox, "3,5");
        Check("mudar a duração do fade out", PlayingFade(w) is { Fade: { In: 2, Out: 3.5 } } && w.ResultText.Text.Contains("fade out 3,5 s"), w.ResultText.Text);
        Drag(X(16), X(14));
        Check("o fade acompanha a linha do fim", PlayingFade(w) is { End: > 13.9 and < 14.1 });
        SetBox(w.FadeOutBox, "abc");
        Check("duração inválida é recusada", w.FadeOutBox.Text == "3,5" && wf.Fade.Out == 3.5, w.StatusText.Text);
        Click(w.StopButton);
        Drag(X(6), X(6));
        Click(w.PlayPauseButton);
        Check("Reproduzir toca o áudio original, sem fade", PlayingFade(w) == null);
        Click(w.StopButton);
        Click(w.PlayTailButton);
        Check("Últimos 3 s toca com fade", PlayingFade(w) != null);
        Click(w.StopButton);
        wf.SetSelection(5, 8);
        await Task.Delay(100);
        Check("fade maior que o trecho é encurtado e avisado", w.ResultText.Text.Contains("encurtado"), w.ResultText.Text);
        w.FadeInCheck.IsChecked = false;
        w.FadeOutCheck.IsChecked = false;
        Check("desligar remove o fade", wf.Fade.IsNone && !w.ResultText.Text.Contains("fade"), w.ResultText.Text);
        wf.SetSelection(7, 12);

        // ----- Fim do arquivo -----
        Drag(X(d) - 1, X(d) - 1);
        Click(w.PlayPauseButton);
        await Task.Delay(300);
        Check("Reproduzir com o cursor no fim não trava", true, State(wf));
        Click(w.StopButton);
        for (int i = 0; i < 40; i++)
        {
            Click(w.PlayPauseButton);
            Drag(X(i * 0.4), X(i * 0.4 + 0.3));
            if (i % 3 == 0) Click(w.PlayPauseButton);
            await Task.Delay(15);
        }
        Click(w.StopButton);
        Check("muitos cliques rápidos não quebram", true);

        // Segundo parâmetro opcional: outro áudio para as imagens.
        if (Environment.GetCommandLineArgs() is { Length: > 2 } extra)
        {
            await w.LoadFileAsync(extra[2]);
            d = wf.Duration;
            wf.SetSelection(d * 0.2, d * 0.8);
        }
        Drag(X(d * 0.45), X(d * 0.45));
        w.FadeInCheck.IsChecked = true;
        w.FadeOutCheck.IsChecked = true;
        SetBox(w.FadeInBox, "15");
        SetBox(w.FadeOutBox, "25");
        await Task.Delay(200);
        Save(w,Path.Combine(dir, "tela-parada.png"));
        w.Width = w.MinWidth;
        w.Height = w.MinHeight;
        await Task.Delay(200);
        Save(w, Path.Combine(dir, "tela-minima.png"));
    }

    /// <summary>Fade aplicado ao que está tocando agora (estado interno do player).</summary>
    private static Mp3Editor.Audio.FadeRegion? PlayingFade(MainWindow w)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var player = typeof(MainWindow).GetField("_player", flags)!.GetValue(w)!;
        var range = player.GetType().GetField("_range", flags)!.GetValue(player);
        return (Mp3Editor.Audio.FadeRegion?)range?.GetType().GetField("_fade", flags)!.GetValue(range);
    }

    private static void SetBox(TextBox box, string text)
    {
        box.Text = text;
        box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
    }

    private static string Label(MainWindow w) => (string)w.PlayPauseButton.Content;

    private static string State(Mp3Editor.Controls.WaveformControl wf) =>
        $"início={wf.Start:F3} fim={wf.End:F3} cursor={wf.Playhead:F3}";

    private static bool Near(double a, double b, double tolerance = 0.02) => Math.Abs(a - b) <= tolerance;

    private static void Click(Button b)
    {
        if (b.IsEnabled) b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
        if (!ok) _failures++;
    }

    private static void Save(Window w, string path)
    {
        var root = (FrameworkElement)w.Content;
        var bmp = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(w.Background, null, new Rect(0, 0, w.ActualWidth, w.ActualHeight));
        bmp.Render(bg);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }

    private static void MakeAudio(string path)
    {
        var tone = new SignalGenerator(44100, 2)
        {
            Type = SignalGeneratorType.Sweep, Frequency = 150, FrequencyEnd = 400, SweepLengthSecs = 3, Gain = 0.004,
        };
        WaveFileWriter.CreateWaveFile16(path, tone.Take(TimeSpan.FromSeconds(20)));
    }
}
