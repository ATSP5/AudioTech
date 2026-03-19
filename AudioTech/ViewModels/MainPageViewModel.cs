using System.Collections.Concurrent;
using System.Text;

using AudioTech.Application.Services;

using Avalonia.Media.Imaging;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using SkiaSharp;

namespace AudioTech.ViewModels;

public partial class MainPageViewModel : ViewModelBase
{
    private readonly IAudioCaptureService _captureService;
    private readonly SettingsViewModel    _settings;
    private readonly IDialogService       _dialog;

    // Latest frame per channel — written from ThreadPool, read from UI thread
    private readonly ConcurrentDictionary<int, FftFrame> _latestFrames = new();

    // Smoothed phase difference state (ch1 phase − ch0 phase, circular EMA)
    private float[] _smoothedPhaseDiff = [];
    private const float PhaseSmoothAlpha = 0.12f;  // lower = smoother

    // ── Observable properties ─────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    private bool _isRunning;

    /// <summary>Incrementing this clears the waterfall history.</summary>
    [ObservableProperty]
    private int _waterfallResetToken;

    [ObservableProperty]
    private bool _isPassthroughEnabled;

    partial void OnIsPassthroughEnabledChanged(bool value) =>
        _captureService.SetPassthrough(value);

    [ObservableProperty]
    private string _statusText = "Stopped";

    /// <summary>Per-channel magnitude data for the FFT graph.</summary>
    [ObservableProperty]
    private IReadOnlyList<ChannelDisplayData> _channels = [];

    /// <summary>
    /// Smoothed phase difference (Ch2 − Ch1 reference) in radians [-π, +π].
    /// Null when fewer than 2 channels are active.
    /// </summary>
    [ObservableProperty]
    private float[]? _phaseDifferenceData;

    /// <summary>Sample rate of the phase-difference curve (used for frequency axis).</summary>
    [ObservableProperty]
    private int _phaseDiffSampleRate;

    /// <summary>Linear-domain mix of all active channels for the waterfall.</summary>
    [ObservableProperty]
    private float[]? _mixedFftData;

    [ObservableProperty]
    private int _mixedFftSampleRate = 44100;

    // ── Display settings forwarded from SettingsViewModel ─────────────────────

    public double MinDb        => _settings.MinDb;
    public double MaxDb        => _settings.MaxDb;
    public double MinFrequency => _settings.MinFrequency;
    public double MaxFrequency => _settings.MaxFrequency;

    public string ToggleButtonText => IsRunning ? "⏹  Stop" : "▶  Start";

    // ── ctor ──────────────────────────────────────────────────────────────────

    // ── Export delegates — set by MainPage code-behind ─────────────────────────

    /// <summary>Renders the FFT graph to a bitmap. Registered by MainPage.</summary>
    public Func<RenderTargetBitmap?>? GetFftBitmap { get; set; }

    /// <summary>Returns the current waterfall bitmap. Registered by MainPage.</summary>
    public Func<WriteableBitmap?>? GetWaterfallBitmap { get; set; }

    // ─────────────────────────────────────────────────────────────────────────

    public RecordPlayViewModel RecordPlay { get; }

    public MainPageViewModel(
        IAudioCaptureService captureService,
        SettingsViewModel    settings,
        RecordPlayViewModel  recordPlay,
        IDialogService       dialog)
    {
        _captureService = captureService;
        _settings       = settings;
        RecordPlay      = recordPlay;
        _dialog         = dialog;

        _captureService.FftFrameReady += OnFftFrameReady;
        RecordPlay.PlaybackFftReady   += OnFftFrameReady; // playback uses channel 0

        // When playback starts: auto-stop mic capture and disable the toggle button.
        // When playback ends: re-enable it.
        RecordPlay.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(RecordPlayViewModel.IsPlaying)) return;

            if (RecordPlay.IsPlaying)
            {
                // Stop mic capture if running
                if (IsRunning) StopCaptureCommand.Execute(null);
                WaterfallResetToken++;
            }

