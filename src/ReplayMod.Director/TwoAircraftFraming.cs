using System;

namespace ReplayMod.Director;

// A sphere around the pair fits from any chosen viewing direction. Use the
// narrower image dimension so a portrait viewport does not crop either wing.
public static class TwoAircraftFraming
{
    public static bool TryDistance(double separation, double radiusA, double radiusB,
        double fov, double aspect, double minimumDistance, double farClip,
        out double distance, out double radius)
    {
        distance = radius = 0;
        if (!Finite(separation) || !Finite(radiusA) || !Finite(radiusB) ||
            !Finite(fov) || !Finite(aspect) || !Finite(minimumDistance) || !Finite(farClip) ||
            separation < 0 || radiusA < 0 || radiusB < 0 || fov <= 0 || fov >= 180 ||
            aspect <= 0 || minimumDistance < 0 || farClip <= 0) return false;

        double paddedRadius = (separation * .5 + Math.Max(radiusA, radiusB)) * 1.15;
        double verticalHalfAngle = fov * (Math.PI / 360);
        double horizontalHalfAngle = Math.Atan(Math.Tan(verticalHalfAngle) * aspect);
        double sine = Math.Sin(Math.Min(verticalHalfAngle, horizontalHalfAngle));
        if (!Finite(paddedRadius) || sine <= 0) return false;

        // Tangency to the sphere uses sin, rather than the planar tan formula.
        double required = Math.Max(minimumDistance, paddedRadius / sine);
        if (!Finite(required) || required <= 0 || required + paddedRadius >= farClip) return false;
        distance = required;
        radius = paddedRadius;
        return true;
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
