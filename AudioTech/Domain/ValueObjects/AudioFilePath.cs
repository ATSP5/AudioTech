using AudioTech.Domain.Common;

namespace AudioTech.Domain.ValueObjects;

public sealed class AudioFilePath : ValueObject
{
    public string Value { get; }

    private static readonly HashSet<string> SupportedExtensions = [".wav", ".mp3", ".flac", ".ogg", ".aac", ".m4a"];

    private AudioFilePath(string value) => Value = value;

    public static AudioFilePath Create(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Audio file path cannot be empty.", nameof(path));

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
            throw new ArgumentException($"Unsupported audio format '{extension}'. Supported: {string.Join(", ", SupportedExtensions)}", nameof(path));

        return new AudioFilePath(path);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value.ToLowerInvariant();
    }

    public override string ToString() => Value;
}
