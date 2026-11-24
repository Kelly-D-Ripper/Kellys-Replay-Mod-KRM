using System;

namespace ReplayMod.Director;

// Smooth the world-oriented camera-minus-subject vector, never an absolute
// camera position. The subject's current translation remains the exact anchor,
// while turns/banks ease the desired offset, independent of a floating origin.
public sealed class CameraRelativeTracking
{
    public double X {get;private set;}
    public double Y {get;private set;}
    public double Z {get;private set;}
    private double previousTime=double.NaN;
    public void Reset()=>previousTime=double.NaN;
    public bool Update(double x,double y,double z,double time,double rate,bool reset=false)
    {
        if(!Finite(x)||!Finite(y)||!Finite(z)||!Finite(time)||!Finite(rate)||rate<0)return false;
        bool snap=reset||!Finite(previousTime)||time<previousTime||time-previousTime>.5;
        double blend=snap?1:Blend(rate,time-previousTime);
        X+=(x-X)*blend;Y+=(y-Y)*blend;Z+=(z-Z)*blend;previousTime=time;
        return true;
    }
    public static double Blend(double rate,double delta)
    {
        if(!Finite(rate)||!Finite(delta)||rate<0||delta<0)return 0;
        return rate==0?1:1-Math.Exp(-rate*delta);
    }
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
}

public enum CameraSubjectLoss { NoPose, Hold, Release }
public sealed class CameraSubjectHold
{
    private bool observed;
    private double missingSince=double.NaN;
    public void Observe(){observed=true;missingSince=double.NaN;}
    public void Reset(){observed=false;missingSince=double.NaN;}
    public CameraSubjectLoss Missing(double time,double seconds)
    {
        if(!observed||!Finite(time)||!Finite(seconds)||seconds<0)return CameraSubjectLoss.NoPose;
        if(!Finite(missingSince)||time<missingSince)missingSince=time;
        return time-missingSince<seconds?CameraSubjectLoss.Hold:CameraSubjectLoss.Release;
    }
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
}

public static class CameraSubjectFraming
{
    // Fit a conservative sphere in both image dimensions. Never guess bounds
    // for a missing/invalid subject or a camera already inside that sphere.
    public static bool TryFov(double radius,double distance,double aspect,out float fov,double aimErrorDegrees=0)
    {
        fov=60;
        if(!Finite(radius)||!Finite(distance)||!Finite(aspect)||!Finite(aimErrorDegrees)||aimErrorDegrees<0||radius<=0||radius>10000||aspect<=0)return false;
        double marginRadius=radius*1.15;
        if(distance<=marginRadius)return false;
        double cone=Math.Asin(marginRadius/distance)+aimErrorDegrees*Math.PI/180;
        if(cone>=Math.PI/2)return false;
        double angle=2*Math.Atan(Math.Tan(cone)/Math.Min(1,aspect))*180/Math.PI;
        if(!Finite(angle)||angle>120)return false;
        fov=(float)Math.Max(10,angle);return true;
    }
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
}
