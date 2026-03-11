using AudioTech.Application.Filters;

using NAudio.Dsp;

namespace AudioTech.Infrastructure.Filters;

/// <summary>
/// Parametric / graphic equalizer built from per-band BiQuad peaking-EQ
/// filters plus an L/R balance stage.
/// <para>
/// The filter holds a shared <see cref="EqualizerSettings"/> reference and
/// rebuilds its internal BiQuad array lazily whenever
/// <see cref="EqualizerSettings.Version"/> changes — no audio glitch from
/// slider moves, no per-change callback needed.
/// </para>
/// </summary>
public sealed class EqualizerFilter : INoiseFilter
{
    private readonly EqualizerSettings _settings;
    private readonly int               _channelIndex; // 0 = L, 1 = R, …
    private readonly int               _sampleRate;

    private BiQuadFilter[]? _biquads;
    private int              _lastVersion = -1;
    private float            _balanceGain = 1f;

    public FilterType Type => FilterType.Equalizer;

    public EqualizerFilter(EqualizerSettings settings, int channelIndex, int sampleRate)
    {
        _settings     = settings;
        _channelIndex = channelIndex;
        _sampleRate   = sampleRate;
    }

    public void Apply(float[] samples)
    {
        // Lazily rebuild when settings have changed since last call
        if (_biquads is null || _settings.Version != _lastVersion)
            Rebuild();

        var biquads = _biquads!;

        // Apply each band in series
        for (int b = 0; b < biquads.Length; b++)
        {
            var bq = biquads[b];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = bq.Transform(samples[i]);
        }

        // Apply L/R balance gain
        if (_balanceGain != 1f)
            for (int i = 0; i < samples.Length; i++)
                samples[i] *= _balanceGain;
    }

    public void Reset()
    {
        _biquads     = null;
        _lastVersion = -1;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void Rebuild()
    {
        int count    = EqualizerSettings.BandCount;
        var freqs    = EqualizerSettings.BandFrequencies;
        var qs       = EqualizerSettings.BandQ;
        var gains    = _settings.BandGainsDb;

        var biquads = new BiQuadFilter[count];
        for (int i = 0; i < count; i++)
        {
            float gain = gains[i];

            // Clamp frequency to just below Nyquist to keep BiQuad stable
            float freq = Math.Min(freqs[i], _sampleRate * 0.45f);

            // PeakingEQ with gain=0 is numerically a passthrough, but we avoid
            // near-zero to keep the formula stable.
            float safeGain = Math.Abs(gain) < 0.05f ? 0.05f * Math.Sign(gain == 0 ? 1 : gain) : gain;

            biquads[i] = BiQuadFilter.PeakingEQ(_sampleRate, freq, qs[i], safeGain);
        }

        _biquads  = biquads;
        _balanceGain = ComputeBalanceGain();
        _lastVersion = _settings.Version;
    }

    private float ComputeBalanceGain()
    {
        float balance = _settings.BalanceDb;

        // Channel 0 (L): attenuate when panned right (+balance).
        // Channel 1 (R): attenuate when panned left  (-balance).
        float dbAdjust = _channelIndex == 0
            ? Math.Min(0f, -balance)   // L side
            : Math.Min(0f,  balance);  // R side

        return dbAdjust == 0f ? 1f : MathF.Pow(10f, dbAdjust / 20f);
    }
}
