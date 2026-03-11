using AudioTech.Application.Filters;

namespace AudioTech.Infrastructure.Filters;

public static class FilterFactory
{
    /// <summary>
    /// Creates a filter for the given type.
    /// <para>
    /// For time-domain filters (LowPass, HighPass, NoiseGate, MovingAverage)
    /// <paramref name="strength"/> is normalized to [0, 1].
    /// </para>
    /// <para>
    /// For spectral filters (WhiteNoiseSubtraction, PurpleNoiseSubtraction)
    /// <paramref name="strength"/> is the raw noise floor in dB (e.g. 0–60).
    /// </para>
    /// <para>
    /// For <see cref="FilterType.Equalizer"/>, <paramref name="equalizerSettings"/>
    /// must be supplied; <paramref name="sampleRate"/> and
    /// <paramref name="channelIndex"/> determine BiQuad coefficients and balance.
    /// </para>
    /// </summary>
    public static INoiseFilter Create(
        FilterType         type,
        float              strength,
        int                sampleRate      = 44100,
        EqualizerSettings? equalizerSettings = null,
        int                channelIndex    = 0)
    {
        return type switch
        {
            FilterType.LowPass       => new LowPassFilter(Math.Clamp(strength, 0f, 1f)),
            FilterType.HighPass      => new HighPassFilter(Math.Clamp(strength, 0f, 1f)),
            FilterType.NoiseGate     => new NoiseGateFilter(Math.Clamp(strength, 0f, 1f)),
            FilterType.MovingAverage => new MovingAverageFilter(Math.Clamp(strength, 0f, 1f)),

            FilterType.WhiteNoiseSubtraction  => new WhiteNoiseSubtractionFilter(strength),
            FilterType.PurpleNoiseSubtraction => new PurpleNoiseSubtractionFilter(strength),
            FilterType.BrownNoiseSubtraction  => new BrownNoiseSubtractionFilter(strength),

            FilterType.Equalizer => new EqualizerFilter(
                equalizerSettings ?? new EqualizerSettings(),
                channelIndex,
                sampleRate),

            _ => new PassthroughFilter()
        };
    }
}
