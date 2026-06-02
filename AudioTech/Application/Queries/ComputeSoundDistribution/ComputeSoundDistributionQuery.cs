using AudioTech.Application.Abstractions;
using AudioTech.Domain.Entities;
using AudioTech.Domain.Enums;
using AudioTech.Domain.ValueObjects;

namespace AudioTech.Application.Queries.ComputeSoundDistribution;

public sealed record ComputeSoundDistributionQuery(
    IReadOnlyList<RoomPoint>    RoomPolygon,
    IReadOnlyList<RoomObstacle> Obstacles,
    SoundSourceNode             SoundSource,
    SurfaceType                 WallSurface,
    double                      WallIrregularity,
    int                         GridResolution = 80
) : IQuery<ComputeSoundDistributionResult>;
