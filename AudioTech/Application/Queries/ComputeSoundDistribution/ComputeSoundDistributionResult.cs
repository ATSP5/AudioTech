namespace AudioTech.Application.Queries.ComputeSoundDistribution;

/// <param name="SplGrid">SPL values in dB; NaN means point is outside the room or inside an obstacle. Indexed [x, y] with x=0 at MinX, y=0 at MinY.</param>
public sealed record ComputeSoundDistributionResult(
    float[,] SplGrid,
    double   MinX,
    double   MinY,
    double   MaxX,
    double   MaxY,
    float    MinSpl,
    float    MaxSpl);
