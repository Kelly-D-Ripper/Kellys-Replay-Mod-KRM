using System;

namespace ReplayMod.Core;

// Zero deltas describe an absolute HP snapshot, not a historical impact to
// resimulate. Tiny real impacts remain real impacts; there is no epsilon test.
public static class ReplayDamageStatePolicy
{
    public static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    public static bool Valid(float hitPoints,float pierce,float blast,float fire,float impact)
        =>Finite(hitPoints)&&Finite(pierce)&&Finite(blast)&&Finite(fire)&&Finite(impact);
    public static bool IsAbsoluteSnapshot(float pierce,float blast,float fire,float impact)
        =>pierce==0&&blast==0&&fire==0&&impact==0;
}
