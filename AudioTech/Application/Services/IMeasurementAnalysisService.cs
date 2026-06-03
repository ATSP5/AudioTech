using AudioTech.Domain.Enums;
using AudioTech.Domain.ValueObjects;

namespace AudioTech.Application.Services;

public sealed record MicrophoneMeasurementRequest(
    Guid MicId,
    MicrophoneSourceType SourceType,
    string? FilePath,
    int DeviceIndex = -1);

public sealed record MicrophoneMeasurementResult(
    Guid MicId,
    bool Success,
    float SplDb,           // Broadband RMS SPL relative to full scale
    float DominantFreqHz,  // Frequency bin with highest energy
    double TimeOffsetMs,   // Cross-correlation lag vs first file (file sources only)
    string? ErrorMessage = null);

public interface IMeasurementAnalysisService
{
    /// <summary>
    /// Analyse each microphone source (file or live device) and return per-mic SPL measurements.
    /// File sources are time-aligned via cross-correlation.
    /// </summary>
    Task<IReadOnlyList<MicrophoneMeasurementResult>> AnalyzeAsync(
        IReadOnlyList<MicrophoneMeasurementRequest> requests,
        CancellationToken ct);
}
