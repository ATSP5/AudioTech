using AudioTech.Application.Services;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioTech.ViewModels;

public partial class RecordPlayViewModel : ViewModelBase
{
    private readonly IAudioRecordPlayService _service;
    private readonly IDialogService          _dialog;
    private readonly SettingsViewModel       _settings;
    private readonly DispatcherTimer         _uiTimer;

    // Prevents seeking feedback-loop when slider is driven by the position timer
    private bool _seekInProgress;

    // ── Observable properties ─────────────────────────────────────────────────

    [ObservableProperty] private bool   _isRecording;
    [ObservableProperty] private bool   _isPlaying;
    [ObservableProperty] private string _timeDisplay = "0:00 / 0:00";
    [ObservableProperty] private double _progress;          // 0 – 1
    [ObservableProperty] private bool   _isProgressEnabled; // true only while playing
    [ObservableProperty] private string _fileLabel = "No file";

    // ── Can-execute helpers (also used as x:Name-free can-execute for buttons) ─

    public bool CanRecord => !IsRecording && !IsPlaying;
    public bool CanStop   => IsRecording  || IsPlaying;
    public bool CanPlay   => !IsRecording && !IsPlaying && _service.HasLoadedFile;
    public bool CanBrowse => !IsRecording;

    // ── Forwarded playback FFT (MainPageViewModel subscribes to this) ─────────

    public event EventHandler<FftFrame>? PlaybackFftReady;

    // ── ctor ──────────────────────────────────────────────────────────────────

    public RecordPlayViewModel(
        IAudioRecordPlayService service,
        IDialogService          dialog,
        SettingsViewModel       settings)
    {
        _service  = service;
        _dialog   = dialog;
        _settings = settings;

        _service.StateChanged     += (_, _) => Dispatcher.UIThread.Post(SyncFromService);
        _service.PlaybackFftReady += (s, e)  => PlaybackFftReady?.Invoke(s, e);
        _settings.PropertyChanged += OnSettingsPropertyChanged;

        _uiTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            (_, _) => UpdateTimeDisplay());
    }

    // ── Sync state from service → ViewModel ──────────────────────────────────

    private void SyncFromService()
    {
        IsRecording       = _service.IsRecording;
        IsPlaying         = _service.IsPlaying;
        IsProgressEnabled = _service.IsPlaying;
        FileLabel         = _service.LoadedFilePath != null
            ? Path.GetFileName(_service.LoadedFilePath)!
            : "No file";

        if (IsRecording || IsPlaying)
            _uiTimer.Start();
        else
            _uiTimer.Stop();

        UpdateTimeDisplay();
        NotifyCanExecuteAll();
    }

    private void UpdateTimeDisplay()
    {
        if (_service.IsRecording)
        {
            var d = _service.RecordingDuration;
            TimeDisplay = $"● {d.Minutes:D2}:{d.Seconds:D2}";
            Progress    = 0;
        }
        else if (_service.IsPlaying)
        {
            var pos = _service.PlaybackPosition;
            var dur = _service.PlaybackDuration;
            TimeDisplay = $"{pos.Minutes:D2}:{pos.Seconds:D2} / {dur.Minutes:D2}:{dur.Seconds:D2}";

            if (!_seekInProgress && dur > TimeSpan.Zero)
                Progress = pos.TotalSeconds / dur.TotalSeconds;
        }
        else
        {
            var dur = _service.PlaybackDuration;
            TimeDisplay = $"0:00 / {dur.Minutes:D2}:{dur.Seconds:D2}";
        }
    }

    private void NotifyCanExecuteAll()
    {
        OnPropertyChanged(nameof(CanRecord));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanBrowse));
        RecordCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        BrowseCommand.NotifyCanExecuteChanged();
    }

    // ── Slider seek helpers (called from MainPage code-behind) ─────────────

    public void BeginSeek() => _seekInProgress = true;

    public void EndSeek(double fraction)
    {
        _service.SeekTo(fraction);
        _seekInProgress = false;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRecordImpl))]
    private void Record()
    {
        _service.StartRecording(_settings.SelectedSampleRate);
    }
    private bool CanRecordImpl() => !IsRecording && !IsPlaying;

    [RelayCommand(CanExecute = nameof(CanStopImpl))]
    private async Task StopAsync()
    {
        if (_service.IsRecording)
        {
            _service.StopRecording();

            var path = await _dialog.ShowSaveFilePickerAsync(
                "recording",
                new[]
                {
                    ("WAV Audio", new[] { "wav" }),
                    ("MP3 Audio", new[] { "mp3" })
                });

            if (path != null)
            {
                var fmt = path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                    ? AudioSaveFormat.Mp3
                    : AudioSaveFormat.Wav;
                await _service.SaveRecordingAsync(path, fmt);
            }
        }
        else
        {
            _service.StopPlayback();
        }
    }
    private bool CanStopImpl() => IsRecording || IsPlaying;

    [RelayCommand(CanExecute = nameof(CanPlayImpl))]
    private void Play()
    {
        _service.StartPlayback(_settings.SelectedFilter.Type, _settings.FilterStrengthValue, _settings.Equalizer.Settings);
    }
    private bool CanPlayImpl() => !IsRecording && !IsPlaying && _service.HasLoadedFile;

    [RelayCommand(CanExecute = nameof(CanBrowseImpl))]
    private async Task BrowseAsync()
    {
        var path = await _dialog.ShowOpenFilePickerAsync(
            new[]
            {
                ("Audio Files", new[] { "wav", "mp3", "flac", "aiff", "ogg" }),
                ("All Files",   new[] { "*" })
            });

        if (path != null)
            _service.LoadFile(path);
    }
    private bool CanBrowseImpl() => !IsRecording;

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_service.IsPlaying) return;
        if (e.PropertyName is nameof(SettingsViewModel.SelectedFilter) or nameof(SettingsViewModel.FilterStrength))
            _service.UpdatePlaybackFilter(_settings.SelectedFilter.Type, _settings.FilterStrengthValue, _settings.Equalizer.Settings);
    }
}
