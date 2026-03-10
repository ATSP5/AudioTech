using AudioTech.Domain.Common;

namespace AudioTech.Domain.ValueObjects;

public sealed class FrequencyRange : ValueObject
{
    public double MinHz { get; }
    public double MaxHz { get; }

    private FrequencyRange(double minHz, double maxHz)
    {
        MinHz = minHz;
        MaxHz = maxHz;
    }

    public static FrequencyRange Create(double minHz, double maxHz)
    {
        if (minHz < 0)
            throw new ArgumentException("Minimum frequency cannot be negative.", nameof(minHz));
        if (maxHz <= minHz)
            throw new ArgumentException("Maximum frequency must be greater than minimum.", nameof(maxHz));

        return new FrequencyRange(minHz, maxHz);
    }

    public static FrequencyRange HumanHearing() => Create(20, 20_000);
    public static FrequencyRange Bass() => Create(20, 300);
    public static FrequencyRange Midrange() => Create(300, 4_000);
    public static FrequencyRange Treble() => Create(4_000, 20_000);

    public bool Contains(double frequencyHz) => frequencyHz >= MinHz && frequencyHz <= MaxHz;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return MinHz;
        yield return MaxHz;
    }

    public override string ToString() => $"{MinHz}Hz - {MaxHz}Hz";
}
