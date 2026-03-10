using Avalonia.Media;

namespace AudioTech.ViewModels;

/// <summary>Snapshot of one channel's FFT data ready for rendering (magnitude only).</summary>
public sealed record ChannelDisplayData(
    int     ChannelIndex,
    string  Label,
    float[] MagnitudeDb,
    int     SampleRate,
    Color   Color);

/// <summary>Shared palette — index maps to channel index.</summary>
public static class ChannelPalette
{
    public static readonly Color[] Colors =
    [
        Color.FromRgb(210, 40,  40),   // 0 — red
        Color.FromRgb(220, 180, 30),   // 1 — gold/yellow
        Color.FromRgb(30,  200, 220),  // 2 — cyan
        Color.FromRgb(70,  220, 80),   // 3 — green
    ];

    public static Color ForChannel(int index) =>
        Colors[index % Colors.Length];
}
