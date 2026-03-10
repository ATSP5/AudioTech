using AudioTech.Application.Filters;

namespace AudioTech.Infrastructure.Filters;

/// <summary>
/// Causal moving average over the last N samples.
/// strength 0 → window=1 (no effect) | strength 1 → window=64 (heavy smoothing)
/// </summary>
public sealed class MovingAverageFilter : INoiseFilter
{
    public FilterType Type => FilterType.MovingAverage;

    private readonly int    _windowSize;
    private readonly float[] _ring;
    private int   _head;
    private float _sum;

    public MovingAverageFilter(float strength)
    {
        _windowSize = 1 + (int)(strength * 63);  // [1 .. 64]
        _ring       = new float[_windowSize];
    }

    public void Apply(float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            _sum          -= _ring[_head];
            _ring[_head]   = samples[i];
            _sum          += samples[i];
            _head          = (_head + 1) % _windowSize;
            samples[i]     = _sum / _windowSize;
        }
    }

    public void Reset()
    {
        Array.Clear(_ring);
        _head = 0;
        _sum  = 0f;
    }
}
