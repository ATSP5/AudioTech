using AudioTech.Domain.Common;
using AudioTech.Domain.Events;
using AudioTech.Domain.ValueObjects;

namespace AudioTech.Domain.Entities;

public sealed class AudioFile : AggregateRoot
{
    public AudioFilePath FilePath { get; private set; }
    public string FileName => Path.GetFileName(FilePath.Value);
    public TimeSpan? Duration { get; private set; }
    public int? SampleRate { get; private set; }
    public int? Channels { get; private set; }
    public bool IsAnalysed { get; private set; }
    public DateTime LoadedAt { get; private init; }

    private AudioFile() { FilePath = null!; }

    public static AudioFile Load(string filePath)
    {
        var audioFile = new AudioFile
        {
            FilePath = AudioFilePath.Create(filePath),
            LoadedAt = DateTime.UtcNow
        };

        audioFile.RaiseDomainEvent(new AudioFileLoadedEvent(audioFile.Id, filePath));

        return audioFile;
    }

    public void SetAudioProperties(TimeSpan duration, int sampleRate, int channels)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentException("Duration must be positive.", nameof(duration));
        if (sampleRate <= 0)
            throw new ArgumentException("Sample rate must be positive.", nameof(sampleRate));
        if (channels <= 0)
            throw new ArgumentException("Channel count must be positive.", nameof(channels));

        Duration = duration;
        SampleRate = sampleRate;
        Channels = channels;
        IsAnalysed = true;

        RaiseDomainEvent(new AudioAnalysisCompletedEvent(Id, duration));
    }
}
