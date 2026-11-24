using System;
using ReplayMod.Events.ConcreteEvents;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Data;

// Managed values own their data independently of the pooled file events.
public sealed class WorldStateSnapshot
{
    public const int MaxPathBytes = 640;
    public double time;
    public float timeOfDay, timeFactor, conditions, cloudHeight, windSpeed, windTurbulence, windHeading, windMainHeading, windRandomArc;
    public Vector3 windVelocity, windOffset;
    public float? moonPhase;
    public bool hasCloudPhase;
    public Vector2 cloudDisplacement;
    public static WorldStateSnapshot From(WorldEnvironmentStateEvent e) => new WorldStateSnapshot {
        time=e.Time,timeOfDay=e.timeOfDay,timeFactor=e.timeFactor,conditions=e.conditions,cloudHeight=e.cloudHeight,
        windSpeed=e.windSpeed,windTurbulence=e.windTurbulence,windHeading=e.windHeading,windMainHeading=e.windMainHeading,windRandomArc=e.windRandomArc,
        windVelocity=e.windVelocity,windOffset=e.windOffset,moonPhase=e.moonPhase,hasCloudPhase=e.hasCloudPhase,cloudDisplacement=e.cloudDisplacement };
    public void CopyTo(WorldEnvironmentStateEvent e)
    {
        e.Time=time;e.timeOfDay=timeOfDay;e.timeFactor=timeFactor;e.conditions=conditions;e.cloudHeight=cloudHeight;
        e.windSpeed=windSpeed;e.windTurbulence=windTurbulence;e.windHeading=windHeading;e.windMainHeading=windMainHeading;e.windRandomArc=windRandomArc;
        e.windVelocity=windVelocity;e.windOffset=windOffset;e.moonPhase=moonPhase;e.hasCloudPhase=hasCloudPhase;e.cloudDisplacement=cloudDisplacement;
    }
    public WorldStateSnapshot Copy() => (WorldStateSnapshot)MemberwiseClone();
    public bool Valid() => Finite(time) && Finite(timeOfDay) && Finite(timeFactor) && Finite(conditions) && Finite(cloudHeight) &&
        Finite(windSpeed) && Finite(windTurbulence) && Finite(windHeading) && Finite(windMainHeading) && Finite(windRandomArc) &&
        Finite(windVelocity.x) && Finite(windVelocity.y) && Finite(windVelocity.z) && Finite(windOffset.x) && Finite(windOffset.y) && Finite(windOffset.z) &&
        (!moonPhase.HasValue || Finite(moonPhase.Value)) && Finite(cloudDisplacement.x) && Finite(cloudDisplacement.y);
    public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static float Lerp(float a,float b,float f) => a+(b-a)*f;
    public static float Cycle(float value,float period) { value%=period;return value<0?value+period:value; }
    private static float LerpCycle(float a,float b,float f,float period)
    { float d=Cycle(b-a+period*.5f,period)-period*.5f;return Cycle(a+d*f,period); }
    public static WorldStateSnapshot At(WorldStateSnapshot a,WorldStateSnapshot b,double at)
    {
        if(a==null||b==null||!Finite(at))throw new ArgumentException("Invalid environment samples");
        if(at>=b.time && b.time>a.time)return b.Copy();
        var result=a.Copy();result.time=at;
        float f=b.time>a.time?(float)Math.Max(0,Math.Min(1,(at-a.time)/(b.time-a.time))):0;
        result.timeOfDay=LerpCycle(a.timeOfDay,b.timeOfDay,f,24);
        result.windHeading=SnapshotHistory.InterpolateAngle(a.windHeading,b.windHeading,f);
        result.windVelocity=Vector3.Lerp(a.windVelocity,b.windVelocity,f);result.windOffset=Vector3.Lerp(a.windOffset,b.windOffset,f);
        // Changes to weather, wind settings or moon availability are discrete;
        // only the continuous clock and actual measured scrolling interpolate.
        if(a.moonPhase.HasValue && b.moonPhase.HasValue)result.moonPhase=LerpCycle(a.moonPhase.Value,b.moonPhase.Value,f,28);
        if(a.hasCloudPhase && b.hasCloudPhase)result.cloudDisplacement=new Vector2(Lerp(a.cloudDisplacement.x,b.cloudDisplacement.x,f),Lerp(a.cloudDisplacement.y,b.cloudDisplacement.y,f));
        if(a.time==b.time && at>a.time)
        {
            // Bounded fallback at file end or starvation; ordinary playback has
            // a measured next sample, including on a multiplayer client.
            float dt=(float)Math.Max(0,Math.Min(.5,at-a.time));
            result.timeOfDay=Cycle(a.timeOfDay+a.timeFactor*dt/3600,24);
            result.windOffset=new Vector3(a.windOffset.x+10*dt,a.windOffset.y+10*dt,a.windOffset.z+10*dt);
            if(a.hasCloudPhase)result.cloudDisplacement=new Vector2(a.cloudDisplacement.x+a.windVelocity.x*dt,a.cloudDisplacement.y+a.windVelocity.z*dt);
        }
        return result;
    }
    public static void SampleAtCut(WorldEnvironmentStateEvent previous,WorldEnvironmentStateEvent next,double cut,WorldEnvironmentStateEvent destination)
    {
        if(previous==null||destination==null)throw new ArgumentNullException();
        At(From(previous),From(next??previous),cut).CopyTo(destination);destination.Time=cut;
    }
}

