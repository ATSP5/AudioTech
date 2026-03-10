using AudioTech.Application.Filters;

namespace AudioTech.Infrastructure.Filters;

/// <summary>
/// Hard noise gate: samples whose amplitude is below the threshold are silenced.
/// strength 0 → threshold≈0 (barely any gating) | strength 1 → threshold≈0.5 (aggressive)
/// Uses a short release envelope to avoid hard clicks on gate close.
/// </summary>
public sealed class NoiseGateFilter : INoiseFilter
{
    public FilterType Type => FilterType.NoiseGate;

    private readonly float _threshold;
    private float _envelope;
    private const float AttackCoeff  = 0.999f;   // open fast
    private const float ReleaseCoeff = 0.995f;   // close slower

    public NoiseGateFilter(float strength)
    {
        // Quadratic curve: low strengths are near-silent, 100% → threshold 0.5
        // strength=0.01 → 0.00005  |  strength=0.5 → 0.125  |  strength=1.0 → 0.5
        _threshold = strength * strength * 0.5f;
    }

    public void Apply(float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float level = MathF.Abs(samples[i]);

            if (level > _threshold)
                _envelope = AttackCoeff  * _envelope + (1f - AttackCoeff)  * 1f;
            else
                _envelope = ReleaseCoeff * _envelope + (1f - ReleaseCoeff) * 0f;

            samples[i] *= _envelope;
        }
    }

    public void Reset() => _envelope = 0f;
}
