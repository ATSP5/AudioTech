using System.Collections.ObjectModel;
using System.Globalization;

using AudioTech.Application.Abstractions;
using AudioTech.Application.Queries.ComputeSoundDistribution;
using AudioTech.Application.Services;
using AudioTech.Domain.Entities;
using AudioTech.Domain.Enums;
using AudioTech.Domain.ValueObjects;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioTech.ViewModels;

public enum DrawingTool    { Select, DrawRoom, DrawObstacle, PlaceMicrophone, PlaceStage }
public enum AcousticsMode  { Simulation, Measure }

public partial class RoomAcousticsViewModel : ViewModelBase
{
    private readonly IQueryDispatcher            _queryDispatcher;
    private readonly IDialogService              _dialogService;
    private readonly IAudioCaptureService        _captureService;
    private readonly IMeasurementAnalysisService _measureService;

    // ── Canvas invalidation ───────────────────────────────────────────────────
    public event EventHandler? DrawingChanged;
    private void NotifyRedraw() => DrawingChanged?.Invoke(this, EventArgs.Empty);

    // ── Room geometry ─────────────────────────────────────────────────────────
    [ObservableProperty] private List<RoomPoint>    _roomPolygon  = [];
    [ObservableProperty] private List<RoomObstacle> _obstacles    = [];
    [ObservableProperty] private List<MicrophoneNode> _microphones = [];
    [ObservableProperty] private SoundSourceNode?   _soundSource;

    // ── Microphone configs (Measure Mode) ────────────────────────────────────
    public ObservableCollection<MicrophoneConfigViewModel> MicConfigs { get; } = [];

    // ── In-progress drawing ───────────────────────────────────────────────────
    [ObservableProperty] private List<RoomPoint> _drawingPoints = [];
    [ObservableProperty] private RoomPoint?      _mouseRoomPos;
    private RoomPoint? _obstacleFirstCorner;
    private bool       _isPanning;
    private double     _panDragStartX, _panDragStartY;
    private double     _panXAtDragStart, _panYAtDragStart;

    // ── Canvas transform ──────────────────────────────────────────────────────
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private double _panX = 0.0;
    [ObservableProperty] private double _panY = 0.0;

    public const double BaseScale = 50.0;

    // ── Mode ──────────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMeasureMode))]
    private bool _isSimulationMode = true;

    public bool IsMeasureMode => !IsSimulationMode;

    // ── Tools ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private DrawingTool _currentTool = DrawingTool.DrawRoom;
    [ObservableProperty] private bool _isSelectTool;
    [ObservableProperty] private bool _isDrawRoomTool = true;
    [ObservableProperty] private bool _isDrawObstacleTool;
    [ObservableProperty] private bool _isPlaceMicrophoneTool;
    [ObservableProperty] private bool _isPlaceStageTool;

    // ── Room wall + obstacle properties ───────────────────────────────────────
    [ObservableProperty] private SurfaceType _wallSurface      = SurfaceType.Hard;
    [ObservableProperty] private double      _wallIrregularity = 0.0;
    [ObservableProperty] private SurfaceType _obstacleSurface      = SurfaceType.Mixed;
    [ObservableProperty] private double      _obstacleIrregularity = 0.0;

    // ── Simulation ────────────────────────────────────────────────────────────
    [ObservableProperty] private double _sourceLevelDb  = 94.0;
    [ObservableProperty] private int    _gridResolution = 80;
    [ObservableProperty] private bool   _isComputing;

    // ── Measure Mode analysis ─────────────────────────────────────────────────
    [ObservableProperty] private bool _isAnalyzing;

    // ── Heatmap ───────────────────────────────────────────────────────────────
    [ObservableProperty] private ComputeSoundDistributionResult? _heatmapResult;
    [ObservableProperty] private WriteableBitmap?               _heatmapBitmap;

    // ── Status ────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _statusText    = "Select  Draw Room  tool, then click to draw.";
    [ObservableProperty] private string _cursorPosition = "0.00, 0.00 m";

    // ── Surface combo support ─────────────────────────────────────────────────
    public static IReadOnlyList<string> SurfaceNames { get; } =
        ["Hard (concrete)", "Mixed (wood)", "Soft (carpet)"];

    public int WallSurfaceIndex
    {
        get => (int)WallSurface;
        set { WallSurface = (SurfaceType)value; NotifyRedraw(); }
    }

    public int ObstacleSurfaceIndex
    {
        get => (int)ObstacleSurface;
        set { ObstacleSurface = (SurfaceType)value; }
    }

