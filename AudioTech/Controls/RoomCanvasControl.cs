using System.Globalization;

using AudioTech.Domain.Entities;
using AudioTech.Domain.ValueObjects;
using AudioTech.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AudioTech.Controls;

/// <summary>
/// FreeCAD-style interactive 2-D canvas for room acoustics drawing.
/// Obtains its ViewModel from the inherited DataContext.
/// </summary>
public sealed class RoomCanvasControl : Control
{
    // ── Static ctor ───────────────────────────────────────────────────────────
    static RoomCanvasControl()
    {
        FocusableProperty.OverrideDefaultValue<RoomCanvasControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<RoomCanvasControl>(true);
    }

    // ── ViewModel via DataContext ──────────────────────────────────────────────
    private RoomAcousticsViewModel? _vm;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
            _vm.DrawingChanged -= OnDrawingChanged;

        _vm = DataContext as RoomAcousticsViewModel;

        if (_vm is not null)
            _vm.DrawingChanged += OnDrawingChanged;

        InvalidateVisual();
    }

    private void OnDrawingChanged(object? sender, EventArgs e) => InvalidateVisual();

    // ── Theme ─────────────────────────────────────────────────────────────────

    private static readonly IBrush BgBrush       = new SolidColorBrush(Color.FromRgb(0x18, 0x20, 0x2E));
    private static readonly IBrush MinorGrid      = new SolidColorBrush(Color.FromArgb(0x55, 0x50, 0x66, 0x88));
    private static readonly IBrush MajorGrid      = new SolidColorBrush(Color.FromArgb(0x99, 0x60, 0x88, 0xBB));
    private static readonly IBrush AxisBrush      = new SolidColorBrush(Color.FromArgb(0xCC, 0x80, 0xA8, 0xD8));
    private static readonly IBrush GridLabelColor = new SolidColorBrush(Color.FromRgb(0x50, 0x70, 0x90));

    private static readonly IBrush RoomFill    = new SolidColorBrush(Color.FromArgb(0x55, 0x30, 0x70, 0xCC));
    private static readonly IPen   RoomPen     = new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0xA8, 0xFF)), 2.0);
    private static readonly IPen   DrawPen     = new Pen(new SolidColorBrush(Color.FromArgb(0xDD, 0x90, 0xC8, 0xFF)), 1.5,
                                                     new DashStyle([6, 4], 0));

    private static readonly IBrush ObsFill     = new SolidColorBrush(Color.FromArgb(0x66, 0xDD, 0x99, 0x10));
    private static readonly IPen   ObsPen      = new Pen(new SolidColorBrush(Color.FromRgb(0xDD, 0xAA, 0x22)), 1.5);
    private static readonly IBrush ObsLabel    = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44));

    private static readonly IBrush MicFill     = new SolidColorBrush(Color.FromRgb(0x20, 0xCC, 0x60));
    private static readonly IPen   MicPen      = new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0xFF, 0x88)), 1.5);
    private static readonly IBrush MicLabel    = new SolidColorBrush(Color.FromRgb(0x66, 0xFF, 0xAA));

    private static readonly IBrush StageFill   = new SolidColorBrush(Color.FromRgb(0xDD, 0x40, 0x40));
    private static readonly IPen   StagePen    = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x70)), 1.5);
    private static readonly IBrush StageLabel  = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0xAA));

    private static readonly IBrush DimLabel    = new SolidColorBrush(Color.FromRgb(0x80, 0xA8, 0xCC));
    private static readonly IPen   DimPen      = new Pen(new SolidColorBrush(Color.FromArgb(0x88, 0x80, 0xA8, 0xCC)), 0.75);

    private static readonly IBrush SnapDot     = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0x80));
    private static readonly IPen   Crosshair   = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1.0);

    private static readonly IBrush HintBrush   = new SolidColorBrush(Color.FromRgb(0x40, 0x60, 0x80));
    private static readonly Typeface SmFont    = new(FontFamily.Default);

    // ── Input ─────────────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_vm is null) return;

        var pt   = e.GetPosition(this);
        var off  = ToOffset(pt);
        var prop = e.GetCurrentPoint(this).Properties;

        _vm.OnPointerDown(off.X, off.Y,
            isLeft:   prop.IsLeftButtonPressed,
            isRight:  prop.IsRightButtonPressed,
            isMiddle: prop.IsMiddleButtonPressed);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm is null) return;

        var off = ToOffset(e.GetPosition(this));
        _vm.OnPointerMove(off.X, off.Y,
            leftHeld: e.GetCurrentPoint(this).Properties.IsLeftButtonPressed);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_vm is null) return;

        _vm.OnPointerUp(e.GetCurrentPoint(this).Properties.PointerUpdateKind
                        == PointerUpdateKind.MiddleButtonReleased);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm is null) return;

        var off = ToOffset(e.GetPosition(this));
        _vm.OnWheel(e.Delta.Y, off.X, off.Y);
        e.Handled = true;
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    private Point ToOffset(Point canvas) =>
        new(canvas.X - Bounds.Width / 2, canvas.Y - Bounds.Height / 2);

    private Point OffsetToCanvas(Point offset) =>
        new(offset.X + Bounds.Width / 2, offset.Y + Bounds.Height / 2);

    private Point RoomToCanvas(RoomPoint room)
    {
        if (_vm is null) return default;
        var off = _vm.RoomToOffset(room);
        return OffsetToCanvas(off);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        ctx.DrawRectangle(BgBrush, null, new Rect(bounds.Size));

        if (_vm is null)
        {
            DrawHint(ctx, bounds, "No view model — navigation error.");
            return;
        }

        DrawGrid(ctx, bounds);
        DrawHeatmapLayer(ctx);
        DrawRoomPolygon(ctx);

        foreach (var obs in _vm.Obstacles)
            DrawObstacle(ctx, obs);

        DrawInProgress(ctx);

        // Microphones — look up measured SPL from MicConfigs in measure mode
        foreach (var mic in _vm.Microphones)
        {
            var cfg = _vm.IsMeasureMode
                ? _vm.MicConfigs.FirstOrDefault(c => c.Node.Id == mic.Id)
                : null;
            DrawMic(ctx, mic, cfg);
        }

        // Stage only shown in simulation mode
        if (_vm.IsSimulationMode && _vm.SoundSource is { } src)
            DrawStage(ctx, src);

        if (_vm.RoomPolygon.Count >= 3)
            DrawDimensions(ctx);

        DrawCrosshair(ctx, bounds);
        DrawSnapDot(ctx);
        DrawModeLabel(ctx, bounds);

        if (_vm.RoomPolygon.Count == 0 && _vm.DrawingPoints.Count == 0)
            DrawHint(ctx, bounds,
                "Select  Draw Room  tool, then click to place vertices.\n" +
                "Click near the first point to close the shape.\n" +
                "Scroll to zoom  ·  Middle-drag to pan");
    }

    // ── Background hint ───────────────────────────────────────────────────────

    private static void DrawHint(DrawingContext ctx, Rect bounds, string text)
    {
        var lines = text.Split('\n');
        double totalH = lines.Length * 18;
        double startY = (bounds.Height - totalH) / 2;
        foreach (var line in lines)
        {
            var ft = new FormattedText(line, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, SmFont, 13, HintBrush);
            ctx.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, startY));
            startY += 20;
        }
    }

    // ── Grid ──────────────────────────────────────────────────────────────────

    private void DrawGrid(DrawingContext ctx, Rect bounds)
    {
        if (_vm is null) return;
        double scale  = RoomAcousticsViewModel.BaseScale * _vm.Zoom;
        double w = bounds.Width, h = bounds.Height;
        double ox = w / 2 + _vm.PanX;
        double oy = h / 2 + _vm.PanY;

        double rL = (-ox) / scale;
        double rR = (w - ox) / scale;
        double rT = (-oy) / scale;
        double rB = (h - oy) / scale;

        const double minor = 1.0;
        const double major = 5.0;

        for (double x = Math.Floor(rL / minor) * minor; x <= rR + minor; x += minor)
        {
            double cx = ox + x * scale;
            bool isMaj  = Math.Abs(x % major) < 0.01 || Math.Abs(Math.Abs(x % major) - major) < 0.01;
            bool isAxis = Math.Abs(x) < 0.01;
            var brush = isAxis ? AxisBrush : isMaj ? MajorGrid : MinorGrid;
            ctx.DrawLine(new Pen(brush, isAxis ? 1.2 : 0.7),
                new Point(cx, 0), new Point(cx, h));

            if (isMaj && !isAxis && scale > 15)
            {
                var ft = new FormattedText($"{(int)Math.Round(x)}m",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SmFont, 9, GridLabelColor);
                ctx.DrawText(ft, new Point(cx + 2, h - 14));
            }
        }

        for (double y = Math.Floor(rT / minor) * minor; y <= rB + minor; y += minor)
        {
            double cy = oy + y * scale;
            bool isMaj  = Math.Abs(y % major) < 0.01 || Math.Abs(Math.Abs(y % major) - major) < 0.01;
            bool isAxis = Math.Abs(y) < 0.01;
            var brush = isAxis ? AxisBrush : isMaj ? MajorGrid : MinorGrid;
            ctx.DrawLine(new Pen(brush, isAxis ? 1.2 : 0.7),
                new Point(0, cy), new Point(w, cy));

            if (isMaj && !isAxis && scale > 15)
            {
                var ft = new FormattedText($"{(int)Math.Round(y)}m",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SmFont, 9, GridLabelColor);
                ctx.DrawText(ft, new Point(3, cy - 11));
            }
        }
    }

    // ── Heatmap ───────────────────────────────────────────────────────────────

    private void DrawHeatmapLayer(DrawingContext ctx)
    {
        if (_vm?.HeatmapBitmap is not { } bmp) return;
        var res = _vm.HeatmapResult!;

        var tl = RoomToCanvas(new RoomPoint(res.MinX, res.MinY));
        var br = RoomToCanvas(new RoomPoint(res.MaxX, res.MaxY));
        var dest = new Rect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
        ctx.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), dest);

        DrawLegend(ctx);
    }

    private void DrawLegend(DrawingContext ctx)
    {
        var res = _vm?.HeatmapResult;
        if (res is null) return;

        double lx = Bounds.Width - 40, ly = 12, lw = 14, lh = 90;

        for (int i = 0; i < (int)lh; i++)
        {
            float t = 1f - i / (float)lh;
            LegendColor(t, out byte r, out byte g, out byte b);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(r, g, b)), null,
                new Rect(lx, ly + i, lw, 1));
        }
        ctx.DrawRectangle(null, DimPen, new Rect(lx, ly, lw, lh));

        var maxFt = Fmt($"{res.MaxSpl:F0}", 9, DimLabel);
        var minFt = Fmt($"{res.MinSpl:F0}", 9, DimLabel);
        ctx.DrawText(maxFt, new Point(lx - maxFt.Width - 2, ly));
        ctx.DrawText(minFt, new Point(lx - minFt.Width - 2, ly + lh - 10));
        ctx.DrawText(Fmt("dB", 9, DimLabel), new Point(lx, ly + lh + 2));
    }

    private static void LegendColor(float t, out byte r, out byte g, out byte b)
    {
        if (t < 0.25f)      { float s = t / 0.25f; r = 0; g = (byte)(s * 180); b = (byte)(120 + s * 135); }
        else if (t < 0.5f)  { float s = (t - 0.25f) / 0.25f; r = 0; g = (byte)(180 + s * 75); b = (byte)(255 * (1f - s)); }
        else if (t < 0.75f) { float s = (t - 0.5f) / 0.25f; r = (byte)(s * 255); g = 255; b = 0; }
        else                { float s = (t - 0.75f) / 0.25f; r = 255; g = (byte)(255 * (1f - s)); b = 0; }
    }

    // ── Room polygon ──────────────────────────────────────────────────────────

    private void DrawRoomPolygon(DrawingContext ctx)
    {
        var pts = _vm!.RoomPolygon;
        if (pts.Count < 2) return;
        var geo = BuildPolygon(pts, pts.Count >= 3);
        ctx.DrawGeometry(pts.Count >= 3 ? RoomFill : null, RoomPen, geo);
    }

    // ── Obstacles ─────────────────────────────────────────────────────────────

    private void DrawObstacle(DrawingContext ctx, RoomObstacle obs)
    {
        if (obs.Polygon.Count < 3) return;
        ctx.DrawGeometry(ObsFill, ObsPen, BuildPolygon(obs.Polygon, true));

        double cx = obs.Polygon.Average(p => p.X);
        double cy = obs.Polygon.Average(p => p.Y);
        var cpt = RoomToCanvas(new RoomPoint(cx, cy));
        var ft = Fmt(obs.Label, 10, ObsLabel);
        ctx.DrawText(ft, new Point(cpt.X - ft.Width / 2, cpt.Y - ft.Height / 2));
    }

    // ── In-progress drawing ───────────────────────────────────────────────────

    private void DrawInProgress(DrawingContext ctx)
    {
        var vm = _vm!;
        var pts = vm.DrawingPoints;
        var mouse = vm.MouseRoomPos;

        if (pts.Count > 0)
        {
            for (int i = 0; i < pts.Count - 1; i++)
                ctx.DrawLine(DrawPen, RoomToCanvas(pts[i]), RoomToCanvas(pts[i + 1]));

            if (mouse is { } m)
            {
                ctx.DrawLine(DrawPen, RoomToCanvas(pts[^1]), RoomToCanvas(m));

                if (pts.Count >= 3)
                    ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0xC0, 0xFF)), 1,
                                        new DashStyle([3, 5], 0)),
                        RoomToCanvas(m), RoomToCanvas(pts[0]));
            }

            foreach (var p in pts)
                ctx.DrawEllipse(DrawPen.Brush, null, RoomToCanvas(p), 3.5, 3.5);

            if (pts.Count >= 3)
            {
                double snapM = 0.75 * RoomAcousticsViewModel.BaseScale * vm.Zoom;
                ctx.DrawEllipse(null,
                    new Pen(new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0x00)), 1.5),
                    RoomToCanvas(pts[0]), snapM, snapM);
            }
        }

        // Obstacle rect preview.
        if (vm.CurrentTool == DrawingTool.DrawObstacle && pts.Count == 1 && mouse is { } mObs)
        {
            var snap2 = new RoomPoint(Math.Round(mObs.X / 0.5) * 0.5, Math.Round(mObs.Y / 0.5) * 0.5);
            var c1 = RoomToCanvas(pts[0]);
            var c2 = RoomToCanvas(snap2);
            ctx.DrawRectangle(ObsFill, ObsPen,
                new Rect(Math.Min(c1.X, c2.X), Math.Min(c1.Y, c2.Y),
                         Math.Abs(c2.X - c1.X), Math.Abs(c2.Y - c1.Y)));
        }
    }

    // ── Microphone ────────────────────────────────────────────────────────────

    private void DrawMic(DrawingContext ctx, MicrophoneNode mic, MicrophoneConfigViewModel? cfg = null)
    {
        var c = RoomToCanvas(mic.Position);
        const double r = 7;

        // Highlight mics that have a measurement result
        bool hasSpl = cfg?.MeasuredSpl is not null;
        var fillBrush = hasSpl ? new SolidColorBrush(Color.FromRgb(0x20, 0xFF, 0x80)) : MicFill;
        var penBrush  = hasSpl ? new SolidColorBrush(Color.FromRgb(0x80, 0xFF, 0xCC)) : MicPen.Brush!;

        ctx.DrawEllipse(fillBrush, new Pen(penBrush, 1.5), c, r, r);
        ctx.DrawLine(new Pen(penBrush, 1), new Point(c.X - r + 2, c.Y), new Point(c.X + r - 2, c.Y));
        ctx.DrawLine(new Pen(penBrush, 1), new Point(c.X, c.Y - r + 2), new Point(c.X, c.Y + r - 2));

        ctx.DrawText(Fmt(mic.Label, 10, MicLabel), new Point(c.X + r + 3, c.Y - 6));

        // In measure mode, show source indicator and measured SPL
        if (cfg is not null)
        {
            string srcIcon = cfg.UseFile ? "F" : "L";
            ctx.DrawText(Fmt(srcIcon, 8, new SolidColorBrush(Color.FromRgb(0xAA, 0xCC, 0xFF))),
                         new Point(c.X - 3, c.Y - r - 12));

            if (cfg.MeasuredSpl is { } spl)
            {
                var splFt = Fmt($"{spl:F1} dBFS", 9,
                    cfg.HasError
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x80))
                        : new SolidColorBrush(Color.FromRgb(0xCC, 0xFF, 0xAA)));
                ctx.DrawText(splFt, new Point(c.X + r + 3, c.Y + 4));
            }

            if (cfg.TimeOffsetDisplay is { Length: > 0 } td)
            {
                ctx.DrawText(Fmt(td, 8, new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0x80))),
                             new Point(c.X + r + 3, c.Y + 15));
            }
        }
    }

    // ── Mode label (top-left badge) ───────────────────────────────────────────

    private static readonly IBrush SimModeBg  = new SolidColorBrush(Color.FromArgb(0xCC, 0x18, 0x3A, 0x60));
    private static readonly IBrush MeasModeBg = new SolidColorBrush(Color.FromArgb(0xCC, 0x18, 0x50, 0x30));
    private static readonly IBrush ModeLabelFg = new SolidColorBrush(Color.FromRgb(0xCC, 0xEE, 0xFF));

    private void DrawModeLabel(DrawingContext ctx, Rect bounds)
    {
        if (_vm is null) return;
        string label = _vm.IsSimulationMode ? "SIMULATION" : "MEASURE";
        var brush    = _vm.IsSimulationMode ? SimModeBg   : MeasModeBg;

        var ft = Fmt(label, 10, ModeLabelFg);
        double px = 6, py = 6;
        ctx.DrawRectangle(brush, null, new Rect(px - 4, py - 2, ft.Width + 8, ft.Height + 4),
                          3, 3);
        ctx.DrawText(ft, new Point(px, py));
    }

    // ── Stage ─────────────────────────────────────────────────────────────────

    private void DrawStage(DrawingContext ctx, SoundSourceNode src)
    {
        var c = RoomToCanvas(src.Position);
        const double r = 11;
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(new Point(c.X, c.Y - r), true);
            gc.LineTo(new Point(c.X + r * 0.866, c.Y + r * 0.5));
            gc.LineTo(new Point(c.X - r * 0.866, c.Y + r * 0.5));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(StageFill, StagePen, geo);
        ctx.DrawEllipse(StagePen.Brush, null, c, 2.5, 2.5);

        var ft = Fmt($"{src.Label}  {src.SourceLevel:F0} dB", 10, StageLabel);
        ctx.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y + r + 4));
    }

    // ── Dimensions ───────────────────────────────────────────────────────────

    private void DrawDimensions(DrawingContext ctx)
    {
        var pts = _vm!.RoomPolygon;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % pts.Count];
            double length = a.DistanceTo(b);
            if (length < 0.1) continue;

            var ca = RoomToCanvas(a); var cb = RoomToCanvas(b);
            double dx = cb.X - ca.X, dy = cb.Y - ca.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 30) continue;

            double nx = -dy / len * 15, ny = dx / len * 15;
            var ma = new Point(ca.X + nx, ca.Y + ny);
            var mb = new Point(cb.X + nx, cb.Y + ny);
            var mid = new Point((ma.X + mb.X) / 2, (ma.Y + mb.Y) / 2);

            ctx.DrawLine(DimPen, ma, mb);
            ctx.DrawLine(DimPen, ca, ma);
            ctx.DrawLine(DimPen, cb, mb);

            var ft = Fmt($"{length:F1} m", 10, DimLabel);
            ctx.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height / 2));
        }
    }

    // ── Crosshair + snap ─────────────────────────────────────────────────────

    private void DrawCrosshair(DrawingContext ctx, Rect bounds)
    {
        if (_vm?.MouseRoomPos is not { } m) return;
        var c = RoomToCanvas(m);
        ctx.DrawLine(Crosshair, new Point(c.X, 0), new Point(c.X, bounds.Height));
        ctx.DrawLine(Crosshair, new Point(0, c.Y), new Point(bounds.Width, c.Y));
    }

    private void DrawSnapDot(DrawingContext ctx)
    {
        if (_vm?.MouseRoomPos is not { } m) return;
        var snap = new RoomPoint(Math.Round(m.X / 0.5) * 0.5, Math.Round(m.Y / 0.5) * 0.5);
        ctx.DrawEllipse(SnapDot, null, RoomToCanvas(snap), 3.5, 3.5);
    }

    // ── Geometry helper ───────────────────────────────────────────────────────

    private StreamGeometry BuildPolygon(IReadOnlyList<RoomPoint> pts, bool close)
    {
        var geo = new StreamGeometry();
        using var gc = geo.Open();
        gc.BeginFigure(RoomToCanvas(pts[0]), true);
        for (int i = 1; i < pts.Count; i++) gc.LineTo(RoomToCanvas(pts[i]));
        gc.EndFigure(close);
        return geo;
    }

    private static FormattedText Fmt(string text, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SmFont, size, brush);
}
