using System;
using ReplayMod.Core;

namespace ReplayMod.Director;

public readonly struct CameraEventShotPlan
{
    public readonly ReplayActorLifetime Lifetime;
    public readonly double EventTime,StartTime;
    public readonly int Rig;
    private CameraEventShotPlan(ReplayActorLifetime lifetime,double time,double start,int rig)
    {Lifetime=lifetime;EventTime=time;StartTime=start;Rig=rig;}
    public static bool TryCreate(ReplayActorLifetime lifetime,double time,double leadIn,double duration,int rig,out CameraEventShotPlan plan)
    {
        plan=default;
        if(lifetime.Actor==0||lifetime.Generation<=0||!Finite(time)||!Finite(leadIn)||!Finite(duration)||duration<0||
            time<0||time>duration||time<lifetime.BirthTime||time>lifetime.DeathTime||rig<1||rig>4)return false;
        plan=new CameraEventShotPlan(lifetime,time,Math.Max(0,time-Math.Max(0,Math.Min(10,leadIn))),rig);return true;
    }
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
}

public enum CameraEventSubjectAction { None, Follow, Unavailable }
public sealed class CameraEventShot
{
    private long revision;
    public bool Active {get;private set;}
    public bool Preparing {get;private set;}
    public bool Following {get;private set;}
    public CameraEventShotPlan Plan {get;private set;}
    public long Begin(CameraEventShotPlan plan){revision++;Plan=plan;Active=Preparing=true;Following=false;return revision;}
    public bool IsCurrent(long request)=>Active&&request==revision;
    public bool Prepared(long request){if(!IsCurrent(request))return false;Preparing=false;return true;}
    public CameraEventSubjectAction Evaluate(double time,long currentGeneration)
    {
        if(!Active||Preparing||Following)return CameraEventSubjectAction.None;
        if(currentGeneration==Plan.Lifetime.Generation)
        {Following=true;return CameraEventSubjectAction.Follow;}
        if(time>Plan.EventTime+.5||time>Plan.Lifetime.DeathTime)
        {Cancel();return CameraEventSubjectAction.Unavailable;}
        return CameraEventSubjectAction.None;
    }
    public void Cancel(){revision++;Active=Preparing=Following=false;}
}
