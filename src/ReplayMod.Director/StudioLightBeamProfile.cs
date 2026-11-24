using System;

namespace ReplayMod.Director;

// Original procedural angular masks. They shape a native spot cone, not its
// distance attenuation, and do not turn it into a rectangular area emitter.
public static class StudioLightBeamProfile
{
    public static bool Valid(int profile, float width, float height, float softness)
        => profile >= 0 && profile <= 4 && Finite(width) && width >= .1f && width <= 1
            && Finite(height) && height >= .1f && height <= 1
            && Finite(softness) && softness >= 0 && softness <= 1;

    public static float Sample(int profile, float width, float height, float softness, float u, float v)
    {
        if (!Valid(profile, width, height, softness) || !Finite(u) || !Finite(v)
            || u < 0 || u > 1 || v < 0 || v > 1) return 0;
        if (profile == 0) return 1;
        // An opaque black border prevents Clamp sampling from extending the
        // mask past its projection. Alpha stays opaque in the raster output.
        if (u == 0 || u == 1 || v == 0 || v == 1) return 0;
        double x = (u * 2.0 - 1) / width, y = (v * 2.0 - 1) / height;
        double radius = profile == 1 ? Math.Sqrt(x * x + y * y) : Math.Max(Math.Abs(x), Math.Abs(y));
        double edge = Edge(radius, softness);
        if (edge <= 0) return 0;
        if (profile == 3)
        {
            // Four panes, separated by simple horizontal/vertical mullions.
            double feather = .01 + softness * .10;
            edge *= Smooth(.045, .045 + feather, Math.Abs(x))
                * Smooth(.045, .045 + feather, Math.Abs(y));
        }
        else if (profile == 4)
        {
            // Six open horizontal bands with five dark interior dividers.
            double phase = (y + 1) * 3;
            double fraction = phase - Math.Floor(phase);
            double gapDistance = Math.Min(fraction, 1 - fraction) * 2;
            edge *= Smooth(.12, .12 + .01 + softness * .25, gapDistance);
        }
        return (float)Math.Max(0, Math.Min(1, edge));
    }

    // Caller supplies its reusable upload buffer. Keeping this rasterizer
    // independent of Unity makes the exact generated texture testable offline.
    public static bool Rasterize(int profile, float width, float height, float softness, int size, byte[] rgba)
    {
        if (!Valid(profile, width, height, softness) || size < 4 || size > 512
            || rgba == null || rgba.Length != size * size * 4) return false;
        int offset = 0;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            byte mask = (byte)Math.Round(Sample(profile, width, height, softness,
                x / (float)(size - 1), y / (float)(size - 1)) * 255);
            rgba[offset++] = mask;
            rgba[offset++] = mask;
            rgba[offset++] = mask;
            rgba[offset++] = 255;
        }
        return true;
    }

    private static double Edge(double radius, double softness)
    {
        if (radius >= 1) return 0;
        if (softness <= 0) return 1;
        return 1 - Smooth(1 - softness, 1, radius);
    }

    private static double Smooth(double low, double high, double value)
    {
        double t = Math.Max(0, Math.Min(1, (value - low) / (high - low)));
        return t * t * (3 - 2 * t);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
