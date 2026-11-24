using System;

namespace ReplayMod.Core;

// One replay camera at a time. A native camera-mode change becomes the new
// baseline; cleanup never overwrites a later change made by the game or a mod.
public sealed class ReplayCloseCameraState<T> where T : class
{
    private T owner;
    private float original, applied;
    public bool HasOverride => owner != null;

    public void Ensure(T target, float maximumNearPlane, Func<T, bool> alive,
        Func<T, float> read, Action<T, float> write)
    {
        if (!ReferenceEquals(owner, target)) Clear(alive, read, write);
        if (target == null || !alive(target)) return;
        float current = read(target);
        if (!FinitePositive(current) || !FinitePositive(maximumNearPlane))
        { Clear(alive, read, write); return; }
        // Native EnterState may select a different near plane while this option
        // is enabled. Keep that value for switching the option off later.
        float baseline = ReferenceEquals(owner, target) && current.Equals(applied) ? original : current;
        float desired = Math.Min(baseline, maximumNearPlane);
        if (desired.Equals(current))
        {
            if (baseline.Equals(current)) { owner = null; original = applied = 0; }
            return;
        }
        owner = target; original = baseline; applied = desired;
        try { write(target, desired); }
        catch { Clear(alive, read, write); throw; }
    }

    public int Clear(Func<T, bool> alive, Func<T, float> read, Action<T, float> write)
    {
        int failures = 0;
        try
        {
            if (owner != null && alive(owner) && read(owner).Equals(applied))
                write(owner, original);
        }
        catch (Exception) { failures++; }
        finally { owner = null; original = applied = 0; }
        return failures;
    }

    private static bool FinitePositive(float value)
        => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
}
