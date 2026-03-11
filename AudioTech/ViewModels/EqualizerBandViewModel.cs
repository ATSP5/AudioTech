using AudioTech.Application.Filters;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AudioTech.ViewModels;

public partial class EqualizerBandViewModel : ObservableObject
{
    private readonly EqualizerSettings _settings;

    public int    BandIndex { get; }
    public string Label     { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GainDisplay))]
    private double _gainDb;

    public string GainDisplay => GainDb switch
    {
        > 0.05  => $"+{GainDb:0.#}",
        < -0.05 => $"{GainDb:0.#}",
        _       => "0"
    };

    public EqualizerBandViewModel(int bandIndex, EqualizerSettings settings)
    {
        BandIndex = bandIndex;
        Label     = EqualizerSettings.BandLabels[bandIndex];
        _settings = settings;
    }

    partial void OnGainDbChanged(double value) =>
        _settings.SetBandGain(BandIndex, (float)value);
}
