using AudioTech.Domain.Common;
using AudioTech.Domain.ValueObjects;

namespace AudioTech.Domain.Entities;

public sealed class SoundSourceNode : Entity
{
    public RoomPoint Position    { get; private set; } = new(0, 0);
    public double    SourceLevel { get; private set; } = 94.0;   // dB SPL at 1 m
    public string    Label       { get; private set; } = string.Empty;

    private SoundSourceNode() { }

    public static SoundSourceNode Create(RoomPoint position, double sourceLevel = 94.0, string label = "Stage") =>
        new() { Position = position, SourceLevel = sourceLevel, Label = label };

    public void MoveTo(RoomPoint position)         => Position = position;
    public void SetSourceLevel(double level)       => SourceLevel = Math.Clamp(level, 60.0, 140.0);
}