            ToggleCaptureCommand.NotifyCanExecuteChanged();
        };

        // Live-update filter while capture is running
        _settings.PropertyChanged += (_, e) =>
        {
            if (IsRunning && e.PropertyName is
                nameof(SettingsViewModel.SelectedFilter) or
                nameof(SettingsViewModel.FilterStrength))
            {
                _captureService.ConfigureFilter(
                    _settings.SelectedFilter.Type,
                    _settings.FilterStrengthValue);
            }
        };
    }

    // ── Audio → UI pipeline ───────────────────────────────────────────────────

    private void OnFftFrameReady(object? sender, FftFrame frame)
    {
        _latestFrames[frame.ChannelIndex] = frame;
        Dispatcher.UIThread.Post(RefreshDisplayData, DispatcherPriority.Render);
    }

    private void RefreshDisplayData()
    {
        if (_latestFrames.IsEmpty) return;

        var frames = _latestFrames.Values
            .OrderBy(f => f.ChannelIndex)
            .ToList();

        // Build per-channel magnitude display records
        var channelList = frames
            .Select(f => new ChannelDisplayData(
                f.ChannelIndex,
                $"Ch {f.ChannelIndex + 1}: {TruncateName(f.DeviceName)}",
                f.MagnitudeDb,
                f.SampleRate,
                ChannelPalette.ForChannel(f.ChannelIndex)))
            .ToList();

        Channels = channelList;

        // Phase difference — only meaningful with ≥2 channels
        if (frames.Count >= 2)
        {
            var ref0  = frames[0]; // reference (Ch1)
            var ref1  = frames[1]; // measured  (Ch2)

            int bins = Math.Min(ref0.PhaseRadians.Length, ref1.PhaseRadians.Length);

            if (_smoothedPhaseDiff.Length != bins)
            {
                _smoothedPhaseDiff = new float[bins]; // reset on size change
            }

            for (int i = 0; i < bins; i++)
            {
                float diff = ref1.PhaseRadians[i] - ref0.PhaseRadians[i];

                // Wrap to [-π, +π]
                diff = WrapAngle(diff);

                // Circular EMA: average via unit-vector sum then atan2
                float sinPrev = MathF.Sin(_smoothedPhaseDiff[i]);
                float cosPrev = MathF.Cos(_smoothedPhaseDiff[i]);
                float sinNew  = MathF.Sin(diff);
                float cosNew  = MathF.Cos(diff);

                float sinAvg  = PhaseSmoothAlpha * sinNew + (1f - PhaseSmoothAlpha) * sinPrev;
                float cosAvg  = PhaseSmoothAlpha * cosNew + (1f - PhaseSmoothAlpha) * cosPrev;

                _smoothedPhaseDiff[i] = MathF.Atan2(sinAvg, cosAvg);
            }

            PhaseDifferenceData  = (float[])_smoothedPhaseDiff.Clone();
            PhaseDiffSampleRate  = ref0.SampleRate;
        }
        else
        {
            PhaseDifferenceData = null;
            _smoothedPhaseDiff  = [];
        }

        // Waterfall: linear-domain average of all channels
        MixedFftData = ComputeMix(channelList);
        MixedFftSampleRate = frames.Count > 0 ? frames[0].SampleRate : 44100;
    }

    private static float WrapAngle(float rad)
    {
        while (rad >  MathF.PI) rad -= 2f * MathF.PI;
        while (rad < -MathF.PI) rad += 2f * MathF.PI;
        return rad;
    }

    private static float[] ComputeMix(IReadOnlyList<ChannelDisplayData> channels)
    {
        if (channels.Count == 0) return [];

        int bins = channels[0].MagnitudeDb.Length;
        var mix  = new float[bins];

        for (int i = 0; i < bins; i++)
        {
            double linearSum = 0;
            foreach (var ch in channels)
                linearSum += Math.Pow(10, ch.MagnitudeDb[i] / 20.0);

            double avg = linearSum / channels.Count;
            mix[i] = avg > 0 ? (float)(20 * Math.Log10(avg)) : -160f;
        }

        return mix;
    }

    private static string TruncateName(string name) =>
        name.Length > 22 ? name[..22] + "…" : name;

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void StartCapture()
    {
        // Apply chosen filter before starting any channel
        _captureService.ConfigureFilter(
            _settings.SelectedFilter.Type,
            _settings.FilterStrengthValue);

        var devices = _captureService.GetAvailableDevices();
        bool any    = false;

        foreach (var ch in _settings.Channels)
        {
            if (!ch.IsEnabled || ch.SelectedDeviceIndex < 0) continue;
            if (ch.SelectedDeviceIndex >= devices.Count)     continue;

            _captureService.StartChannel(
                ch.ChannelIndex,
                ch.SelectedDeviceIndex,
                _settings.SelectedSampleRate,
                _settings.SelectedFftSize);
            any = true;
        }

        if (any)
        {
            IsRunning  = true;
            StatusText = $"Running — {_captureService.ActiveChannels.Count} channel(s)";
            WaterfallResetToken++;
        }
    }

    [RelayCommand]
    private void StopCapture()
    {
        _captureService.StopAll();
        _latestFrames.Clear();
        _smoothedPhaseDiff  = [];
        Channels            = [];
        MixedFftData        = null;
        PhaseDifferenceData = null;
        IsRunning           = false;
        StatusText          = "Stopped";
    }

    [RelayCommand(CanExecute = nameof(CanToggleCaptureImpl))]
    private void ToggleCapture()
    {
        if (IsRunning) StopCaptureCommand.Execute(null);
        else           StartCaptureCommand.Execute(null);
    }

    private bool CanToggleCaptureImpl() => !RecordPlay.IsPlaying;

    // ── Export commands ────────────────────────────────────────────────────────

    private static readonly (string Name, string[] Patterns)[] ImageFileTypes =
    [
        ("PNG Image",    ["png"]),
        ("JPEG Image",   ["jpg", "jpeg"]),
        ("Bitmap Image", ["bmp"])
    ];

    private static readonly (string Name, string[] Patterns)[] CsvFileTypes =
    [
        ("CSV File", ["csv"])
    ];

    [RelayCommand]
    private async Task ExportAsync(string target)
    {
        switch (target)
        {
            case "upper-csv":   await ExportUpperCsvAsync();   break;
            case "upper-image": await ExportUpperImageAsync(); break;
            case "lower-csv":   await ExportLowerCsvAsync();   break;
            case "lower-image": await ExportLowerImageAsync(); break;
            case "both-image":  await ExportBothImagesAsync(); break;
        }
    }

    private async Task ExportUpperCsvAsync()
    {
        var channels = Channels;
        if (channels.Count == 0) return;

        var path = await _dialog.ShowSaveFilePickerAsync("fft_spectrum", CsvFileTypes);
        if (path is null) return;

        var sb = new StringBuilder();

        // Header
        sb.Append("Frequency (Hz)");
        foreach (var ch in channels)
            sb.Append($",{ch.Label} (dB)");
        var phase = PhaseDifferenceData;
        if (phase is not null)
            sb.Append(",Phase Diff (rad)");
        sb.AppendLine();

        // Data rows
        int bins = channels[0].MagnitudeDb.Length;
        double binHz = channels[0].SampleRate / 2.0 / bins;
        for (int i = 1; i < bins; i++)
        {
            double freq = i * binHz;
            if (freq < MinFrequency || freq > MaxFrequency) continue;

            sb.Append($"{freq:F2}");
            foreach (var ch in channels)
                sb.Append($",{ch.MagnitudeDb[i]:F4}");
            if (phase is not null && i < phase.Length)
                sb.Append($",{phase[i]:F6}");
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(path, sb.ToString());
    }

    private async Task ExportLowerCsvAsync()
    {
        var data = MixedFftData;
        if (data is null || data.Length == 0) return;

        var path = await _dialog.ShowSaveFilePickerAsync("waterfall_spectrum", CsvFileTypes);
        if (path is null) return;

        var sb = new StringBuilder();
        sb.AppendLine("Frequency (Hz),Magnitude (dB)");

        int bins = data.Length;
        double binHz = MixedFftSampleRate / 2.0 / bins;
        for (int i = 1; i < bins; i++)
        {
            double freq = i * binHz;
            if (freq < MinFrequency || freq > MaxFrequency) continue;
            sb.AppendLine($"{freq:F2},{data[i]:F4}");
        }

        await File.WriteAllTextAsync(path, sb.ToString());
    }

    private async Task ExportUpperImageAsync()
    {
        var bitmap = GetFftBitmap?.Invoke();
        if (bitmap is null) return;

        var path = await _dialog.ShowSaveFilePickerAsync("fft_graph", ImageFileTypes);
        if (path is null) return;

        await SaveBitmapAsync(bitmap, path);
    }

    private async Task ExportLowerImageAsync()
    {
        var bitmap = GetWaterfallBitmap?.Invoke();
        if (bitmap is null) return;

        var path = await _dialog.ShowSaveFilePickerAsync("waterfall", ImageFileTypes);
        if (path is null) return;

        await SaveBitmapAsync(bitmap, path);
    }

    private async Task ExportBothImagesAsync()
    {
        var fftBitmap = GetFftBitmap?.Invoke();
        if (fftBitmap is not null)
        {
            var path = await _dialog.ShowSaveFilePickerAsync("fft_graph", ImageFileTypes);
            if (path is not null)
                await SaveBitmapAsync(fftBitmap, path);
        }

        var watBitmap = GetWaterfallBitmap?.Invoke();
        if (watBitmap is not null)
        {
            var path = await _dialog.ShowSaveFilePickerAsync("waterfall", ImageFileTypes);
            if (path is not null)
                await SaveBitmapAsync(watBitmap, path);
        }
    }

    private static async Task SaveBitmapAsync(Bitmap bitmap, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".png")
        {
            bitmap.Save(path);
            return;
        }

        // For JPEG and BMP, round-trip through SkiaSharp
        using var ms = new MemoryStream();
        bitmap.Save(ms);
        ms.Position = 0;

        using var skBitmap = SKBitmap.Decode(ms);
        var format = ext switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".bmp"            => SKEncodedImageFormat.Bmp,
            _                 => SKEncodedImageFormat.Png
        };

        using var encoded = skBitmap.Encode(format, 90);
        await File.WriteAllBytesAsync(path, encoded.ToArray());
    }
}
