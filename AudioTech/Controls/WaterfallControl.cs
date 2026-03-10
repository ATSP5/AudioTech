using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AudioTech.Controls;

public class WaterfallControl : Control
{
    // ── Avalonia properties ──────────────────────────────────────────────────

    public static readonly StyledProperty<float[]?> FftDataProperty =
        AvaloniaProperty.Register<WaterfallControl, float[]?>(nameof(FftData));

    public static readonly StyledProperty<double> MinDbProperty =
        AvaloniaProperty.Register<WaterfallControl, double>(nameof(MinDb), -120);

    public static readonly StyledProperty<double> MaxDbProperty =
        AvaloniaProperty.Register<WaterfallControl, double>(nameof(MaxDb), -40);

    public float[]? FftData { get => GetValue(FftDataProperty); set => SetValue(FftDataProperty, value); }
    public double   MinDb   { get => GetValue(MinDbProperty);   set => SetValue(MinDbProperty, value); }
    public double   MaxDb   { get => GetValue(MaxDbProperty);   set => SetValue(MaxDbProperty, value); }

    // ── Internal state ───────────────────────────────────────────────────────

    private const int BitmapWidth  = 1024;
    private const int BitmapHeight = 512;

    private WriteableBitmap? _bitmap;

    // ── Static ctor ──────────────────────────────────────────────────────────

    static WaterfallControl()
    {
        FftDataProperty.Changed.AddClassHandler<WaterfallControl>((ctrl, _) => ctrl.OnFftDataChanged());
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _bitmap = new WriteableBitmap(
            new PixelSize(BitmapWidth, BitmapHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        ClearBitmap();
    }

    // ── Data update ──────────────────────────────────────────────────────────

    private void OnFftDataChanged()
    {
        var data = FftData;
        if (data is null || _bitmap is null) return;

        using var fb = _bitmap.Lock();

        unsafe
        {
            byte* ptr = (byte*)fb.Address;
            int stride = fb.RowBytes;
            int totalBytes = stride * (BitmapHeight - 1);

            // Shift existing rows down by one (memmove-safe)
            Buffer.MemoryCopy(ptr, ptr + stride, totalBytes, totalBytes);

            // Write new row at top (row 0)
            uint* row = (uint*)ptr;
            double range = MaxDb - MinDb;

            for (int x = 0; x < BitmapWidth; x++)
            {
                // Map pixel x → FFT bin (logarithmic)
                double t = x / (double)(BitmapWidth - 1);
                double logMin = Math.Log10(1.0);
                double logMax = Math.Log10(data.Length);
                double logBin = logMin + t * (logMax - logMin);
                int binIndex = (int)Math.Clamp(Math.Pow(10, logBin), 1, data.Length - 1);

                float db = data[binIndex];
                float normalized = (float)Math.Clamp((db - MinDb) / range, 0.0, 1.0);
                int lutIndex = (int)(normalized * 255);
                row[x] = ColorMap.Lut256[lutIndex];
            }
        }

        InvalidateVisual();
    }

    private void ClearBitmap()
    {
        if (_bitmap is null) return;
        using var fb = _bitmap.Lock();
        unsafe
        {
            byte* ptr = (byte*)fb.Address;
            int totalBytes = fb.RowBytes * BitmapHeight;
            for (int i = 0; i < totalBytes; i += 4)
            {
                ptr[i]     = 10;  // B
                ptr[i + 1] = 5;   // G
                ptr[i + 2] = 5;   // R
                ptr[i + 3] = 255; // A
            }
        }
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        if (_bitmap is null) return;
        ctx.DrawImage(_bitmap, new Rect(0, 0, BitmapWidth, BitmapHeight), new Rect(Bounds.Size));
    }
}
