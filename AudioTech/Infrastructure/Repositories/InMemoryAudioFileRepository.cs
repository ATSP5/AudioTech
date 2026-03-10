using System.Collections.Concurrent;

using AudioTech.Domain.Entities;
using AudioTech.Domain.Repositories;

namespace AudioTech.Infrastructure.Repositories;

public sealed class InMemoryAudioFileRepository : IAudioFileRepository
{
    private readonly ConcurrentDictionary<Guid, AudioFile> _store = new();

    public Task<AudioFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(id, out var audioFile);
        return Task.FromResult(audioFile);
    }

    public Task<IReadOnlyList<AudioFile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AudioFile> result = _store.Values.ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    public Task AddAsync(AudioFile audioFile, CancellationToken cancellationToken = default)
    {
        _store[audioFile.Id] = audioFile;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AudioFile audioFile, CancellationToken cancellationToken = default)
    {
        _store[audioFile.Id] = audioFile;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
