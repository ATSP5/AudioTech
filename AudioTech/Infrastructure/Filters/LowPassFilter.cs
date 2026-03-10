using AudioTech.Application.Filters;

namespace AudioTech.Infrastructure.Filters;

/// <summary>
/// Single-pole IIR low-pass: y[n] = α·x[n] + (1−α)·y[n−1]
/// strength 0 → α≈1.0 (no effect) | strength 1 → α≈0.02 (heavy smoothing)
/// </summary>
public sealed class LowPassFilter : INoiseFilter
{
    public FilterType Type => FilterType.LowPass;

    private readonly float _alpha;
    private float _prev;

    public LowPassFilter(float strength)
    {
        // Map strength [0,1] → alpha [0.99, 0.02] exponentially for perceptual linearity
        _alpha = MathF.Pow(0.02f, strength);  // strength=0 → 1.0, strength=1 → 0.02
    }

    public void Apply(float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            _prev      = _alpha * samples[i] + (1f - _alpha) * _prev;
            samples[i] = _prev;
        }
    }

    public void Reset() => _prev = 0f;
}
