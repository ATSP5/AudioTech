using AudioTech.Domain.Common;
using AudioTech.Domain.ValueObjects;

namespace AudioTech.Domain.Entities;

public sealed class MicrophoneNode : Entity
{
    public RoomPoint Position        { get; private set; } = new(0, 0);
    public string?   AssignedFilePath { get; private set; }
    public string    Label           { get; private set; } = string.Empty;

    private MicrophoneNode() { }

    public static MicrophoneNode Create(RoomPoint position, string label = "Mic") =>
        new() { Position = position, Label = label };

    public void MoveTo(RoomPoint position)     => Position = position;
    public void AssignFile(string? path)       => AssignedFilePath = path;
    public void Rename(string label)           => Label = label;
}
