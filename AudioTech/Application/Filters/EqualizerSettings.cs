namespace AudioTech.Application.Filters;

/// <summary>
/// Shared, mutable EQ state. <see cref="EqualizerFilter"/> instances hold a
/// reference to this object and detect mutations via <see cref="Version"/>.
/// All writes increment <see cref="Version"/> atomically so the filter
/// rebuilds its BiQuad coefficients on the next <c>Apply()</c> call.
/// </summary>
public sealed class EqualizerSettings
{
    // ── Band definition ───────────────────────────────────────────────────────

    public static readonly float[] BandFrequencies =
        [10, 30, 50, 75, 100, 125, 1000, 2000, 4000, 10_000, 16_000, 20_000];

    public static readonly string[] BandLabels =
        ["10 Hz", "30 Hz", "50 Hz", "75 Hz", "100 Hz", "125 Hz",
         "1 kHz", "2 kHz", "4 kHz", "10 kHz", "16 kHz", "20 kHz"];

    /// <summary>Q (bandwidth) per band — wider at extremes, tighter in the mid-range.</summary>
    public static readonly float[] BandQ =
        [0.70f, 0.90f, 1.00f, 1.20f, 1.40f, 1.41f,
         1.41f, 1.41f, 1.41f, 1.20f, 1.00f, 0.80f];

    public static int BandCount => BandFrequencies.Length; // 12

    // ── Mutable state ─────────────────────────────────────────────────────────

    private readonly float[] _bandGainsDb = new float[BandCount]; // default 0 dB
    private float _balanceDb; // -20 … +20, default 0

    /// <summary>
    /// Monotonically-increasing counter. The filter checks this on every
    /// <c>Apply()</c> and rebuilds BiQuads when the value has changed.
    /// </summary>
    public int Version { get; private set; }

    public IReadOnlyList<float> BandGainsDb => _bandGainsDb;

    public void SetBandGain(int bandIndex, float gainDb)
    {
        _bandGainsDb[bandIndex] = gainDb;
        Version++;
    }

    /// <summary>
    /// L/R balance in dB.  Negative pans left (R is attenuated).
    /// Positive pans right (L is attenuated). Range: -20 … +20.
    /// </summary>
    public float BalanceDb
    {
        get => _balanceDb;
        set { _balanceDb = value; Version++; }
    }

    public void Reset()
    {
        Array.Clear(_bandGainsDb);
        _balanceDb = 0;
        Version++;
    }
}
