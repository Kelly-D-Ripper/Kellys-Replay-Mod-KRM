using System;

namespace ReplayMod.Director;

public static class CameraOrientation
{
    // Projecting forward onto the horizon keeps its compass direction even if
    // the camera is rolled. A vertical view has no heading, so retain its yaw.
    public static float FlatYaw(double forwardX,double forwardZ,double fallbackYaw)
    {
        double yaw=Finite(forwardX)&&Finite(forwardZ)&&forwardX*forwardX+forwardZ*forwardZ>1e-10
            ? Math.Atan2(forwardX,forwardZ)*180/Math.PI
            : Finite(fallbackYaw)?fallbackYaw:0;
        yaw%=360;if(yaw<0)yaw+=360;return (float)yaw;
    }
    public static float Lens(float value,float fallback=60)
    {
        if(float.IsNaN(value)||float.IsInfinity(value))value=Finite(fallback)?fallback:60;
        return Math.Max(1,Math.Min(120,value));
    }
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
}
