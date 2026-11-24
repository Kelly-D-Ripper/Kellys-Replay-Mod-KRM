using System;

namespace ReplayMod.UI;

// A slider can cover a bright outdoor fill and a subtle close-up light without
// spending nearly its entire travel on unusably small values at one end.
public static class StudioLightControlMath
{
    public static float ToSlider(float value,float minimum,float maximum)
    {
        RequireRange(minimum,maximum);
        if(float.IsNaN(value)||float.IsInfinity(value))value=minimum;
        value=Math.Max(minimum,Math.Min(maximum,value));
        return (float)(Math.Log(1+value-minimum)/Math.Log(1+maximum-minimum));
    }
    public static float FromSlider(float position,float minimum,float maximum)
    {
        RequireRange(minimum,maximum);
        if(float.IsNaN(position)||float.IsInfinity(position))position=0;
        position=Math.Max(0,Math.Min(1,position));
        return (float)Math.Max(minimum,Math.Min(maximum,minimum+Math.Exp(position*Math.Log(1+maximum-minimum))-1));
    }
    public static float BeamWidth(float range,float angle)
    {
        if(float.IsNaN(range)||float.IsInfinity(range)||range<0||float.IsNaN(angle)||float.IsInfinity(angle)||angle<0||angle>=180)return 0;
        return (float)(2*range*Math.Tan(angle*Math.PI/360));
    }
    private static void RequireRange(float minimum,float maximum)
    {
        if(float.IsNaN(minimum)||float.IsInfinity(minimum)||float.IsNaN(maximum)||float.IsInfinity(maximum)||maximum<=minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum));
    }
}
