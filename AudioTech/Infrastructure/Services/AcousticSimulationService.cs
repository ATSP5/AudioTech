using AudioTech.Application.Queries.ComputeSoundDistribution;
using AudioTech.Application.Services;
using AudioTech.Domain.Entities;
using AudioTech.Domain.Enums;
using AudioTech.Domain.ValueObjects;

namespace AudioTech.Infrastructure.Services;

/// <summary>
/// Estimates steady-state SPL distribution using the 1st-order image-source method
/// combined with inverse-square-law direct field.
/// </summary>
public sealed class AcousticSimulationService : IAcousticSimulationService
{

    public ComputeSoundDistributionResult Compute(
        IReadOnlyList<RoomPoint> roomPolygon,
        IReadOnlyList<RoomObstacle> obstacles,
        SoundSourceNode soundSource,
        SurfaceType wallSurface,
        double wallIrregularity,
        int gridResolution = 80)
    {
        if (roomPolygon.Count < 3)
            throw new ArgumentException("Room polygon must have at least 3 vertices.");

        // ── Bounding box ────────────────────────────────────────────────────────
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in roomPolygon)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        double roomW = maxX - minX;
        double roomH = maxY - minY;
        if (roomW < 0.1) roomW = 0.1;
        if (roomH < 0.1) roomH = 0.1;

        // Proportional grid dimensions.
        int gridW = gridResolution;
        int gridH = Math.Max(4, (int)Math.Round(gridResolution * roomH / roomW));

        var grid = new float[gridW, gridH];

        // ── Wall segments ───────────────────────────────────────────────────────
        var walls = CollectWallSegments(roomPolygon, wallSurface, wallIrregularity);
        foreach (var obs in obstacles)
            walls.AddRange(CollectWallSegments(obs.Polygon, obs.Surface, obs.Irregularity));

        double L0  = soundSource.SourceLevel;
        var    src = soundSource.Position;

        var rng = new Random(42); // fixed seed → deterministic per input

        float minSpl = float.MaxValue;
        float maxSpl = float.MinValue;

        for (int gx = 0; gx < gridW; gx++)
        {
            double rx = minX + (gx + 0.5) * roomW / gridW;

            for (int gy = 0; gy < gridH; gy++)
            {
                double ry = minY + (gy + 0.5) * roomH / gridH;
                var receiver = new RoomPoint(rx, ry);

                // Skip points outside the room or inside obstacles.
                if (!IsInsidePolygon(receiver, roomPolygon) ||
                    IsInsideAnyObstacle(receiver, obstacles))
                {
                    grid[gx, gy] = float.NaN;
                    continue;
                }

                // Direct field (inverse-square law, no occlusion check for simplicity).
                double dDirect = Math.Max(src.DistanceTo(receiver), 0.1);
                double pLinear = DbToLinear(L0 - 20.0 * Math.Log10(dDirect));

                // 1st-order reflections from each wall segment.
                foreach (var wall in walls)
                {
                    var imgSrc = src.MirrorAcrossLine(wall.P1, wall.P2);

                    // Valid reflection: the line img→receiver must cross the wall segment.
                    if (!SegmentsIntersect(imgSrc, receiver, wall.P1, wall.P2))
                        continue;

                    double dRef = Math.Max(imgSrc.DistanceTo(receiver), 0.1);

                    // Perturbation for rough surfaces.
                    double alpha = wall.Absorption;
                    if (wall.Irregularity > 0)
                    {
                        double jitter = (rng.NextDouble() * 2 - 1) * wall.Irregularity * 0.25;
                        alpha = Math.Clamp(alpha + jitter, 0.01, 0.99);
                    }

                    // Reflected SPL = direct_spl_at_image_distance + pressure_reflection_loss.
                    double splRef = L0 - 20.0 * Math.Log10(dRef) + 10.0 * Math.Log10(1.0 - alpha);
                    pLinear += DbToLinear(splRef);
                }

                float spl = (float)(10.0 * Math.Log10(Math.Max(pLinear, 1e-30)));
                grid[gx, gy] = spl;

                if (spl < minSpl) minSpl = spl;
                if (spl > maxSpl) maxSpl = spl;
            }
        }

        return new ComputeSoundDistributionResult(grid, minX, minY, maxX, maxY, minSpl, maxSpl);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static double DbToLinear(double db) => Math.Pow(10.0, db / 10.0);

    private static List<WallSeg> CollectWallSegments(
        IReadOnlyList<RoomPoint> polygon,
        SurfaceType surface,
        double irregularity)
    {
        double alpha = surface switch
        {
            SurfaceType.Hard  => 0.05,
            SurfaceType.Mixed => 0.30,
            SurfaceType.Soft  => 0.80,
            _                 => 0.05
        };

        var list = new List<WallSeg>(polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            var p1 = polygon[i];
            var p2 = polygon[(i + 1) % polygon.Count];
            list.Add(new WallSeg(p1, p2, alpha, irregularity));
        }
        return list;
    }

    /// Ray-casting point-in-polygon test.
    private static bool IsInsidePolygon(RoomPoint pt, IReadOnlyList<RoomPoint> poly)
    {
        bool inside = false;
        int  j      = poly.Count - 1;
        for (int i = 0; i < poly.Count; i++)
        {
            var pi = poly[i];
            var pj = poly[j];
            if ((pi.Y > pt.Y) != (pj.Y > pt.Y) &&
                pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
                inside = !inside;
            j = i;
        }
        return inside;
    }

    private static bool IsInsideAnyObstacle(RoomPoint pt, IReadOnlyList<RoomObstacle> obstacles)
    {
        foreach (var obs in obstacles)
            if (IsInsidePolygon(pt, obs.Polygon)) return true;
        return false;
    }

    /// Returns true if segment P1–P2 intersects segment P3–P4.
    private static bool SegmentsIntersect(
        RoomPoint p1, RoomPoint p2,
        RoomPoint p3, RoomPoint p4)
    {
        double d1x = p2.X - p1.X, d1y = p2.Y - p1.Y;
        double d2x = p4.X - p3.X, d2y = p4.Y - p3.Y;
        double denom = d1x * d2y - d1y * d2x;
        if (Math.Abs(denom) < 1e-12) return false;
        double dx = p3.X - p1.X, dy = p3.Y - p1.Y;
        double t = (dx * d2y - dy * d2x) / denom;
        double u = (dx * d1y - dy * d1x) / denom;
        return t > 0 && t < 1 && u > 0 && u < 1;
    }

    private sealed record WallSeg(RoomPoint P1, RoomPoint P2, double Absorption, double Irregularity);
}
