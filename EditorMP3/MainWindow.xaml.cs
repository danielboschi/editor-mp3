using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Mp3Editor.Audio;

namespace Mp3Editor;

public partial class MainWindow : Window
{
    private const double PreviewSeconds = 3;

    private readonly PlaybackService _player = new();
    private readonly DispatcherTimer _timer;
    private string? _path;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => UpdatePlaybackUi();
        _player.PlaybackEnded += (_, _) => OnPlaybackFinished();
        Waveform.SelectionChanged += (_, _) => UpdateSelectionUi();
        Closed += (_, _) => _player.Dispose();

        UpdateEnabledState();

        // Permite abrir arquivo pela linha de comando / "Abrir com".
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
            Loaded += async (_, _) => await LoadFileAsync(args[1]);
    }

    // ---------- Arquivo ----------

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Abrir arquivo de áudio",
            Filter = "Arquivos de áudio (*.mp3;*.wav)|*.mp3;*.wav|MP3 (*.mp3)|*.mp3|WAV (*.wav)|*.wav",
        };
        if (dialog.ShowDialog(this) == true)
            await LoadFileAsync(dialog.FileName);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await LoadFileAsync(files[0]);
    }

    private async Task LoadFileAsync(string path)
    {
        if (_busy) return;
        StopPlayback();
        SetBusy(true, "Carregando " + Path.GetFileName(path) + "…");
        try
        {
            var data = await Task.Run(() => WaveformData.Load(path, 6000));
            _player.Load(path);
            _path = path;

            FileNameText.Text = Path.GetFileName(path);
            Title = Path.GetFileName(path) + " — Editor de MP3";
            HintText.Visibility = Visibility.Collapsed;
            Waveform.SetData(data);

            string channels = data.Channels == 1 ? "mono" : data.Channels == 2 ? "estéreo" : $"{data.Channels} canais";
            StatusText.Text = $"{Path.GetExtension(path).TrimStart('.').ToUpperInvariant()} · {data.SampleRate} Hz · {channels} · " +
                              $"duração {TimeFormat.Format(data.Duration)}.  Arraste os marcadores verde (início) e vermelho (fim).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            MessageBox.Show(this, "Não foi possível abrir o arquivo:\n\n" + ex.Message, "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_path == null) return;
        StopPlayback();

        bool isWav = string.Equals(Path.GetExtension(_path), ".wav", StringComparison.OrdinalIgnoreCase);
        var dialog = new SaveFileDialog
        {
            Title = "Salvar áudio cortado",
            Filter = "MP3 (*.mp3)|*.mp3|WAV (*.wav)|*.wav",
            FilterIndex = isWav ? 2 : 1,
            FileName = Path.GetFileNameWithoutExtension(_path) + " (cortado)",
            InitialDirectory = Path.GetDirectoryName(_path),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        string output = dialog.FileName;
        if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(_path), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Escolha um nome diferente do arquivo original — o original não é alterado.",
                "Salvar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string input = _path;
        double start = Waveform.Start, end = Waveform.End;
        SetBusy(true, "Salvando…");
        try
        {
            string how = await Task.Run(() => AudioTrimmer.Save(input, output, start, end));
            StatusText.Text = $"Salvo em {output} ({how}).";
            MessageBox.Show(this, $"Arquivo salvo com sucesso!\n\n{output}\n\n({how})", "Salvo",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha ao salvar.";
            MessageBox.Show(this, "Não foi possível salvar o arquivo:\n\n" + ex.Message, "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------- Corte ----------

    private void UpdateSelectionUi()
    {
        if (!StartBox.IsKeyboardFocused) StartBox.Text = TimeFormat.Format(Waveform.Start);
        if (!EndBox.IsKeyboardFocused) EndBox.Text = TimeFormat.Format(Waveform.End);

        if (_path == null)
        {
            ResultText.Text = "";
            return;
        }

        double removedStart = Waveform.Start;
        double removedEnd = Waveform.Duration - Waveform.End;
        ResultText.Text = $"Duração final: {TimeFormat.Format(Waveform.End - Waveform.Start)}   " +
                          $"(remove {TimeFormat.Format(removedStart)} do começo e {TimeFormat.Format(removedEnd)} do final)";
    }

    private void TimeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyTimeBox((TextBox)sender);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Keyboard.ClearFocus();
            UpdateSelectionUi();
        }
    }

    private void TimeBox_LostFocus(object sender, RoutedEventArgs e) => ApplyTimeBox((TextBox)sender);

    private void ApplyTimeBox(TextBox box)
    {
        if (_path == null) return;
        if (TimeFormat.TryParse(box.Text, out double t))
        {
            if (box == StartBox)
                Waveform.SetSelection(Math.Min(t, Waveform.End - 0.05), Waveform.End);
            else
                Waveform.SetSelection(Waveform.Start, Math.Max(t, Waveform.Start + 0.05));
        }
        else
        {
            StatusText.Text = "Tempo inválido. Use o formato mm:ss.mmm, por exemplo 01:23.500.";
        }
        box.Text = TimeFormat.Format(box == StartBox ? Waveform.Start : Waveform.End);
    }

    private void StartHere_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPlaybackPosition(out double pos)) return;
        Waveform.SetSelection(Math.Min(pos, Waveform.End - 0.05), Waveform.End);
    }

    private void EndHere_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPlaybackPosition(out double pos)) return;
        Waveform.SetSelection(Waveform.Start, Math.Max(pos, Waveform.Start + 0.05));
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Waveform.SetSelection(0, Waveform.Duration);

    private bool TryGetPlaybackPosition(out double position)
    {
        position = _player.Position;
        if (_player.IsActive) return true;
        StatusText.Text = "Toque o áudio (por exemplo, \"Ouvir original\") e clique no botão quando chegar ao ponto desejado.";
        return false;
    }

    // ---------- Reprodução ----------

    private void PlayResult_Click(object sender, RoutedEventArgs e) => Play(Waveform.Start, Waveform.End);

    private void PlayHead_Click(object sender, RoutedEventArgs e) =>
        Play(Waveform.Start, Math.Min(Waveform.Start + PreviewSeconds, Waveform.End));

    private void PlayTail_Click(object sender, RoutedEventArgs e) =>
        Play(Math.Max(Waveform.End - PreviewSeconds, Waveform.Start), Waveform.End);

    private void PlayOriginal_Click(object sender, RoutedEventArgs e) => Play(0, Waveform.Duration);

    private void Pause_Click(object sender, RoutedEventArgs e) => TogglePause();

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();

    private void Play(double from, double to)
    {
        if (_path == null) return;
        try
        {
            _player.Play(from, to);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível tocar o áudio:\n\n" + ex.Message, "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        PauseButton.Content = "⏸  Pausar";
        _timer.Start();
        UpdatePlaybackUi();
        UpdateEnabledState();
    }

    private void TogglePause()
    {
        if (!_player.IsActive) return;
        if (_player.IsPaused)
        {
            _player.Resume();
            PauseButton.Content = "⏸  Pausar";
        }
        else
        {
            _player.Pause();
            PauseButton.Content = "▶  Continuar";
        }
    }

    private void StopPlayback()
    {
        _player.Stop();
        OnPlaybackFinished();
    }

    private void OnPlaybackFinished()
    {
        _timer.Stop();
        Waveform.Playhead = null;
        PositionText.Text = "";
        PauseButton.Content = "⏸  Pausar";
        UpdateEnabledState();
    }

    private void UpdatePlaybackUi()
    {
        if (!_player.IsActive) return;
        double pos = _player.Position;
        Waveform.Playhead = pos;
        PositionText.Text = $"{TimeFormat.Format(pos)} / {TimeFormat.Format(Waveform.Duration)}";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || Keyboard.FocusedElement is TextBox || _path == null || _busy) return;
        if (_player.IsActive) TogglePause();
        else Play(Waveform.Start, Waveform.End);
        e.Handled = true;
    }

    // ---------- Estado dos botões ----------

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        if (status != null) StatusText.Text = status;
        Cursor = busy ? Cursors.Wait : null;
        UpdateEnabledState();
    }

    private void UpdateEnabledState()
    {
        bool loaded = _path != null && !_busy;
        bool playing = _player.IsActive;

        OpenButton.IsEnabled = !_busy;
        Waveform.IsEnabled = loaded;
        foreach (var control in new Control[]
                 {
                     SaveButton, StartBox, EndBox, ResetButton,
                     PlayResultButton, PlayHeadButton, PlayTailButton, PlayOriginalButton,
                 })
            control.IsEnabled = loaded;

        StartHereButton.IsEnabled = loaded && playing;
        EndHereButton.IsEnabled = loaded && playing;
        PauseButton.IsEnabled = loaded && playing;
        StopButton.IsEnabled = loaded && playing;
    }
}
