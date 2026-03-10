using AudioTech.Domain.Entities;

namespace AudioTech.Domain.Repositories;

public interface IAudioFileRepository
{
    Task<AudioFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AudioFile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(AudioFile audioFile, CancellationToken cancellationToken = default);
    Task UpdateAsync(AudioFile audioFile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
