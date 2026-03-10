using AudioTech.Application.Filters;

namespace AudioTech.Infrastructure.Filters;

/// <summary>No-op filter — passes samples through unchanged.</summary>
public sealed class PassthroughFilter : INoiseFilter
{
    public FilterType Type => FilterType.None;
    public void Apply(float[] samples) { }
    public void Reset() { }
}
