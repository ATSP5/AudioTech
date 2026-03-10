namespace AudioTech.Application.Filters;

public interface INoiseFilter
{
    FilterType Type     { get; }

    /// <summary>
    /// Applies the filter in-place. Returns the same array (mutated) for zero allocation.
    /// </summary>
    void Apply(float[] samples);

    /// <summary>Reset all internal state (use when restarting a channel).</summary>
    void Reset();
}
