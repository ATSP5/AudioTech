using AudioTech.Application.Queries.ComputeSoundDistribution;
using AudioTech.Domain.Entities;
using AudioTech.Domain.Enums;
using AudioTech.Domain.ValueObjects;

namespace AudioTech.Application.Services;

public interface IAcousticSimulationService
{
    ComputeSoundDistributionResult Compute(
        IReadOnlyList<RoomPoint> roomPolygon,
        IReadOnlyList<RoomObstacle> obstacles,
        SoundSourceNode soundSource,
        SurfaceType wallSurface,
        double wallIrregularity,
        int gridResolution = 80);
}