    // ── Constructor ───────────────────────────────────────────────────────────
    public RoomAcousticsViewModel(
        IQueryDispatcher            queryDispatcher,
        IDialogService              dialogService,
        IAudioCaptureService        captureService,
        IMeasurementAnalysisService measureService)
    {
        _queryDispatcher = queryDispatcher;
        _dialogService   = dialogService;
        _captureService  = captureService;
        _measureService  = measureService;
    }

    // ── Mode switch ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void SetSimulationMode()
    {
        IsSimulationMode = true;
        // Show the stage tool when switching to simulation
        StatusText = "Simulation mode: place sound source, then press Process.";
        HeatmapBitmap = null;
        HeatmapResult = null;
        NotifyRedraw();
    }

    [RelayCommand]
    private void SetMeasureMode()
    {
        IsSimulationMode = false;
        StatusText = "Measure mode: assign recordings or live mics, then press Analyze.";
        HeatmapBitmap = null;
        HeatmapResult = null;
        NotifyRedraw();
    }

    // ── Tool selection ────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectTool(string toolName)
    {
        CurrentTool           = Enum.Parse<DrawingTool>(toolName);
        IsSelectTool          = CurrentTool == DrawingTool.Select;
        IsDrawRoomTool        = CurrentTool == DrawingTool.DrawRoom;
        IsDrawObstacleTool    = CurrentTool == DrawingTool.DrawObstacle;
        IsPlaceMicrophoneTool = CurrentTool == DrawingTool.PlaceMicrophone;
        IsPlaceStageTool      = CurrentTool == DrawingTool.PlaceStage;
        DrawingPoints         = [];
        _obstacleFirstCorner  = null;
        StatusText = CurrentTool switch
        {
            DrawingTool.DrawRoom        => RoomPolygon.Count >= 3 ? "Room drawn. Clear to redraw." : "Click to add vertices.",
            DrawingTool.DrawObstacle    => "Click 1st corner, then 2nd corner.",
            DrawingTool.PlaceMicrophone => "Click to place a microphone.",
            DrawingTool.PlaceStage      => "Click to place the sound source.",
            _                          => "Click to select.",
        };
        NotifyRedraw();
    }

    // ── Canvas input ──────────────────────────────────────────────────────────

    public void OnPointerDown(double offsetX, double offsetY, bool isLeft, bool isRight, bool isMiddle)
    {
        var roomPos = OffsetToRoom(offsetX, offsetY);
        var snapped = SnapToGrid(roomPos);

        if (isMiddle)
        {
            _isPanning       = true;
            _panDragStartX   = offsetX;
            _panDragStartY   = offsetY;
            _panXAtDragStart = PanX;
            _panYAtDragStart = PanY;
            return;
        }

        if (isRight) { CancelDrawing(); return; }
        if (!isLeft) return;

        switch (CurrentTool)
        {
            case DrawingTool.DrawRoom:        HandleDrawRoomClick(snapped);        break;
            case DrawingTool.DrawObstacle:    HandleDrawObstacleClick(snapped);    break;
            case DrawingTool.PlaceMicrophone: PlaceMicrophoneAt(snapped);          break;
            case DrawingTool.PlaceStage:      PlaceStageAt(snapped);               break;
        }
        NotifyRedraw();
    }

    public void OnPointerMove(double offsetX, double offsetY, bool leftHeld)
    {
        var roomPos   = OffsetToRoom(offsetX, offsetY);
        MouseRoomPos  = roomPos;
        CursorPosition = $"{roomPos.X:F2}, {roomPos.Y:F2} m";

        if (_isPanning)
        {
            PanX = _panXAtDragStart + (offsetX - _panDragStartX);
            PanY = _panYAtDragStart + (offsetY - _panDragStartY);
        }

        NotifyRedraw();
    }

    public void OnPointerUp(bool isMiddle)
    {
        if (isMiddle) _isPanning = false;
    }

    public void OnWheel(double delta, double offsetX, double offsetY)
    {
        double oldScale = BaseScale * Zoom;
        double newZoom  = Math.Clamp(Zoom * (delta > 0 ? 1.12 : 1.0 / 1.12), 0.05, 30.0);
        double newScale = BaseScale * newZoom;

        double rx = (offsetX - PanX) / oldScale;
        double ry = (offsetY - PanY) / oldScale;
        PanX  = offsetX - rx * newScale;
        PanY  = offsetY - ry * newScale;
        Zoom  = newZoom;

        NotifyRedraw();
    }

    // ── Drawing helpers ───────────────────────────────────────────────────────

