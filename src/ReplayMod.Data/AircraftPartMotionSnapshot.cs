using System;
using System.Collections.Generic;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Data;

[Flags]
public enum AircraftPartFlags : byte { None=0, Present=1, Detached=2, Fragmented=4 }
public struct AircraftPartPose
{
    public byte partId;
    public AircraftPartFlags flags;
    public GlobalPosition position;
    public Quaternion rotation;
}
public sealed class AircraftPartMotionSnapshot
{
    public const int MaxParts=256;
    public double time;
    public float blendSeconds;
    public AircraftPartPose[] parts=Array.Empty<AircraftPartPose>();
    public AircraftPartMotionSnapshot Copy()=>new AircraftPartMotionSnapshot {time=time,blendSeconds=blendSeconds,parts=(AircraftPartPose[])parts.Clone()};
    public static AircraftPartMotionSnapshot From(AircraftPartMotionEvent e)=>new AircraftPartMotionSnapshot {time=e.Time,blendSeconds=e.blendSeconds,parts=(AircraftPartPose[])e.parts.Clone()};
    public void CopyTo(AircraftPartMotionEvent e){e.Time=time;e.blendSeconds=blendSeconds;e.parts=(AircraftPartPose[])parts.Clone();}
    public static bool Valid(AircraftPartPose part)
    {
        var p=part.position;var q=part.rotation;double norm=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
        return ((byte)part.flags&~7)==0&&AircraftVisualSnapshot.Finite(p.x)&&AircraftVisualSnapshot.Finite(p.y)&&AircraftVisualSnapshot.Finite(p.z)&&
            AircraftVisualSnapshot.Finite(q.x)&&AircraftVisualSnapshot.Finite(q.y)&&AircraftVisualSnapshot.Finite(q.z)&&AircraftVisualSnapshot.Finite(q.w)&&norm>.25&&norm<4;
    }
    public static bool SameLayout(AircraftPartMotionSnapshot a,AircraftPartMotionSnapshot b)
    {
        if(a==null||b==null||a.parts.Length!=b.parts.Length)return false;
        for(int i=0;i<a.parts.Length;i++)if(a.parts[i].partId!=b.parts[i].partId)return false;
        return true;
    }
    public bool Changed(AircraftPartMotionSnapshot next)
    {
        if(!SameLayout(this,next))return true;
        for(int i=0;i<parts.Length;i++)
        {
            var a=parts[i];var b=next.parts[i];if(a.flags!=b.flags)return true;
            if((a.flags&AircraftPartFlags.Present)==0)continue;
            double x=a.position.x-b.position.x,y=a.position.y-b.position.y,z=a.position.z-b.position.z;
            double dot=Math.Abs(a.rotation.x*b.rotation.x+a.rotation.y*b.rotation.y+a.rotation.z*b.rotation.z+a.rotation.w*b.rotation.w);
            if(x*x+y*y+z*z>.000004||dot<.999999)return true;
        }
        return false;
    }
    public static AircraftPartMotionSnapshot At(AircraftPartMotionSnapshot previous,AircraftPartMotionSnapshot next,double time)
    {
        if(!SameLayout(previous,next))throw new ArgumentException("Aircraft part layouts do not match");
        if(double.IsNaN(time)||double.IsInfinity(time))throw new ArgumentOutOfRangeException(nameof(time));
        var sample=(time>=next.time?next:previous).Copy();sample.time=time;sample.blendSeconds=0;
        if(time>=next.time)return sample;
        float t=AircraftVisualSnapshot.Fraction(previous.time,next.time,next.blendSeconds,time);
        for(int i=0;i<sample.parts.Length;i++)
        {
            var a=previous.parts[i];var b=next.parts[i];
            // Appearance/disappearance and detachment are discrete events. Do
            // not interpolate an absent part into a newly visible body.
            if((a.flags&AircraftPartFlags.Present)==0||(b.flags&AircraftPartFlags.Present)==0)continue;
            sample.parts[i].position=new GlobalPosition(a.position.x+(b.position.x-a.position.x)*t,a.position.y+(b.position.y-a.position.y)*t,a.position.z+(b.position.z-a.position.z)*t);
            sample.parts[i].rotation=Quaternion.Slerp(a.rotation,b.rotation,t);
        }
        return sample;
    }
}
public sealed class AircraftPartMotionTimeline
{
    private readonly List<AircraftPartMotionSnapshot> samples=new List<AircraftPartMotionSnapshot>();
    private static readonly Func<AircraftPartMotionSnapshot,double> TimeOf=s=>s.time;
    public void Add(AircraftPartMotionEvent e)=>SnapshotHistory.Insert(samples,AircraftPartMotionSnapshot.From(e),TimeOf);
    public bool Locate(double time,out AircraftPartMotionSnapshot previous,out AircraftPartMotionSnapshot next,out float fraction)
    {
        previous=next=null;fraction=0;
        if(!SnapshotHistory.Locate(samples,time,TimeOf,out int a,out int b,out _))return false;
        previous=samples[a];next=samples[b];
        fraction=a==b||!AircraftPartMotionSnapshot.SameLayout(previous,next)?0:AircraftVisualSnapshot.Fraction(previous.time,next.time,next.blendSeconds,time);
        SnapshotHistory.Trim(samples,a,8);return true;
    }
}
public sealed class ReplayPartDetachTracker
{
    private readonly Dictionary<uint,HashSet<byte>> detached=new Dictionary<uint,HashSet<byte>>();
    public bool Observe(uint unit,byte part)
    {if(!detached.TryGetValue(unit,out var parts))detached[unit]=parts=new HashSet<byte>();return parts.Add(part);}
    public void Forget(uint unit)=>detached.Remove(unit);
    public void Clear()=>detached.Clear();
}
