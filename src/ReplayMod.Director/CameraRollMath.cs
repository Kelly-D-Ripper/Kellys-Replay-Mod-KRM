using System;

namespace ReplayMod.Director;

public static class CameraRollMath
{
    public const float DegreesPerSecond=45;
    public static float Normalize(float degrees)
    {
        if(float.IsNaN(degrees)||float.IsInfinity(degrees))return 0;
        degrees%=360;
        return degrees>180?degrees-360:degrees< -180?degrees+360:degrees;
    }
    public static float Add(float angle,float delta)
    {
        if(float.IsNaN(delta)||float.IsInfinity(delta))return Normalize(angle);
        return Normalize(angle+delta);
    }
    public static float Advance(float angle,float input,float realSeconds)
    {
        if(float.IsNaN(input)||float.IsInfinity(input)||float.IsNaN(realSeconds)||float.IsInfinity(realSeconds)||realSeconds<=0)return Normalize(angle);
        return Add(angle,Math.Max(-1,Math.Min(1,input))*DegreesPerSecond*realSeconds);
    }
}
