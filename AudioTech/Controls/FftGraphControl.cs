using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using AudioTech.ViewModels;

namespace AudioTech.Controls;

public class FftGraphControl : Control
{
    // ── Avalonia properties ───────────────────────────────────────────────────

    public static readonly StyledProperty<IReadOnlyList<ChannelDisplayData>?> ChannelsProperty =
        AvaloniaProperty.Register<FftGraphControl, IReadOnlyList<ChannelDisplayData>?>(nameof(Channels));

    public static readonly StyledProperty<float[]?> PhaseDifferenceProperty =
        AvaloniaProperty.Register<FftGraphControl, float[]?>(nameof(PhaseDifference));

    public static readonly StyledProperty<int> PhaseDiffSampleRateProperty =
        AvaloniaProperty.Register<FftGraphControl, int>(nameof(PhaseDiffSampleRate), 44100);

    public static readonly StyledProperty<double> MinDbProperty =
        AvaloniaProperty.Register<FftGraphControl, double>(nameof(MinDb), -120);

    public static readonly StyledProperty<double> MaxDbProperty =
        AvaloniaProperty.Register<FftGraphControl, double>(nameof(MaxDb), 0);

    public static readonly StyledProperty<double> MinFrequencyProperty =
        AvaloniaProperty.Register<FftGraphControl, double>(nameof(MinFrequency), 20);

    public static readonly StyledProperty<double> MaxFrequencyProperty =
        AvaloniaProperty.Register<FftGraphControl, double>(nameof(MaxFrequency), 20000);

    public IReadOnlyList<ChannelDisplayData>? Channels       { get => GetValue(ChannelsProperty);           set => SetValue(ChannelsProperty, value); }
    public float[]?                           PhaseDifference { get => GetValue(PhaseDifferenceProperty);   set => SetValue(PhaseDifferenceProperty, value); }
    public int                                PhaseDiffSampleRate { get => GetValue(PhaseDiffSampleRateProperty); set => SetValue(PhaseDiffSampleRateProperty, value); }
    public double MinDb        { get => GetValue(MinDbProperty);        set => SetValue(MinDbProperty, value); }
    public double MaxDb        { get => GetValue(MaxDbProperty);        set => SetValue(MaxDbProperty, value); }
    public double MinFrequency { get => GetValue(MinFrequencyProperty); set => SetValue(MinFrequencyProperty, value); }
    public double MaxFrequency { get => GetValue(MaxFrequencyProperty); set => SetValue(MaxFrequencyProperty, value); }

    static FftGraphControl()
    {
        AffectsRender<FftGraphControl>(
            ChannelsProperty, PhaseDifferenceProperty, PhaseDiffSampleRateProperty,
            MinDbProperty, MaxDbProperty, MinFrequencyProperty, MaxFrequencyProperty);
    }

    // ── Rendering resources ───────────────────────────────────────────────────

    private static readonly IBrush   BgBrush     = new SolidColorBrush(Color.FromRgb(6, 6, 12));
    private static readonly IBrush   LabelBrush  = new SolidColorBrush(Color.FromRgb(130, 130, 130));
    private static readonly IPen     GridPen     = new Pen(new SolidColorBrush(Color.FromArgb(50,  80, 80, 90)),  1);
    private static readonly IPen     AxisPen     = new Pen(new SolidColorBrush(Color.FromArgb(90, 160,160,170)),  1);
    private static readonly Typeface SmallFont   = new(FontFamily.Default);
    // Phase difference curve — white, solid
    private static readonly Color PhaseColor = Color.FromRgb(255, 255, 255);

    private static readonly double[] GridFreqs   = [50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000];
    private static readonly double[] GridDbLevels = [-120, -100, -80, -60, -40, -20, 0];
    private static readonly double[] PhaseGridDeg = [-180, -90, 0, 90, 180];

    // Layout constants
    private const double LeftMargin     = 38;
    private const double RightMarginFull = 44;  // when phase axis is visible
    private const double RightMarginNone = 8;   // when no phase
    private const double TopMargin      = 6;
    private const double BotMargin      = 18;

    // ── Render ────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w < 1 || h < 1) return;

        var   phaseDiff = PhaseDifference;
        bool  hasPhase  = phaseDiff is { Length: > 0 };