    private void HandleDrawRoomClick(RoomPoint snapped)
    {
        if (RoomPolygon.Count >= 3) { StatusText = "Room drawn. Use Clear Room to redraw."; return; }

        if (DrawingPoints.Count >= 3 && snapped.DistanceTo(DrawingPoints[0]) < 0.75)
        { FinalizeRoomPolygon(); return; }

        DrawingPoints = [.. DrawingPoints, snapped];
        StatusText = DrawingPoints.Count >= 3
            ? $"{DrawingPoints.Count} pts — click near first to close."
            : $"{DrawingPoints.Count} pt(s) — keep clicking.";
    }

    private void FinalizeRoomPolygon()
    {
        RoomPolygon   = [.. DrawingPoints];
        DrawingPoints = [];
        StatusText    = IsSimulationMode ? "Room drawn. Place sound source." : "Room drawn. Place microphones.";
        HeatmapBitmap = null;
        HeatmapResult = null;
    }

    private void HandleDrawObstacleClick(RoomPoint snapped)
    {
        if (_obstacleFirstCorner is null)
        {
            _obstacleFirstCorner = snapped;
            DrawingPoints        = [snapped];
            StatusText           = "First corner set. Click for second.";
        }
        else
        {
            var p1 = _obstacleFirstCorner;
            var p2 = snapped;
            if (Math.Abs(p2.X - p1.X) < 0.1 || Math.Abs(p2.Y - p1.Y) < 0.1)
            { StatusText = "Too small — click further away."; return; }

            var obs = RoomObstacle.Create(
                [new(p1.X, p1.Y), new(p2.X, p1.Y), new(p2.X, p2.Y), new(p1.X, p2.Y)],
                ObstacleSurface, ObstacleIrregularity, $"Obstacle {Obstacles.Count + 1}");
            Obstacles            = [.. Obstacles, obs];
            _obstacleFirstCorner = null;
            DrawingPoints        = [];
            HeatmapBitmap        = null;
            HeatmapResult        = null;
            StatusText           = $"Obstacle {Obstacles.Count} added.";
        }
    }

    private void PlaceMicrophoneAt(RoomPoint pos)
    {
        var mic = MicrophoneNode.Create(pos, $"Mic {Microphones.Count + 1}");
        Microphones = [.. Microphones, mic];

        var devices = _captureService.GetAvailableDevices().Select(d => d.Name).ToList();
        MicConfigs.Add(new MicrophoneConfigViewModel(mic, _dialogService, devices, NotifyRedraw));

        HeatmapBitmap = null;
        HeatmapResult = null;
        StatusText     = $"Microphone {Microphones.Count} placed.";
    }

    private void PlaceStageAt(RoomPoint pos)
    {
        if (SoundSource is null)
            SoundSource = SoundSourceNode.Create(pos, SourceLevelDb);
        else
            SoundSource.MoveTo(pos);
        HeatmapBitmap = null;
        HeatmapResult = null;
        StatusText     = "Sound source placed. Press Process.";
    }

