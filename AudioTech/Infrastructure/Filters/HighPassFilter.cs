using AudioTech.Application.Filters;

namespace AudioTech.Infrastructure.Filters;

/// <summary>
/// Single-pole IIR high-pass: y[n] = β·(y[n−1] + x[n] − x[n−1])
/// strength 0 → β≈0.99 (barely removes DC) | strength 1 → β≈0.30 (aggressive low-cut)
/// </summary>
public sealed class HighPassFilter : INoiseFilter
{
    public FilterType Type => FilterType.HighPass;

    private readonly float _beta;
    private float _prevIn;
    private float _prevOut;

    public HighPassFilter(float strength)
    {
        // Higher strength → smaller beta → higher cutoff
        _beta = 0.99f - strength * 0.69f;   // [0.99 .. 0.30]
    }

    public void Apply(float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float x    = samples[i];
            float y    = _beta * (_prevOut + x - _prevIn);
            _prevIn    = x;
            _prevOut   = y;
            samples[i] = y;
        }
    }

    public void Reset() { _prevIn = 0f; _prevOut = 0f; }
}
