using System.Collections.ObjectModel;

using AudioTech.Application.Filters;
using AudioTech.Application.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioTech.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IAudioCaptureService _captureService;

    // ── Equalizer ─────────────────────────────────────────────────────────────

    public EqualizerViewModel Equalizer { get; }

    // ── Microphone channels ──────────────────────────────────────────────────

    public ObservableCollection<MicrophoneChannelConfig> Channels { get; } = [];

    // ── FFT ──────────────────────────────────────────────────────────────────

    public IReadOnlyList<int> FftSizes    { get; } = [512, 1024, 2048, 4096, 8192];
    public IReadOnlyList<int> SampleRates { get; } = [22050, 44100, 48000];

    [ObservableProperty] private int _selectedFftSize    = 4096;
    [ObservableProperty] private int _selectedSampleRate = 44100;

    // ── Noise filter ─────────────────────────────────────────────────────────

    public IReadOnlyList<FilterOption> FilterOptions { get; } = FilterOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsFilterEnabled),
        nameof(IsFilterDbBased),
        nameof(FilterStrengthMax),
        nameof(FilterStrengthUnit),
        nameof(FilterStrengthDisplay),
        nameof(FilterStrengthValue),
        nameof(IsStrengthVisible),
        nameof(IsEqualizerSelected))]
    private FilterOption _selectedFilter = FilterOption.All[0]; // "No Filter"

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterStrengthDisplay), nameof(FilterStrengthValue))]
    private double _filterStrength = 50;

    // ── Filter computed properties ────────────────────────────────────────────

    public bool IsFilterEnabled    => SelectedFilter.Type != FilterType.None;
    public bool IsFilterDbBased    => SelectedFilter.IsDbBased;
    public bool IsEqualizerSelected => SelectedFilter.Type == FilterType.Equalizer;

    /// <summary>Hide the generic strength slider when Equalizer is active.</summary>
    public bool IsStrengthVisible  => IsFilterEnabled && !IsEqualizerSelected;

    /// <summary>Slider maximum: 60 dB for spectral filters, 100 for %-based.</summary>
    public double FilterStrengthMax => IsFilterDbBased ? 60 : 100;

    /// <summary>Unit label shown next to the slider value.</summary>
    public string FilterStrengthUnit => IsFilterDbBased ? "dB" : "%";

    /// <summary>Formatted value + unit for display (e.g. "30 dB" or "75%").</summary>
    public string FilterStrengthDisplay =>
        IsFilterDbBased ? $"{FilterStrength:0} dB" : $"{FilterStrength:0}%";

    /// <summary>
    /// Value passed to <see cref="IAudioCaptureService.ConfigureFilter"/>:
    /// raw dB for spectral filters, normalized [0,1] for time-domain filters.
    /// </summary>
    public float FilterStrengthValue =>
        IsFilterDbBased ? (float)FilterStrength : (float)(FilterStrength / 100.0);

    // Reset slider to a sensible default when switching filter types
    partial void OnSelectedFilterChanged(FilterOption value)
    {
        FilterStrength = value.IsDbBased ? 0 : 50;
    }

    // ── Display range ────────────────────────────────────────────────────────

    [ObservableProperty] private double _minDb        = -120;
    [ObservableProperty] private double _maxDb        = -40;
    [ObservableProperty] private double _minFrequency = 20;
    [ObservableProperty] private double _maxFrequency = 20000;

    // ── ctor ─────────────────────────────────────────────────────────────────

    public SettingsViewModel(IAudioCaptureService captureService, EqualizerViewModel equalizer)
    {
        _captureService = captureService;
        Equalizer       = equalizer;

        // Register the shared EQ settings object up-front so filters created
        // with FilterType.Equalizer always reference the correct instance.
        _captureService.SetEqualizerSettings(equalizer.Settings);

        Channels.Add(new MicrophoneChannelConfig(0) { IsEnabled = true });
        Channels.Add(new MicrophoneChannelConfig(1));

        foreach (var ch in Channels)
            ch.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MicrophoneChannelConfig.GainDb))
                    _captureService.SetChannelGain(ch.ChannelIndex, (float)ch.GainDb);
                else if (e.PropertyName == nameof(MicrophoneChannelConfig.IsPassthrough))
                    _captureService.SetChannelPassthrough(ch.ChannelIndex, ch.IsPassthrough);
            };

        RefreshDevicesCommand.Execute(null);
    }

    [RelayCommand]
    public void RefreshDevices()
    {
        var devices = _captureService.GetAvailableDevices();

        foreach (var ch in Channels)
        {
            int prev = ch.SelectedDeviceIndex;
            ch.AvailableDevices.Clear();
            foreach (var d in devices)
                ch.AvailableDevices.Add(d.Name);

            ch.SelectedDeviceIndex = devices.Count > 0
                ? Math.Clamp(prev, 0, devices.Count - 1)
                : -1;
        }
    }
}
