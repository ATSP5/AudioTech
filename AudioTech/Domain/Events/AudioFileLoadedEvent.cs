using AudioTech.Domain.Common;

namespace AudioTech.Domain.Events;

public sealed record AudioFileLoadedEvent(Guid AudioFileId, string FilePath) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
