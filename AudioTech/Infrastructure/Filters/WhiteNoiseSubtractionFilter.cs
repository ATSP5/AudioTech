using AudioTech.Application.Filters;

namespace AudioTech.Infrastructure.Filters;

/// <summary>
/// Spectral subtraction of a flat (white) noise floor.
/// Subtracts <paramref name="floorDb"/> dB uniformly from every FFT bin.
/// Bins cannot drop below -160 dB.
/// </summary>
public sealed class WhiteNoiseSubtractionFilter : ISpectralFilter
{
    public FilterType Type => FilterType.WhiteNoiseSubtraction;

    private readonly float _floorDb;

    /// <param name="floorDb">Noise floor to subtract in dB (e.g. 6, 12, 30).</param>
    public WhiteNoiseSubtractionFilter(float floorDb) => _floorDb = floorDb;

    // No time-domain processing needed
    public void Apply(float[] samples) { }
    public void Reset() { }

    public void ApplySpectral(float[] magnitudeDb, int sampleRate, int fftSize)
    {
        for (int i = 0; i < magnitudeDb.Length; i++)
            magnitudeDb[i] = MathF.Max(magnitudeDb[i] - _floorDb, -160f);
    }
}
