using System;

namespace ReplayMod.Core;

// Replay seconds, independent of wall clock and the native scene's reset clock.
public sealed class ReplayGunBurstState
{
    public bool HasShot { get; private set; }
    public double LastShot { get; private set; }
    public bool Record(double time, bool coalesceSameTime)
    {
        if (double.IsNaN(time) || double.IsInfinity(time) || time < 0) return false;
        if (HasShot && (time < LastShot || coalesceSameTime && time == LastShot)) return false;
        LastShot = time; HasShot = true; return true;
    }
    public double Age(double time) => HasShot ? Math.Max(0, time - LastShot) : double.PositiveInfinity;
    // The native sound methods normally run in FixedUpdate; preserve that
    // tolerance when replay invokes them from the rendered presentation pass.
    public static double NativeAge(double age, float fixedDelta, float frameDelta) => Math.Max(0, age - Math.Max(0, fixedDelta - frameDelta));
}
