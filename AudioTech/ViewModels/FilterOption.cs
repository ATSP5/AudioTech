using AudioTech.Application.Filters;

namespace AudioTech.ViewModels;

/// <param name="IsDbBased">
/// When true the strength slider uses dB (0–60) instead of percent (0–100).
/// </param>
public sealed record FilterOption(FilterType Type, string DisplayName, bool IsDbBased = false)
{
    public override string ToString() => DisplayName;

    public static IReadOnlyList<FilterOption> All { get; } =
    [
        new(FilterType.None,                  "No Filter"),
        new(FilterType.LowPass,               "Low-Pass (smooth highs)"),
        new(FilterType.HighPass,              "High-Pass (cut rumble)"),
        new(FilterType.NoiseGate,             "Noise Gate"),
        new(FilterType.MovingAverage,         "Moving Average"),
        new(FilterType.WhiteNoiseSubtraction, "White Noise Subtraction", IsDbBased: true),
        new(FilterType.PurpleNoiseSubtraction,"Purple Noise Subtraction (+6 dB/oct)", IsDbBased: true),
        new(FilterType.BrownNoiseSubtraction, "Brown Noise Subtraction (−6 dB/oct)",  IsDbBased: true),
    ];
}
