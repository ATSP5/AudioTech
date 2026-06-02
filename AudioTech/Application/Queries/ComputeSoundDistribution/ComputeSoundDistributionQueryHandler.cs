using AudioTech.Application.Abstractions;
using AudioTech.Application.Services;

namespace AudioTech.Application.Queries.ComputeSoundDistribution;

public sealed class ComputeSoundDistributionQueryHandler
    : IQueryHandler<ComputeSoundDistributionQuery, ComputeSoundDistributionResult>
{
    private readonly IAcousticSimulationService _simulation;

    public ComputeSoundDistributionQueryHandler(IAcousticSimulationService simulation) =>
        _simulation = simulation;

    public Task<ComputeSoundDistributionResult> HandleAsync(
        ComputeSoundDistributionQuery query, CancellationToken ct) =>
        Task.FromResult(_simulation.Compute(
            query.RoomPolygon,
            query.Obstacles,
            query.SoundSource,
            query.WallSurface,
            query.WallIrregularity,
            query.GridResolution));
}
