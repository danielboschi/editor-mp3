using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Mp3Editor.Audio;
using Mp3Editor.Controls;

namespace Mp3Editor;

public partial class MainWindow : Window
{
    internal const string OpenFilter =
        "Arquivos de áudio (*.mp3;*.wav)|*.mp3;*.wav|MP3 (*.mp3)|*.mp3|WAV (*.wav)|*.wav|Todos os arquivos (*.*)|*.*";

    private const double PreviewSeconds = 3;
    private const string PlayLabel = "▶  Reproduzir";
    private const string PauseLabel = "⏸  Pausar";

    /// <summary>O que está tocando: o áudio inteiro, o resultado do corte ou um trecho curto.</summary>
    private enum PlayMode { Full, Result, Preview }

    private readonly PlaybackService _player = new();
    private PlayMode _mode = PlayMode.Full;
    private string? _path;
    private bool _busy;
    private bool _following;           // o cursor está acompanhando a reprodução
    private bool _selectionUiPending;
    private TimeSpan _lastPositionText;

    public MainWindow()
    {
        InitializeComponent();

        _player.PlaybackEnded += (_, _) => OnPlaybackFinished();
        Waveform.SelectionChanged += (_, _) => OnSelectionChanged();
        Waveform.PlayheadDragging += (_, t) => ShowPosition(t);
        Waveform.PlayheadMoved += (_, t) => SeekTo(t);
        Closed += (_, _) =>
        {
            FollowPlayback(false);
            _player.Dispose();
        };

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
            Filter = OpenFilter,
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

    internal async Task LoadFileAsync(string path)
    {
        if (_busy) return;
        StopPlayback();
        string previousStatus = StatusText.Text;
        SetBusy(true, "Carregando " + Path.GetFileName(path) + "…");
        try
        {
            var (data, source) = await Task.Run(() =>
            {
                var opened = AudioSource.Open(path);
                try
                {
                    return (WaveformData.Load(opened), opened);
                }
                catch
                {
                    opened.Dispose();
                    throw;
                }
            });
            _player.Load(source);
            _path = path;

            FileNameText.Text = Path.GetFileName(path);
            Title = Path.GetFileName(path) + " — Editor de MP3";
            HintText.Visibility = Visibility.Collapsed;
            Waveform.SetData(data);
            UpdateSelectionUi();
            ShowPosition(0);

            string channels = data.Channels == 1 ? "mono" : "estéreo";
            StatusText.Text = $"{Path.GetExtension(path).TrimStart('.').ToUpperInvariant()} · {data.SampleRate} Hz · {channels} · " +
                              $"duração {TimeFormat.Format(data.Duration)}.  Arraste a linha verde (início) e a vermelha (fim). " +
                              "Clique ou arraste no gráfico para mover o cursor.";
        }
        catch (Exception ex)
        {
            StatusText.Text = previousStatus;
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

    private void OnSelectionChanged()
    {
        // Ao ouvir o resultado, mover a linha do fim já vale para o que está tocando.
        if (_mode == PlayMode.Result) _player.SetEnd(Waveform.End);

        // Atualiza os textos no máximo uma vez por quadro durante o arraste.
        if (_selectionUiPending) return;
        _selectionUiPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _selectionUiPending = false;
            UpdateSelectionUi();
        });
    }

    private void UpdateSelectionUi()
    {
        if (!StartBox.IsKeyboardFocused) SetText(StartBox, TimeFormat.Format(Waveform.Start));
        if (!EndBox.IsKeyboardFocused) SetText(EndBox, TimeFormat.Format(Waveform.End));

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

    private static void SetText(TextBox box, string text)
    {
        if (box.Text != text) box.Text = text;
    }

    private void TimeBox_KeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        if (e.Key == Key.Enter)
        {
            ApplyTimeBox(box);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            box.Text = CurrentText(box); // descarta o que foi digitado
            Waveform.Focus();
            e.Handled = true;
        }
    }

    private void TimeBox_LostFocus(object sender, RoutedEventArgs e) => ApplyTimeBox((TextBox)sender);

    private string CurrentText(TextBox box) =>
        TimeFormat.Format(box == StartBox ? Waveform.Start : Waveform.End);

    private void ApplyTimeBox(TextBox box)
    {
        if (_path == null || box.Text == CurrentText(box)) return;
        if (TimeFormat.TryParse(box.Text, out double t))
        {
            if (box == StartBox)
                Waveform.SetSelection(Math.Min(t, Waveform.End - WaveformControl.MinGap), Waveform.End);
            else
                Waveform.SetSelection(Waveform.Start, Math.Max(t, Waveform.Start + WaveformControl.MinGap));
        }
        else
        {
            StatusText.Text = "Tempo inválido. Use o formato mm:ss.mmm, por exemplo 01:23.500.";
        }
        box.Text = CurrentText(box);
    }

    private void StartHere_Click(object sender, RoutedEventArgs e)
    {
        double t = CursorTime;
        if (t > Waveform.End - WaveformControl.MinGap)
            StatusText.Text = "O cursor está depois da linha do fim. Mova a linha vermelha primeiro.";
        else
            Waveform.SetSelection(t, Waveform.End);
    }

    private void EndHere_Click(object sender, RoutedEventArgs e)
    {
        double t = CursorTime;
        if (t < Waveform.Start + WaveformControl.MinGap)
            StatusText.Text = "O cursor está antes da linha do início. Mova a linha verde primeiro.";
        else
            Waveform.SetSelection(Waveform.Start, t);
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Waveform.SetSelection(0, Waveform.Duration);

    // ---------- Reprodução ----------

    /// <summary>Posição do cursor branco: o que está sendo ouvido, ou onde ele foi deixado.</summary>
    private double CursorTime => _player.IsActive ? _player.Position : Waveform.Playhead;

    private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlay();

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();

    private void PlayResult_Click(object sender, RoutedEventArgs e) =>
        Play(Waveform.Start, Waveform.End, PlayMode.Result);

    private void PlayHead_Click(object sender, RoutedEventArgs e) =>
        Play(Waveform.Start, Math.Min(Waveform.Start + PreviewSeconds, Waveform.End), PlayMode.Preview);

    private void PlayTail_Click(object sender, RoutedEventArgs e) =>
        Play(Math.Max(Waveform.End - PreviewSeconds, Waveform.Start), Waveform.End, PlayMode.Preview);

    private void TogglePlay()
    {
        if (_path == null || _busy) return;

        if (_player.IsPlaying)
        {
            _player.Pause();
        }
        else if (_player.IsPaused)
        {
            _player.Resume();
        }
        else
        {
            double from = Waveform.Playhead;
            if (from >= Waveform.Duration - WaveformControl.MinGap) from = 0;
            PlayFrom(from);
            return;
        }
        UpdatePlaybackState();
    }

    /// <summary>Toca a partir do ponto, parando na linha do fim se estiver ouvindo o resultado.</summary>
    private void PlayFrom(double from)
    {
        bool insideResult = _mode == PlayMode.Result && from >= Waveform.Start && from < Waveform.End;
        Play(from, insideResult ? Waveform.End : Waveform.Duration, insideResult ? PlayMode.Result : PlayMode.Full);
    }

    private void Play(double from, double to, PlayMode mode)
    {
        if (_path == null) return;
        try
        {
            _mode = mode;
            _player.Play(from, to);
            Waveform.Playhead = from;
        }
        catch (Exception ex)
        {
            StopPlayback();
            MessageBox.Show(this, "Não foi possível tocar o áudio:\n\n" + ex.Message, "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        UpdatePlaybackState();
    }

    /// <summary>O usuário escolheu outra posição no gráfico.</summary>
    private void SeekTo(double time)
    {
        if (_player.IsPlaying)
        {
            PlayFrom(Math.Min(time, Math.Max(0, Waveform.Duration - 0.01)));
        }
        else
        {
            _player.Stop(); // pausado: "Reproduzir" continua do novo ponto
            Waveform.Playhead = time;
            UpdatePlaybackState();
        }
        ShowPosition(time);
    }

    private void StopPlayback()
    {
        _player.Stop();
        OnPlaybackFinished();
    }

    private void OnPlaybackFinished()
    {
        _mode = PlayMode.Full;
        Waveform.Playhead = Waveform.Start;
        if (_path != null && !Waveform.IsDraggingPlayhead) ShowPosition(Waveform.Start);
        UpdatePlaybackState();
    }

    /// <summary>Ajusta botões e o acompanhamento do cursor ao estado do player.</summary>
    private void UpdatePlaybackState()
    {
        PlayPauseButton.Content = _player.IsPlaying ? PauseLabel : PlayLabel;
        FollowPlayback(_player.IsPlaying);
        UpdateEnabledState();
    }

    private void FollowPlayback(bool follow)
    {
        if (follow == _following) return;
        _following = follow;
        if (follow) CompositionTarget.Rendering += OnRenderingFrame;
        else CompositionTarget.Rendering -= OnRenderingFrame;
    }

    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        if (!_player.IsPlaying || Waveform.IsDraggingPlayhead) return;
        double pos = _player.Position;
        Waveform.Playhead = pos;

        // O cursor anda a cada quadro; o texto, 20 vezes por segundo.
        var now = ((RenderingEventArgs)e).RenderingTime;
        if ((now - _lastPositionText).Duration().TotalMilliseconds < 50) return;
        _lastPositionText = now;
        ShowPosition(pos);
    }

    private void ShowPosition(double time) =>
        PositionText.Text = $"{TimeFormat.Format(time)} / {TimeFormat.Format(Waveform.Duration)}";

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || Keyboard.FocusedElement is TextBox || _path == null || _busy) return;
        if (!e.IsRepeat) TogglePlay();
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

        OpenButton.IsEnabled = !_busy;
        Waveform.IsEnabled = loaded;
        foreach (var control in new Control[]
                 {
                     SaveButton, StartBox, EndBox, StartHereButton, EndHereButton, ResetButton,
                     PlayPauseButton, PlayResultButton, PlayHeadButton, PlayTailButton,
                 })
            control.IsEnabled = loaded;

        StopButton.IsEnabled = loaded && _player.IsActive;
    }
}
