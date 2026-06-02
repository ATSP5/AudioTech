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

public enum DrawingTool { Select, DrawRoom, DrawObstacle, PlaceMicrophone, PlaceStage }

public partial class RoomAcousticsViewModel : ViewModelBase
{
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly IDialogService   _dialogService;

    // ── Canvas event ─────────────────────────────────────────────────────────
    public event EventHandler? DrawingChanged;
    private void NotifyRedraw() => DrawingChanged?.Invoke(this, EventArgs.Empty);

    // ── Room geometry ─────────────────────────────────────────────────────────
    [ObservableProperty] private List<RoomPoint>    _roomPolygon  = [];
    [ObservableProperty] private List<RoomObstacle> _obstacles    = [];
    [ObservableProperty] private List<MicrophoneNode> _microphones = [];
    [ObservableProperty] private SoundSourceNode?   _soundSource;

    // ── In-progress drawing ───────────────────────────────────────────────────
    [ObservableProperty] private List<RoomPoint>  _drawingPoints = [];
    [ObservableProperty] private RoomPoint?       _mouseRoomPos;
    private RoomPoint? _obstacleFirstCorner;
    private bool       _isPanning;
    private double     _panDragStartX, _panDragStartY;
    private double     _panXAtDragStart, _panYAtDragStart;

    // ── Canvas transform ──────────────────────────────────────────────────────
    [ObservableProperty] private double _zoom   = 1.0;
    [ObservableProperty] private double _panX   = 0.0;
    [ObservableProperty] private double _panY   = 0.0;

    public const double BaseScale = 50.0; // pixels per metre at zoom=1

    // ── Tools ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private DrawingTool _currentTool = DrawingTool.DrawRoom;
    [ObservableProperty] private bool _isSelectTool;
    [ObservableProperty] private bool _isDrawRoomTool = true;
    [ObservableProperty] private bool _isDrawObstacleTool;
    [ObservableProperty] private bool _isPlaceMicrophoneTool;
    [ObservableProperty] private bool _isPlaceStageTool;

    // ── Room wall properties ──────────────────────────────────────────────────
    [ObservableProperty] private SurfaceType _wallSurface      = SurfaceType.Hard;
    [ObservableProperty] private double      _wallIrregularity = 0.0;

    // ── New obstacle properties ───────────────────────────────────────────────
    [ObservableProperty] private SurfaceType _obstacleSurface      = SurfaceType.Mixed;
    [ObservableProperty] private double      _obstacleIrregularity = 0.0;

    // ── Source ────────────────────────────────────────────────────────────────
    [ObservableProperty] private double _sourceLevelDb = 94.0;

    // ── Heatmap ───────────────────────────────────────────────────────────────
    [ObservableProperty] private ComputeSoundDistributionResult? _heatmapResult;
    [ObservableProperty] private WriteableBitmap?               _heatmapBitmap;
    [ObservableProperty] private bool                           _isComputing;
    [ObservableProperty] private int                            _gridResolution = 80;

    // ── Status ────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _statusText    = "Select tool, then draw the room boundary.";
    [ObservableProperty] private string _cursorPosition = "0.00, 0.00 m";

    // ── Surface display names (for UI combos) ─────────────────────────────────
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
    public RoomAcousticsViewModel(IQueryDispatcher queryDispatcher, IDialogService dialogService)
    {
        _queryDispatcher = queryDispatcher;
        _dialogService   = dialogService;
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

        // Cancel in-progress drawing when switching tools.
        DrawingPoints       = [];
        _obstacleFirstCorner = null;

        StatusText = CurrentTool switch
        {
            DrawingTool.DrawRoom        => RoomPolygon.Count >= 3
                                           ? "Room already drawn. Clear to redraw."
                                           : "Click to add vertices. Click near first point to close.",
            DrawingTool.DrawObstacle    => "Click first corner, then second corner of obstacle.",
            DrawingTool.PlaceMicrophone => "Click to place a microphone.",
            DrawingTool.PlaceStage      => "Click to place the sound source (stage).",
            _                          => "Click to select an element.",
        };
        NotifyRedraw();
    }

    // ── Canvas input ──────────────────────────────────────────────────────────

    public void OnPointerDown(double offsetFromCenterX, double offsetFromCenterY,
                              bool isLeft, bool isRight, bool isMiddle)
    {
        var roomPos = OffsetToRoom(offsetFromCenterX, offsetFromCenterY);
        var snapped = SnapToGrid(roomPos);

        if (isMiddle)
        {
            _isPanning       = true;
            _panDragStartX   = offsetFromCenterX;
            _panDragStartY   = offsetFromCenterY;
            _panXAtDragStart = PanX;
            _panYAtDragStart = PanY;
            return;
        }

        if (isRight)
        {
            CancelDrawing();
            return;
        }

        if (!isLeft) return;

        switch (CurrentTool)
        {
            case DrawingTool.DrawRoom:
                HandleDrawRoomClick(snapped);
                break;

            case DrawingTool.DrawObstacle:
                HandleDrawObstacleClick(snapped);
                break;

            case DrawingTool.PlaceMicrophone:
                PlaceMicrophoneAt(snapped);
                break;

            case DrawingTool.PlaceStage:
                PlaceStageAt(snapped);
                break;
        }
        NotifyRedraw();
    }

