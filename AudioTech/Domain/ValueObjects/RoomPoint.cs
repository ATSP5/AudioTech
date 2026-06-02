namespace AudioTech.Domain.ValueObjects;

public sealed record RoomPoint(double X, double Y)
{
    public double DistanceTo(RoomPoint other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // Mirror this point across the infinite line passing through lineA and lineB.
    public RoomPoint MirrorAcrossLine(RoomPoint lineA, RoomPoint lineB)
    {
        var dx = lineB.X - lineA.X;
        var dy = lineB.Y - lineA.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-12) return this;
        var t = ((X - lineA.X) * dx + (Y - lineA.Y) * dy) / lenSq;
        var footX = lineA.X + t * dx;
        var footY = lineA.Y + t * dy;
        return new RoomPoint(2 * footX - X, 2 * footY - Y);
    }
}
