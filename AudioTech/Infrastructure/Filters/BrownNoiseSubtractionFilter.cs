using AudioTech.Application.Filters;

namespace AudioTech.Infrastructure.Filters;

/// <summary>
/// Spectral subtraction of a brown/red noise floor (−6 dB/octave, −20 dB/decade).
/// This is the mirror image of purple noise: strong subtraction at low frequencies,
/// weak subtraction at high frequencies.
///
/// The <paramref name="floorDb"/> value sets the floor at the reference frequency
/// (1 kHz). Below 1 kHz the floor increases; above it decreases.
///
///   floor_i = floorDb − 20 · log10(freq_i / 1000 Hz)
/// </summary>
public sealed class BrownNoiseSubtractionFilter : ISpectralFilter
{
    public FilterType Type => FilterType.BrownNoiseSubtraction;

    private readonly float _floorDb;
    private const double RefFreqHz = 1000.0;

    /// <param name="floorDb">Noise floor at 1 kHz in dB (e.g. 6, 12, 30).</param>
    public BrownNoiseSubtractionFilter(float floorDb) => _floorDb = floorDb;

    public void Apply(float[] samples) { }
    public void Reset() { }

    public void ApplySpectral(float[] magnitudeDb, int sampleRate, int fftSize)
    {
        int    bins  = magnitudeDb.Length;
        double binHz = sampleRate / 2.0 / bins;

        for (int i = 1; i < bins; i++)
        {
            double freq  = i * binHz;
            // Negative log term: low freq → large floor, high freq → small (or negative) floor
            float  floor = _floorDb - (float)(20.0 * Math.Log10(freq / RefFreqHz));
            // Only subtract where the floor is positive — don't boost bins
            if (floor > 0f)
                magnitudeDb[i] = MathF.Max(magnitudeDb[i] - floor, -160f);
        }

        // DC bin — subtract at reference level
        magnitudeDb[0] = MathF.Max(magnitudeDb[0] - _floorDb, -160f);
    }
}