    public void OnPointerMove(double offsetFromCenterX, double offsetFromCenterY, bool leftHeld)
    {
        var roomPos = OffsetToRoom(offsetFromCenterX, offsetFromCenterY);
        MouseRoomPos    = roomPos;
        CursorPosition  = $"{roomPos.X:F2}, {roomPos.Y:F2} m";

        if (_isPanning && leftHeld is false)
        {
            PanX = _panXAtDragStart + (offsetFromCenterX - _panDragStartX);
            PanY = _panYAtDragStart + (offsetFromCenterY - _panDragStartY);
        }

        NotifyRedraw();
    }

    public void OnPointerUp(bool isMiddle)
    {
        if (isMiddle) _isPanning = false;
    }

    public void OnWheel(double delta, double offsetFromCenterX, double offsetFromCenterY)
    {
        double oldScale = BaseScale * Zoom;
        double factor   = delta > 0 ? 1.12 : 1.0 / 1.12;
        double newZoom  = Math.Clamp(Zoom * factor, 0.05, 30.0);
        double newScale = BaseScale * newZoom;

        // Keep room point under mouse stationary.
        double rx = (offsetFromCenterX - PanX) / oldScale;
        double ry = (offsetFromCenterY - PanY) / oldScale;
        PanX = offsetFromCenterX - rx * newScale;
        PanY = offsetFromCenterY - ry * newScale;
        Zoom = newZoom;

        NotifyRedraw();
    }

    // ── Drawing helpers ───────────────────────────────────────────────────────

    private void HandleDrawRoomClick(RoomPoint snapped)
    {
        if (RoomPolygon.Count >= 3)
        {
            StatusText = "Room already drawn. Use Clear Room to redraw.";
            return;
        }

        if (DrawingPoints.Count >= 3)
        {
            // Close if near first point.
            if (snapped.DistanceTo(DrawingPoints[0]) < 0.75)
            {
                FinalizeRoomPolygon();
                return;
            }
        }

        DrawingPoints = [.. DrawingPoints, snapped];
        StatusText = DrawingPoints.Count >= 3
            ? $"{DrawingPoints.Count} pts — click near first point to close."
            : $"{DrawingPoints.Count} point(s) — keep clicking.";
    }

    private void FinalizeRoomPolygon()
    {
        RoomPolygon   = [.. DrawingPoints];
        DrawingPoints = [];
        StatusText    = "Room drawn. Place the sound source, then press Process.";
        HeatmapBitmap = null;
        HeatmapResult = null;
        NotifyRedraw();
    }

    private void HandleDrawObstacleClick(RoomPoint snapped)
    {
        if (_obstacleFirstCorner is null)
        {
            _obstacleFirstCorner = snapped;
            DrawingPoints        = [snapped];
            StatusText           = "First corner set. Click for second corner.";
        }
        else
        {
            var p1 = _obstacleFirstCorner;
            var p2 = snapped;

            if (Math.Abs(p2.X - p1.X) < 0.1 || Math.Abs(p2.Y - p1.Y) < 0.1)
            {
                StatusText = "Obstacle too small — click further away.";
                return;
            }

            var rectPoly = new RoomPoint[]
            {
                new(p1.X, p1.Y),
                new(p2.X, p1.Y),
                new(p2.X, p2.Y),
                new(p1.X, p2.Y)
            };

            var obs = RoomObstacle.Create(rectPoly, ObstacleSurface, ObstacleIrregularity,
                                          $"Obstacle {Obstacles.Count + 1}");
            Obstacles            = [.. Obstacles, obs];
            _obstacleFirstCorner = null;
            DrawingPoints        = [];
            HeatmapBitmap        = null;
            HeatmapResult        = null;
            StatusText           = $"Obstacle {Obstacles.Count} added. Click for next, or switch tool.";
        }
    }

    private void PlaceMicrophoneAt(RoomPoint pos)
    {
        var mic = MicrophoneNode.Create(pos, $"Mic {Microphones.Count + 1}");
        Microphones   = [.. Microphones, mic];
        HeatmapBitmap = null;
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
        StatusText     = "Sound source placed. Press Process to compute.";
    }

    private void CancelDrawing()
    {
        DrawingPoints        = [];
        _obstacleFirstCorner = null;
        StatusText           = "Drawing cancelled.";
        NotifyRedraw();
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
        StatusText     = "Room cleared. Draw a new boundary.";
        NotifyRedraw();
    }

