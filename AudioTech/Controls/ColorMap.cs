using Avalonia.Media;

namespace AudioTech.Controls;

/// <summary>Plasma-like colormap: dark → purple → magenta → white.</summary>
public static class ColorMap
{
    // BGRA key stops at t=0..1
    private static readonly (float T, byte B, byte G, byte R, byte A)[] Stops =
    [
        (0.00f,  10,  5,   5, 255),   // near-black
        (0.20f, 120, 10,  30, 255),   // dark blue/indigo
        (0.45f, 160, 20, 100, 255),   // purple
        (0.65f, 180, 60, 200, 255),   // magenta/pink
        (0.82f, 220,160, 255, 255),   // light lavender
        (1.00f, 255,255, 255, 255),   // white
    ];

    /// <summary>Maps a 0..1 value to a BGRA packed uint32.</summary>
    public static uint ToBgra(float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        int hi = 1;
        while (hi < Stops.Length - 1 && Stops[hi].T < t) hi++;
        int lo = hi - 1;

        float range = Stops[hi].T - Stops[lo].T;
        float alpha = range < 1e-6f ? 0f : (t - Stops[lo].T) / range;

        byte Lerp(byte a, byte b) => (byte)(a + (b - a) * alpha);

        byte blue  = Lerp(Stops[lo].B, Stops[hi].B);
        byte green = Lerp(Stops[lo].G, Stops[hi].G);
        byte red   = Lerp(Stops[lo].R, Stops[hi].R);

        return (uint)((255 << 24) | (red << 16) | (green << 8) | blue);
    }

    /// <summary>Pre-built 256-entry lookup table for fast conversion.</summary>
    public static readonly uint[] Lut256 = BuildLut(256);

    private static uint[] BuildLut(int size)
    {
        var lut = new uint[size];
        for (int i = 0; i < size; i++)
            lut[i] = ToBgra(i / (float)(size - 1));
        return lut;
    }
}
