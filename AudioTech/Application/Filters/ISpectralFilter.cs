namespace AudioTech.Application.Filters;

/// <summary>
/// Marker extension for filters that operate in the frequency domain (after FFT).
/// The time-domain <see cref="INoiseFilter.Apply"/> is a no-op for spectral filters.
/// </summary>
public interface ISpectralFilter : INoiseFilter
{
    /// <summary>
    /// Modifies <paramref name="magnitudeDb"/> in-place by subtracting the estimated
    /// noise floor. Called directly after FFT magnitude computation.
    /// </summary>
    void ApplySpectral(float[] magnitudeDb, int sampleRate, int fftSize);
}