    private void CancelDrawing()
    {
        DrawingPoints        = [];
        _obstacleFirstCorner = null;
        StatusText           = "Drawing cancelled.";
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void FinishDrawing()
    {
        if (CurrentTool == DrawingTool.DrawRoom && DrawingPoints.Count >= 3)
            FinalizeRoomPolygon();
    }

    [RelayCommand]
    private void ClearRoom()
    {
        RoomPolygon   = [];
        DrawingPoints = [];
        HeatmapBitmap = null;
        HeatmapResult = null;
        StatusText     = "Room cleared.";
        NotifyRedraw();
    }

    [RelayCommand]
    private void ClearObstacles() { Obstacles = []; HeatmapBitmap = null; HeatmapResult = null; NotifyRedraw(); }

    [RelayCommand]
    private void ClearMicrophones()
    {
        Microphones = [];
        MicConfigs.Clear();
        HeatmapBitmap = null;
        HeatmapResult = null;
        NotifyRedraw();
    }

    [RelayCommand]
    private void ClearAll()
    {
        RoomPolygon   = [];
        DrawingPoints = [];
        Obstacles     = [];
        Microphones   = [];
        SoundSource   = null;
        HeatmapBitmap = null;
        HeatmapResult = null;
        MicConfigs.Clear();
        StatusText    = "Canvas cleared.";
        NotifyRedraw();
    }

    [RelayCommand]
    private void ResetView() { Zoom = 1.0; PanX = 0.0; PanY = 0.0; NotifyRedraw(); }

    // ── Simulation ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ComputeHeatmapAsync(CancellationToken ct)
    {
        if (RoomPolygon.Count < 3) { StatusText = "Draw room first."; return; }
        if (SoundSource is null)   { StatusText = "Place sound source first."; return; }

        IsComputing = true;
        StatusText  = "Computing simulation…";
        try
        {
            SoundSource.SetSourceLevel(SourceLevelDb);
            var query = new ComputeSoundDistributionQuery(
                RoomPolygon, Obstacles, SoundSource,
                WallSurface, WallIrregularity, GridResolution);

            var result = await Task.Run(
                async () => await _queryDispatcher.DispatchAsync<
                    ComputeSoundDistributionQuery, ComputeSoundDistributionResult>(query, ct), ct);

            HeatmapResult = result;
            HeatmapBitmap = BuildHeatmapBitmap(result);
            StatusText    = $"Done. SPL {result.MinSpl:F0}–{result.MaxSpl:F0} dB (Δ{result.MaxSpl - result.MinSpl:F0} dB)";
        }
        catch (OperationCanceledException) { StatusText = "Cancelled."; }
        catch (Exception ex)               { StatusText = $"Error: {ex.Message}"; }
        finally                            { IsComputing = false; NotifyRedraw(); }
    }

    // ── Measure Mode analysis ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task AnalyzeMeasurementsAsync(CancellationToken ct)
    {
        if (RoomPolygon.Count < 3)       { StatusText = "Draw room first."; return; }
        if (MicConfigs.Count < 1)        { StatusText = "Place at least one microphone."; return; }

        // Validate that at least some mics are configured.
        var configured = MicConfigs.Where(c =>
            (c.UseFile && c.Node.AssignedFilePath is not null) ||
            (!c.UseFile && c.SelectedDeviceIndex >= 0)).ToList();

        if (configured.Count == 0)
        {
            StatusText = "Assign a file or live device to at least one microphone.";
            return;
        }

        IsAnalyzing = true;
        StatusText  = $"Analyzing {configured.Count} source(s)…";
        HeatmapBitmap = null;
        HeatmapResult = null;

        foreach (var cfg in MicConfigs) cfg.ClearResult();

        try
        {
            var requests = configured.Select(c => new MicrophoneMeasurementRequest(
                c.Node.Id,
                c.EffectiveSourceType,
                c.UseFile ? c.Node.AssignedFilePath : null,
                c.UseFile ? -1 : c.SelectedDeviceIndex)).ToList();

            var analysisResults = await Task.Run(
                async () => await _measureService.AnalyzeAsync(requests, ct), ct);

            // Push results back to config VMs.
            foreach (var r in analysisResults)
            {
                var cfg = MicConfigs.FirstOrDefault(c => c.Node.Id == r.MicId);
                cfg?.SetResult(r);
            }

            // Build spatial interpolation from successful measurements.
            var measurements = analysisResults
                .Where(r => r.Success)
                .Select(r => (
                    Position: configured.First(c => c.Node.Id == r.MicId).Node.Position,
                    SplDb: r.SplDb))
                .ToList();

            if (measurements.Count >= 1)
            {
                HeatmapResult = InterpolateMeasured(measurements);
                HeatmapBitmap = BuildHeatmapBitmap(HeatmapResult);

                int ok = measurements.Count;
                float range = HeatmapResult.MaxSpl - HeatmapResult.MinSpl;
                StatusText = $"Analysis done. {ok}/{configured.Count} sources OK. " +
                             $"SPL range: {HeatmapResult.MinSpl:F0}–{HeatmapResult.MaxSpl:F0} dBFS (Δ{range:F0} dB)";
            }
            else
            {
                StatusText = "All sources failed. Check assignments.";
            }
        }
        catch (OperationCanceledException) { StatusText = "Cancelled."; }
        catch (Exception ex)               { StatusText = $"Error: {ex.Message}"; }
        finally                            { IsAnalyzing = false; NotifyRedraw(); }
    }

    // ── IDW spatial interpolation ─────────────────────────────────────────────

    private ComputeSoundDistributionResult InterpolateMeasured(
        IReadOnlyList<(RoomPoint Position, float SplDb)> measurements)
    {
        var poly = RoomPolygon;
        double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X);
        double minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
        double w = maxX - minX, h = maxY - minY;
        if (w < 0.1) w = 0.1;
        if (h < 0.1) h = 0.1;

        int gridW = GridResolution;
        int gridH = Math.Max(4, (int)Math.Round(gridW * h / w));

        var grid = new float[gridW, gridH];
        float minSpl = float.MaxValue, maxSpl = float.MinValue;

        for (int gx = 0; gx < gridW; gx++)
        for (int gy = 0; gy < gridH; gy++)
        {
            double rx = minX + (gx + 0.5) * w / gridW;
            double ry = minY + (gy + 0.5) * h / gridH;
            var pt = new RoomPoint(rx, ry);

            if (!IsInsidePolygon(pt, poly) || IsInsideAnyObstacle(pt))
            { grid[gx, gy] = float.NaN; continue; }

            // IDW in linear power domain (physically correct summation).
            double totalW = 0, totalP = 0;
            foreach (var (pos, spl) in measurements)
            {
                double d      = Math.Max(pos.DistanceTo(pt), 0.01);
                double weight = 1.0 / (d * d);  // IDW exponent = 2
                double power  = Math.Pow(10.0, spl / 10.0);
                totalW += weight;
                totalP += weight * power;
            }

            float val = totalW > 0
                ? (float)(10.0 * Math.Log10(Math.Max(totalP / totalW, 1e-30)))
                : float.NaN;

            grid[gx, gy] = val;
            if (!float.IsNaN(val)) { if (val < minSpl) minSpl = val; if (val > maxSpl) maxSpl = val; }
        }

        return new ComputeSoundDistributionResult(grid, minX, minY, maxX, maxY, minSpl, maxSpl);
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    private static bool IsInsidePolygon(RoomPoint pt, IReadOnlyList<RoomPoint> poly)
    {
        bool inside = false;
        int  j      = poly.Count - 1;
        for (int i = 0; i < poly.Count; i++)
        {
            var pi = poly[i]; var pj = poly[j];
            if ((pi.Y > pt.Y) != (pj.Y > pt.Y) &&
                pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
                inside = !inside;
            j = i;
        }
        return inside;
    }

    private bool IsInsideAnyObstacle(RoomPoint pt)
    {
        foreach (var obs in Obstacles)
            if (IsInsidePolygon(pt, obs.Polygon)) return true;
        return false;
    }

    // ── Heatmap bitmap ────────────────────────────────────────────────────────

    private static WriteableBitmap BuildHeatmapBitmap(ComputeSoundDistributionResult result)
    {
        int w = result.SplGrid.GetLength(0);
        int h = result.SplGrid.GetLength(1);
        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                                      PixelFormat.Bgra8888, AlphaFormat.Premul);
        float range = result.MaxSpl - result.MinSpl;
        if (range < 1f) range = 1f;

        using var fb = bmp.Lock();
        unsafe
        {
            byte* ptr    = (byte*)fb.Address;
            int   stride = fb.RowBytes;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int   off = y * stride + x * 4;
                float spl = result.SplGrid[x, y];

                if (float.IsNaN(spl))
                { ptr[off] = ptr[off+1] = ptr[off+2] = ptr[off+3] = 0; continue; }

                float t = Math.Clamp((spl - result.MinSpl) / range, 0f, 1f);
                HeatColor(t, out byte r, out byte g, out byte b);
                const byte a = 200;
                ptr[off]   = (byte)(b * a / 255);
                ptr[off+1] = (byte)(g * a / 255);
                ptr[off+2] = (byte)(r * a / 255);
                ptr[off+3] = a;
            }
        }
        return bmp;
    }

    private static void HeatColor(float t, out byte r, out byte g, out byte b)
    {
        if (t < 0.25f)      { float s = t / 0.25f; r = 0; g = (byte)(s * 180); b = (byte)(120 + s * 135); }
        else if (t < 0.5f)  { float s = (t-0.25f)/0.25f; r = 0; g = (byte)(180+s*75); b = (byte)(255*(1f-s)); }
        else if (t < 0.75f) { float s = (t-0.5f) /0.25f; r = (byte)(s*255); g = 255; b = 0; }
        else                { float s = (t-0.75f)/0.25f; r = 255; g = (byte)(255*(1f-s)); b = 0; }
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    public RoomPoint OffsetToRoom(double offsetX, double offsetY)
    {
        double scale = BaseScale * Zoom;
        return new RoomPoint((offsetX - PanX) / scale, (offsetY - PanY) / scale);
    }

    public Point RoomToOffset(RoomPoint p)
    {
        double scale = BaseScale * Zoom;
        return new Point(p.X * scale + PanX, p.Y * scale + PanY);
    }

    private static RoomPoint SnapToGrid(RoomPoint p, double step = 0.5) =>
        new(Math.Round(p.X / step) * step, Math.Round(p.Y / step) * step);
}
