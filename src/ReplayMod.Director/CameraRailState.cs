using System;

namespace ReplayMod.Director;

public enum RailDirection { Forward, Backward, Left, Right, Up, Down }

// Global coordinates are independent of Unity's floating-origin local position.
// Straight shots retain their captured direction. Follow-view shots can update
// that direction without replacing the position or changing their speed.
public sealed class CameraRailState
{
    public bool Active { get; private set; }
    public float Speed { get; private set; } = 5;
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Z { get; private set; }
    public double DirectionX { get; private set; }
    public double DirectionY { get; private set; }
    public double DirectionZ { get; private set; }

    public void SetSpeed(float speed)
    {
        if (!Finite(speed)) return;
        Speed = Math.Max(.1f, Math.Min(100, speed));
    }
    public bool Start(double x, double y, double z, double dx, double dy, double dz)
    {
        Stop();
        if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(dx) || !Finite(dy) || !Finite(dz)) return false;
        if (!SetDirection(dx,dy,dz)) return false;
        X=x; Y=y; Z=z;
        Active=true; return true;
    }
    public bool SetDirection(double dx,double dy,double dz)
    {
        if(!Finite(dx)||!Finite(dy)||!Finite(dz))return false;
        double length=Math.Sqrt(dx*dx+dy*dy+dz*dz);
        if(!Finite(length)||length<.000001)return false;
        DirectionX=dx/length;DirectionY=dy/length;DirectionZ=dz/length;return true;
    }
    public void Advance(double realSeconds)
        =>AdvanceAtSpeed(realSeconds,Speed);
    public void AdvanceAtSpeed(double realSeconds,double metresPerSecond)
    {
        if (!Active || !Finite(realSeconds) || realSeconds <= 0 || !Finite(metresPerSecond) || metresPerSecond<0) return;
        double distance=metresPerSecond*realSeconds;
        double x=X+DirectionX*distance, y=Y+DirectionY*distance, z=Z+DirectionZ*distance;
        if(!Finite(x) || !Finite(y) || !Finite(z)){Stop();return;}
        X=x; Y=y; Z=z;
    }
    // Native Datum adds its origin offset to global coordinates. Perform that
    // cancellation in double precision before converting the small local value.
    public float LocalX(double originX) => (float)(X+originX);
    public float LocalY(double originY) => (float)(Y+originY);
    public float LocalZ(double originZ) => (float)(Z+originZ);
    public void Stop() => Active=false;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

public static class RailSpeedMatch
{
    public static double EffectiveSpeed(double recordedSpeed,double replaySeconds,double realSeconds)
    {
        if(!DirectorProject.Finite(recordedSpeed)||!DirectorProject.Finite(replaySeconds)||!DirectorProject.Finite(realSeconds)||recordedSpeed<0||replaySeconds<=0||realSeconds<=0)return 0;
        double value=recordedSpeed*replaySeconds/realSeconds;
        return DirectorProject.Finite(value)?value:0;
    }
}
