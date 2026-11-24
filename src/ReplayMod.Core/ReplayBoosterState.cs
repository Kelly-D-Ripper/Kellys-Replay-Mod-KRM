using System;
namespace ReplayMod.Core;

public enum ReplayBoosterTransition { None, Activate, Separate }

// Booster fuel is not stored, but attachment and missile age are. Use the native
// prefab's initial ignition delay; recorded attachment alone owns separation.
public sealed class ReplayBoosterState
{
    private readonly double activationDelay;
    private bool activated, separated;
    public ReplayBoosterState(double delay, bool alreadyActivated = false)
    {
        activationDelay = double.IsNaN(delay) || double.IsInfinity(delay) ? 0 : Math.Max(0, delay);
        activated = alreadyActivated;
    }
    public ReplayBoosterTransition Apply(bool attached, double missileAge)
    {
        if (separated) return ReplayBoosterTransition.None;
        if (!attached) { separated = true; return ReplayBoosterTransition.Separate; }
        if (!activated && !double.IsNaN(missileAge) && missileAge >= activationDelay)
        { activated = true; return ReplayBoosterTransition.Activate; }
        return ReplayBoosterTransition.None;
    }
}