        double rightMargin = hasPhase ? RightMarginFull : RightMarginNone;
        double plotW = w - LeftMargin - rightMargin;
        double plotH = h - TopMargin - BotMargin;
        if (plotW < 1 || plotH < 1) return;

        double logMin = Math.Log10(Math.Max(MinFrequency, 1));
        double logMax = Math.Log10(Math.Max(MaxFrequency, 2));

        // ── Background ────────────────────────────────────────────────────────
        ctx.FillRectangle(BgBrush, new Rect(Bounds.Size));

        // ── dB grid (horizontal lines + left labels) ──────────────────────────
        foreach (double db in GridDbLevels)
        {
            if (db < MinDb || db > MaxDb) continue;
            double y = TopMargin + DbToY(db, plotH);
            ctx.DrawLine(GridPen, new Point(LeftMargin, y), new Point(LeftMargin + plotW, y));
            ctx.DrawText(MakeLabel($"{db:0}", 9), new Point(LeftMargin - 36, y - 6));
        }

        // ── Frequency grid (vertical lines + bottom labels) ───────────────────
        foreach (double freq in GridFreqs)
        {
            if (freq < MinFrequency || freq > MaxFrequency) continue;
            double x = LeftMargin + FreqToX(freq, plotW, logMin, logMax);
            ctx.DrawLine(GridPen, new Point(x, TopMargin), new Point(x, TopMargin + plotH));
            string lbl = freq >= 1000 ? $"{freq / 1000:0}k" : $"{freq:0}";
            ctx.DrawText(MakeLabel(lbl, 9), new Point(x + 2, TopMargin + plotH + 3));
        }

        // ── Phase axis (right side) — only when phase data is present ─────────
        if (hasPhase)
        {
            foreach (double deg in PhaseGridDeg)
            {
                double rad = deg * Math.PI / 180.0;
                double y   = TopMargin + PhaseToY(rad, plotH);
                ctx.DrawLine(AxisPen,
                    new Point(LeftMargin + plotW, y),
                    new Point(LeftMargin + plotW + 5, y));
                ctx.DrawText(MakeLabel($"{deg:0}°", 8),
                    new Point(LeftMargin + plotW + 7, y - 6));
            }

            // Phase zero reference line (subtle)
            double zeroY = TopMargin + PhaseToY(0, plotH);
            ctx.DrawLine(AxisPen,
                new Point(LeftMargin, zeroY),
                new Point(LeftMargin + plotW, zeroY));
        }

        // ── Magnitude curves (one per channel) ────────────────────────────────
        var channels = Channels;
        if (channels is not null)
        {
            foreach (var ch in channels)
            {
                double binHz = ch.SampleRate / 2.0 / ch.MagnitudeDb.Length;
                var pen = new Pen(new SolidColorBrush(ch.Color), 1.5);
                DrawCurve(ctx, pen, ch.MagnitudeDb, ch.MagnitudeDb.Length, binHz,
                    plotW, plotH, logMin, logMax, (v, pH) => DbToY(v, pH));
            }
        }

