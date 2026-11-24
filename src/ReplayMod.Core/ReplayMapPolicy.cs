using System;

namespace ReplayMod.Core;

// Presentation policy only: replay actors remain owned even when the native
// radar registry or map filters intentionally omit their icons.
public static class ReplayMapPolicy
{
    public const int RefreshBudget = 64;
    public const double RefreshInterval = .05;
    public static bool Visible(bool exactLifetime, bool disabled, bool hiddenBuilding, bool terminalMissile, float iconSize)
        => exactLifetime && !disabled && !hiddenBuilding && !terminalMissile &&
            !float.IsNaN(iconSize) && !float.IsInfinity(iconSize) && iconSize > 0;
}
