using AudioTech.Domain.Common;
using AudioTech.Domain.Enums;
using AudioTech.Domain.ValueObjects;

namespace AudioTech.Domain.Entities;

public sealed class RoomObstacle : Entity
{
    private List<RoomPoint> _polygon = [];

    public IReadOnlyList<RoomPoint> Polygon    => _polygon.AsReadOnly();
    public SurfaceType              Surface     { get; private set; }
    public double                   Irregularity { get; private set; }
    public string                   Label       { get; private set; } = string.Empty;

    private RoomObstacle() { }

    public static RoomObstacle Create(
        IReadOnlyList<RoomPoint> polygon,
        SurfaceType surface,
        double irregularity = 0.0,
        string label = "Obstacle")
    {
        if (polygon.Count < 3)
            throw new ArgumentException("Obstacle requires at least 3 points.", nameof(polygon));

        return new RoomObstacle
        {
            _polygon     = [.. polygon],
            Surface      = surface,
            Irregularity = Math.Clamp(irregularity, 0.0, 1.0),
            Label        = label
        };
    }

    public void UpdateProperties(SurfaceType surface, double irregularity)
    {
        Surface      = surface;
        Irregularity = Math.Clamp(irregularity, 0.0, 1.0);
    }
}
