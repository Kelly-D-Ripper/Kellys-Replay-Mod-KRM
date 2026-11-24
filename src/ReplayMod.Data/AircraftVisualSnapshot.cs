using System;
using System.Collections.Generic;
using ReplayMod.Events.ConcreteEvents;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Data;

public struct AircraftVisualPose
{
    public Vector3 position;
    public Quaternion rotation;
}

// Independent managed snapshots, never references to pooled file events.
public sealed class AircraftVisualSnapshot
{
    public const int MaxChannels = 256;
    public double time;
    public uint layoutId;
    public bool gearDeployed;
    public byte gearState;
    public float blendSeconds;
    public AircraftVisualPose[] poses = Array.Empty<AircraftVisualPose>();

    public AircraftVisualSnapshot Copy(double at)
    {
        return new AircraftVisualSnapshot { time=at,layoutId=layoutId,gearDeployed=gearDeployed,
            gearState=gearState,blendSeconds=blendSeconds,poses=(AircraftVisualPose[])poses.Clone() };
    }
    public static AircraftVisualSnapshot From(AircraftVisualStateEvent e) => new AircraftVisualSnapshot {
        time=e.Time,layoutId=e.layoutId,gearDeployed=e.gearDeployed,gearState=e.gearState,blendSeconds=e.blendSeconds,
        poses=(AircraftVisualPose[])e.poses.Clone() };
    public void CopyTo(AircraftVisualStateEvent e)
    {
        e.Time=time;e.layoutId=layoutId;e.gearDeployed=gearDeployed;e.gearState=gearState;e.blendSeconds=blendSeconds;
        e.poses=(AircraftVisualPose[])poses.Clone();
    }
    public static float Fraction(double previous,double next,float blendSeconds,double time)
    {
        double start=Math.Max(previous,next-blendSeconds);
        return next>start?(float)Math.Max(0,Math.Min(1,(time-start)/(next-start))):0;
    }
    public static AircraftVisualSnapshot At(AircraftVisualSnapshot previous,AircraftVisualSnapshot next,double time)
    {
        if(previous==null||next==null)throw new ArgumentNullException();
        if(previous.layoutId!=next.layoutId||previous.poses.Length!=next.poses.Length)throw new ArgumentException("Aircraft visual layouts do not match");
        if(double.IsNaN(time)||double.IsInfinity(time))throw new ArgumentOutOfRangeException(nameof(time));
        if(time>=next.time)return next.Copy(time);
        float fraction=Fraction(previous.time,next.time,next.blendSeconds,time);
        var result=previous.Copy(time);result.blendSeconds=0;
        for(int i=0;i<result.poses.Length;i++)result.poses[i]=new AircraftVisualPose {
            position=Vector3.Lerp(previous.poses[i].position,next.poses[i].position,fraction),
            rotation=Quaternion.Slerp(previous.poses[i].rotation,next.poses[i].rotation,fraction)};
        return result;
    }
    public bool Changed(AircraftVisualSnapshot next)
    {
        if(next==null || layoutId!=next.layoutId || gearDeployed!=next.gearDeployed || gearState!=next.gearState || poses.Length!=next.poses.Length)return true;
        for(int i=0;i<poses.Length;i++)
        {
            var a=poses[i];var b=next.poses[i];
            double dx=a.position.x-b.position.x,dy=a.position.y-b.position.y,dz=a.position.z-b.position.z;
            double dot=Math.Abs(a.rotation.x*b.rotation.x+a.rotation.y*b.rotation.y+a.rotation.z*b.rotation.z+a.rotation.w*b.rotation.w);
            if(dx*dx+dy*dy+dz*dz>0.000004 || dot<0.999999)return true;
        }
        return false;
    }
    public static bool Finite(float value) => !float.IsNaN(value)&&!float.IsInfinity(value);
    public static bool Valid(AircraftVisualPose pose)
    {
        var p=pose.position;var q=pose.rotation;
        double norm=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
        return Finite(p.x)&&Finite(p.y)&&Finite(p.z)&&Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w)&&norm>0.25&&norm<4;
    }
}

public sealed class AircraftVisualTimeline
{
    private readonly List<AircraftVisualSnapshot> samples = new List<AircraftVisualSnapshot>();
    public int Count=>samples.Count;
    private static readonly Func<AircraftVisualSnapshot,double> TimeOf = s=>s.time;
    public void Add(AircraftVisualStateEvent e) => SnapshotHistory.Insert(samples,AircraftVisualSnapshot.From(e),TimeOf);
    public void Clear()=>samples.Clear();
    public void RetainLayout(uint layout,int channels)=>samples.RemoveAll(s=>s.layoutId!=layout||s.poses.Length!=channels);
    public bool Locate(double time,out AircraftVisualSnapshot previous,out AircraftVisualSnapshot next,out float fraction)
    {
        previous=next=null;fraction=0;
        if(!SnapshotHistory.Locate(samples,time,TimeOf,out int a,out int b,out fraction))return false;
        previous=samples[a];next=samples[b];
        if(a!=b)
        {
            fraction=AircraftVisualSnapshot.Fraction(previous.time,next.time,next.blendSeconds,time);
        }
        SnapshotHistory.Trim(samples,a,8);return true;
    }
}

public sealed class AircraftVisualChangeTracker
{
    private AircraftVisualSnapshot published;
    private double observedTime;
    public void Observe(AircraftVisualSnapshot current,Action<AircraftVisualSnapshot> publish)
    {
        if(current==null||publish==null)throw new ArgumentNullException();
        if(published==null||published.Changed(current))
        {
            // Store a duration on the current event, never enqueue a retroactive
            // hold event: every recording remains globally ordered by time.
            current.blendSeconds=published==null?0:(float)Math.Max(0,Math.Min(3600,current.time-observedTime));
            publish(current.Copy(current.time));published=current.Copy(current.time);
        }
        observedTime=current.time;
    }
}
