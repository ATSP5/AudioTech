using AudioTech.Domain.Common;

namespace AudioTech.Domain.Events;

public sealed record AudioAnalysisCompletedEvent(Guid AudioFileId, TimeSpan Duration) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