public sealed class HangarDoorSnapshot
{
    public double time;
    public float openAmount,currentSpeed,blendSeconds;
    public bool opening,available,selfDisabled;
    public static bool Valid(HangarDoorStateEvent e) => WorldStateSnapshot.Finite(e.Time) && WorldStateSnapshot.Finite(e.openAmount) && e.openAmount>=0 && e.openAmount<=1 &&
        WorldStateSnapshot.Finite(e.currentSpeed) && WorldStateSnapshot.Finite(e.blendSeconds) && e.blendSeconds>=0 && e.blendSeconds<=3600 && e.unitId!=0;
    public static HangarDoorSnapshot From(HangarDoorStateEvent e) => new HangarDoorSnapshot {
        time=e.Time,openAmount=e.openAmount,currentSpeed=e.currentSpeed,blendSeconds=e.blendSeconds,opening=e.opening,available=e.available,selfDisabled=e.selfDisabled };
    public void CopyTo(HangarDoorStateEvent e)
    { e.Time=time;e.openAmount=openAmount;e.currentSpeed=currentSpeed;e.blendSeconds=blendSeconds;e.opening=opening;e.available=available;e.selfDisabled=selfDisabled; }
    public HangarDoorSnapshot Copy() => (HangarDoorSnapshot)MemberwiseClone();
    public bool Changed(HangarDoorSnapshot other) => other==null || Math.Abs(openAmount-other.openAmount)>0.00001f || Math.Abs(currentSpeed-other.currentSpeed)>0.0001f ||
        opening!=other.opening || available!=other.available || selfDisabled!=other.selfDisabled;
    public static HangarDoorSnapshot At(HangarDoorSnapshot a,HangarDoorSnapshot b,double at)
    {
        if(a==null||b==null||!WorldStateSnapshot.Finite(at))throw new ArgumentException("Invalid hangar samples");
        if(at>=b.time && b.time>a.time)return b.Copy();
        var result=a.Copy();result.time=at;
        double start=Math.Max(a.time,b.time-b.blendSeconds);
        float f=b.time>start?(float)Math.Max(0,Math.Min(1,(at-start)/(b.time-start))):0;
        result.openAmount=a.openAmount+(b.openAmount-a.openAmount)*f;
        result.currentSpeed=a.currentSpeed+(b.currentSpeed-a.currentSpeed)*f;
        return result;
    }
    public static void SampleAtCut(HangarDoorStateEvent previous,HangarDoorStateEvent next,double cut,HangarDoorStateEvent destination)
    {
        if(previous==null||destination==null)throw new ArgumentNullException();
        if(next!=null&&(next.unitId!=previous.unitId||next.hangarPath!=previous.hangarPath))throw new ArgumentException("Different replay hangars");
        var sample=At(From(previous),From(next??previous),cut);sample.CopyTo(destination);
        destination.Time=cut;destination.unitId=previous.unitId;destination.hangarPath=previous.hangarPath;destination.blendSeconds=0;
    }
}