        // ── Dominant frequency label ──────────────────────────────────────────────
        if (channels is not null && channels.Count > 0)
        {
            double domFreq = 0;
            float  domDb   = float.MinValue;

            foreach (var ch in channels)
            {
                double binHz = ch.SampleRate / 2.0 / ch.MagnitudeDb.Length;
                for (int i = 1; i < ch.MagnitudeDb.Length; i++)
                {
                    double freq = i * binHz;
                    if (freq < MinFrequency || freq > MaxFrequency) continue;
                    if (ch.MagnitudeDb[i] > domDb)
                    {
                        domDb   = ch.MagnitudeDb[i];
                        domFreq = freq;
                    }
                }
            }

            if (domFreq > 0)
            {
                // Vertical marker line at dominant frequency
                double domX = LeftMargin + FreqToX(domFreq, plotW, logMin, logMax);
                ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 220, 80)), 1),
                    new Point(domX, TopMargin),
                    new Point(domX, TopMargin + plotH));

                // Text label
                string freqStr = domFreq >= 1000
                    ? $"▲ {domFreq / 1000:0.00} kHz"
                    : $"▲ {domFreq:0} Hz";
                var labelText = MakeLabel(freqStr, 9.5);
                // Position label just left of the marker (or right if near left edge)
                double labelX = domX + 4;
                if (labelX + 70 > LeftMargin + plotW) labelX = domX - 74;
                ctx.DrawText(labelText, new Point(labelX, TopMargin + 4));
            }
        }

        // ── Phase difference curve (single, teal, dashed) ────────────────────
        if (hasPhase && phaseDiff is not null)
        {
            double binHz = PhaseDiffSampleRate / 2.0 / phaseDiff.Length;
            var pen = new Pen(new SolidColorBrush(PhaseColor), 1.2);
            DrawCurve(ctx, pen, phaseDiff, phaseDiff.Length, binHz,
                plotW, plotH, logMin, logMax, (v, pH) => PhaseToY(v, pH));
        }

        // ── Legend ────────────────────────────────────────────────────────────
        DrawLegend(ctx, channels, hasPhase, rightMargin);
    }

    // ── Curve helper ──────────────────────────────────────────────────────────

    private void DrawCurve(
        DrawingContext ctx, IPen pen,
        float[] values, int bins, double binHz,
        double plotW, double plotH,
        double logMin, double logMax,
        Func<double, double, double> toY)
    {
        var geo = new StreamGeometry();
        using var sgc = geo.Open();
        bool started = false;

        for (int i = 1; i < bins; i++)
        {
            double freq = i * binHz;
            if (freq < MinFrequency || freq > MaxFrequency) continue;

            double x = LeftMargin + FreqToX(freq, plotW, logMin, logMax);
            double y = TopMargin  + toY(values[i], plotH);

            if (!started) { sgc.BeginFigure(new Point(x, y), false); started = true; }
            else          { sgc.LineTo(new Point(x, y)); }
        }

        if (started) { sgc.EndFigure(false); ctx.DrawGeometry(null, pen, geo); }
    }

    // ── Legend ────────────────────────────────────────────────────────────────

    private void DrawLegend(
        DrawingContext ctx,
        IReadOnlyList<ChannelDisplayData>? channels,
        bool hasPhase,
        double rightMargin)
    {
        int channelCount = channels?.Count ?? 0;
        if (channelCount == 0 && !hasPhase) return;

        const double itemH   = 16;
        const double padX    = 8;
        const double padY    = 6;
        const double swatchW = 14;
        const double swatchH = 8;
        const double textOff = 20;
        const double legendW = 180;

        int    rows    = channelCount + (hasPhase ? 1 : 0);
        double legendH = padY * 2 + rows * itemH;
        double lx      = Bounds.Width - legendW - rightMargin - 4;
        double ly      = TopMargin + 4;

        // Box
        ctx.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(185, 5, 5, 15)),
            new Pen(new SolidColorBrush(Color.FromArgb(70, 200, 200, 220)), 1),
            new RoundedRect(new Rect(lx, ly, legendW, legendH), 4));

        double cy = ly + padY;

        // ── Channel magnitude rows ────────────────────────────────────────────
        if (channels is not null)
        {
            foreach (var ch in channels)
            {
                ctx.FillRectangle(
                    new SolidColorBrush(ch.Color),
                    new Rect(lx + padX, cy + (itemH - swatchH) / 2, swatchW, swatchH));

                ctx.DrawText(
                    MakeLabel(ch.Label, 10),
                    new Point(lx + padX + textOff, cy + 1));
                cy += itemH;
            }
        }

        // ── Phase difference row (only when visible) ──────────────────────────
        if (hasPhase)
        {
            double lineY   = cy + itemH / 2;
            var    dashPen = new Pen(new SolidColorBrush(PhaseColor), 1.2);

            ctx.DrawLine(dashPen,
                new Point(lx + padX, lineY),
                new Point(lx + padX + swatchW, lineY));

            ctx.DrawText(
                MakeLabel("Phase diff (Ch2 − Ch1)", 10),
                new Point(lx + padX + textOff, cy + 1));
            cy += itemH;
        }
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    private double FreqToX(double freq, double plotW, double logMin, double logMax) =>
        plotW * (Math.Log10(freq) - logMin) / (logMax - logMin);

    private double DbToY(double db, double plotH) =>
        plotH * (1 - Math.Clamp((db - MinDb) / (MaxDb - MinDb), 0, 1));

    private static double PhaseToY(double rad, double plotH) =>
        plotH * (1 - (rad + Math.PI) / (2 * Math.PI));

    private static FormattedText MakeLabel(string text, double size) =>
        new(text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            SmallFont, size, LabelBrush);
}