    [RelayCommand]
    private void ClearObstacles()
    {
        Obstacles     = [];
        HeatmapBitmap = null;
        HeatmapResult = null;
        NotifyRedraw();
    }

    [RelayCommand]
    private void ClearMicrophones()
    {
        Microphones = [];
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
        StatusText     = "Canvas cleared.";
        NotifyRedraw();
    }

    [RelayCommand]
    private void ResetView()
    {
        Zoom = 1.0;
        PanX = 0.0;
        PanY = 0.0;
        NotifyRedraw();
    }

    [RelayCommand]
    private async Task ComputeHeatmapAsync(CancellationToken ct)
    {
        if (RoomPolygon.Count < 3) { StatusText = "Draw the room boundary first."; return; }
        if (SoundSource is null)   { StatusText = "Place the sound source first."; return; }

        IsComputing = true;
        StatusText  = "Computing sound distribution…";

        try
        {
            SoundSource.SetSourceLevel(SourceLevelDb);

            var query = new ComputeSoundDistributionQuery(
                RoomPolygon, Obstacles, SoundSource,
                WallSurface, WallIrregularity, GridResolution);

            var result = await Task.Run(
                async () => await _queryDispatcher.DispatchAsync<ComputeSoundDistributionQuery, ComputeSoundDistributionResult>(query, ct), ct);

            HeatmapResult = result;
            HeatmapBitmap = BuildHeatmapBitmap(result);

            float dyn = result.MaxSpl - result.MinSpl;
            StatusText = $"Done. SPL: {result.MinSpl:F0} – {result.MaxSpl:F0} dB  (Δ{dyn:F0} dB)";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Computation cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsComputing = false;
            NotifyRedraw();
        }
    }

    [RelayCommand]
    private async Task AssignMicrophoneFileAsync(MicrophoneNode mic)
    {
        var path = await _dialogService.ShowOpenFilePickerAsync(
        [
            ("Audio Files", ["*.wav", "*.mp3", "*.flac", "*.ogg", "*.aiff"])
        ]);

        if (path is not null)
        {
            mic.AssignFile(path);
            NotifyRedraw();
        }
    }

    // ── Heatmap bitmap builder ────────────────────────────────────────────────

    private static WriteableBitmap BuildHeatmapBitmap(ComputeSoundDistributionResult result)
    {
        int w = result.SplGrid.GetLength(0);
        int h = result.SplGrid.GetLength(1);

        var bmp = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

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
                int   offset = y * stride + x * 4;
                float spl    = result.SplGrid[x, y];

                if (float.IsNaN(spl))
                {
                    ptr[offset] = ptr[offset + 1] = ptr[offset + 2] = ptr[offset + 3] = 0;
                    continue;
                }

                float t = Math.Clamp((spl - result.MinSpl) / range, 0f, 1f);
                HeatColor(t, out byte r, out byte g, out byte b);
                const byte a = 200;

                // Premultiplied BGRA
                ptr[offset]     = (byte)(b * a / 255);
                ptr[offset + 1] = (byte)(g * a / 255);
                ptr[offset + 2] = (byte)(r * a / 255);
                ptr[offset + 3] = a;
            }
        }

        return bmp;
    }

    private static void HeatColor(float t, out byte r, out byte g, out byte b)
    {
        // Blue → Cyan → Green → Yellow → Red
        if (t < 0.25f)
        {
            float s = t / 0.25f;
            r = 0; g = (byte)(s * 180); b = (byte)(120 + s * 135);
        }
        else if (t < 0.5f)
        {
            float s = (t - 0.25f) / 0.25f;
            r = 0; g = (byte)(180 + s * 75); b = (byte)(255 * (1f - s));
        }
        else if (t < 0.75f)
        {
            float s = (t - 0.5f) / 0.25f;
            r = (byte)(s * 255); g = 255; b = 0;
        }
        else
        {
            float s = (t - 0.75f) / 0.25f;
            r = 255; g = (byte)(255 * (1f - s)); b = 0;
        }
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    /// Converts canvas mouse offset from canvas centre to room metres.
    public RoomPoint OffsetToRoom(double offsetX, double offsetY)
    {
        double scale = BaseScale * Zoom;
        return new RoomPoint(
            (offsetX - PanX) / scale,
            (offsetY - PanY) / scale);
    }

    /// Converts room metres to canvas offset from canvas centre.
    public Point RoomToOffset(RoomPoint p)
    {
        double scale = BaseScale * Zoom;
        return new Point(p.X * scale + PanX, p.Y * scale + PanY);
    }

    private static RoomPoint SnapToGrid(RoomPoint p, double step = 0.5)
    {
        return new RoomPoint(
            Math.Round(p.X / step) * step,
            Math.Round(p.Y / step) * step);
    }
}
