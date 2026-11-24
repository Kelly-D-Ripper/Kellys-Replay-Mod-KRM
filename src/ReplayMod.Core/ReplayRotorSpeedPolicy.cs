using System;

namespace ReplayMod.Core;

internal static class ReplayRotorSpeedPolicy
{
    // Native RotorShaft.GetRPM converts radians/second with this factor.
    private const float RpmPerRadianPerSecond=9.55414f;
    internal static bool TryConvert(float rpm,float angularSpeedLimit,out float angularSpeed,out float ratio)
    {
        angularSpeed=0;ratio=0;
        if(float.IsNaN(rpm)||float.IsInfinity(rpm)||rpm<0||float.IsNaN(angularSpeedLimit)||float.IsInfinity(angularSpeedLimit)||angularSpeedLimit<=0)return false;
        float speed=rpm/RpmPerRadianPerSecond,scaled=speed/angularSpeedLimit;
        if(float.IsNaN(speed)||float.IsInfinity(speed)||float.IsNaN(scaled)||float.IsInfinity(scaled))return false;
        angularSpeed=speed;ratio=Math.Max(0f,scaled);return true;
    }
}
