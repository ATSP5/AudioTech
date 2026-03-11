using AudioTech.Application.Filters;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioTech.ViewModels;

public partial class EqualizerViewModel : ViewModelBase
{
    /// <summary>Shared settings read by <c>EqualizerFilter</c> instances.</summary>
    public EqualizerSettings Settings { get; } = new();

    public IReadOnlyList<EqualizerBandViewModel> Bands { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BalanceDisplay))]
    private double _balanceDb;

    public string BalanceDisplay => BalanceDb switch
    {
        > 0.05  => $"R +{BalanceDb:0.#} dB",
        < -0.05 => $"L +{-BalanceDb:0.#} dB",
        _       => "Center"
    };

    public EqualizerViewModel()
    {
        Bands = Enumerable
            .Range(0, EqualizerSettings.BandCount)
            .Select(i => new EqualizerBandViewModel(i, Settings))
            .ToList()
            .AsReadOnly();
    }

    partial void OnBalanceDbChanged(double value) =>
        Settings.BalanceDb = (float)value;

    [RelayCommand]
    private void ResetEq()
    {
        Settings.Reset(); // increments version — filters rebuild automatically

        foreach (var band in Bands)
            band.GainDb = 0;

        BalanceDb = 0;
    }
}
