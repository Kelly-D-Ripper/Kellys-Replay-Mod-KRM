using System;
using System.Collections.Generic;
using System.IO;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Data;

public struct FuelFlameSnapshot
{
    public double time;
    public bool attached;
    public GlobalPosition position;
    public Quaternion rotation,localRotation;
    public Vector3 localPosition;
    public float age,speed,lightIntensity,blendSeconds;
    public uint emittingMask;
    public float[] opacity;
    public FuelFlameSnapshot Copy(){var copy=this;copy.opacity=opacity==null?Array.Empty<float>():(float[])opacity.Clone();return copy;}
}

public static class FuelFlameWarmPolicy
{
    public static float Duration(FuelFlameSnapshot state,int index)
    {
        if(state.opacity==null||index<0||index>=state.opacity.Length||index>=32||(state.emittingMask&(1u<<index))==0||state.opacity[index]<=0||!CountermeasureTimeline.Finite(state.age)||state.age<=0)return 0;
        return Math.Min(5,state.age);
    }
}

public sealed class FuelFlameTimeline
{
    public const int MaxActive=256,MaxSystems=32,MaxSamples=128;
    private readonly List<FuelFlameSnapshot> samples=new List<FuelFlameSnapshot>();
    private static readonly Func<FuelFlameSnapshot,double> TimeOf=value=>value.time;
    public readonly FuelFlameStateEvent metadata;
    public double EndTime{get;private set;}=double.PositiveInfinity;
    public FuelFlameSnapshot Latest=>samples[samples.Count-1];
    public FuelFlameTimeline(FuelFlameStateEvent begin)
    {
        if(begin.phase!=FuelFlamePhase.Begin||!begin.HasValidPayload())throw new ArgumentException("Fuel fire needs a valid beginning");
        metadata=new FuelFlameStateEvent();begin.CopyTo(metadata);samples.Add(begin.snapshot.Copy());
    }
    public void Add(FuelFlameStateEvent value)
    {
        if(value.flameId!=metadata.flameId||!value.HasValidPayload())return;
        if(value.phase==FuelFlamePhase.End){EndTime=Math.Min(EndTime,value.Time);return;}
        SnapshotHistory.Insert(samples,value.snapshot.Copy(),TimeOf);
        if(samples.Count>MaxSamples)throw new InvalidDataException("Fuel fire lookahead history exceeds limit");
    }
    public bool TryAt(double time,out FuelFlameSnapshot result)
    {
        result=default;if(time>=EndTime||!SnapshotHistory.Locate(samples,time,TimeOf,out int a,out int b,out _))return false;
        result=At(samples[a],samples[b],time);SnapshotHistory.Trim(samples,a,8);return true;
    }
    public static FuelFlameSnapshot At(FuelFlameSnapshot a,FuelFlameSnapshot b,double time)
    {
        var result=(time>=b.time?b:a).Copy();result.time=time;
        if(a.time==b.time||time>=b.time){result.age+=(float)Math.Max(0,time-(time>=b.time?b.time:a.time));return result;}
        result.age=a.age+(float)Math.Max(0,time-a.time);
        if(a.opacity.Length!=b.opacity.Length||a.attached!=b.attached)return result;
        float t=AircraftVisualSnapshot.Fraction(a.time,b.time,b.blendSeconds,time);
        result.position=new GlobalPosition(Lerp(a.position.x,b.position.x,t),Lerp(a.position.y,b.position.y,t),Lerp(a.position.z,b.position.z,t));result.rotation=Quaternion.Slerp(a.rotation,b.rotation,t);
        result.localPosition=Vector3.Lerp(a.localPosition,b.localPosition,t);result.localRotation=Quaternion.Slerp(a.localRotation,b.localRotation,t);
        result.speed=Lerp(a.speed,b.speed,t);result.lightIntensity=Lerp(a.lightIntensity,b.lightIntensity,t);
        for(int i=0;i<result.opacity.Length;i++)result.opacity[i]=Lerp(a.opacity[i],b.opacity[i],t);
        return result;
    }
    private static float Lerp(float a,float b,float t)=>a+(b-a)*t;
    public FuelFlameStateEvent SnapshotEvent(){var value=new FuelFlameStateEvent();metadata.CopyTo(value);value.snapshot=Latest.Copy();return value;}
    public static bool Changed(FuelFlameSnapshot a,FuelFlameSnapshot b)
    {
        if(a.attached!=b.attached||a.emittingMask!=b.emittingMask||a.opacity.Length!=b.opacity.Length||Math.Abs(a.speed-b.speed)>.05||Math.Abs(a.lightIntensity-b.lightIntensity)>.005)return true;
        if(Squared(a.position.x-b.position.x,a.position.y-b.position.y,a.position.z-b.position.z)>.000004||Squared(a.localPosition.x-b.localPosition.x,a.localPosition.y-b.localPosition.y,a.localPosition.z-b.localPosition.z)>.000004||
            Math.Abs(Dot(a.rotation,b.rotation))<.999999||Math.Abs(Dot(a.localRotation,b.localRotation))<.999999)return true;
        for(int i=0;i<a.opacity.Length;i++)if(a.opacity[i]!=b.opacity[i])return true;return false;
    }
    private static double Squared(float x,float y,float z)=>(double)x*x+(double)y*y+(double)z*z;
    private static double Dot(Quaternion a,Quaternion b)=>(double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z+(double)a.w*b.w;
    private static bool QuaternionValid(Quaternion q){double norm=Dot(q,q);return CountermeasureTimeline.Finite(norm)&&norm>.000001&&norm<4;}
    public static bool Valid(FuelFlameSnapshot value)
    {
        if(!CountermeasureTimeline.Finite(value.time)||!CountermeasureTimeline.Finite(value.position.x)||!CountermeasureTimeline.Finite(value.position.y)||!CountermeasureTimeline.Finite(value.position.z)||
            !CountermeasureTimeline.Finite(value.localPosition.x)||!CountermeasureTimeline.Finite(value.localPosition.y)||!CountermeasureTimeline.Finite(value.localPosition.z)||!QuaternionValid(value.rotation)||!QuaternionValid(value.localRotation)||
            !CountermeasureTimeline.Finite(value.age)||value.age<0||value.age>604800||!CountermeasureTimeline.Finite(value.speed)||value.speed<0||value.speed>100000||!CountermeasureTimeline.Finite(value.lightIntensity)||value.lightIntensity<0||value.lightIntensity>1000000||
            !CountermeasureTimeline.Finite(value.blendSeconds)||value.blendSeconds<0||value.blendSeconds>3600||value.opacity==null||value.opacity.Length>MaxSystems)return false;
        if(value.opacity.Length<32&&(value.emittingMask>>value.opacity.Length)!=0)return false;
        foreach(float opacity in value.opacity)if(!CountermeasureTimeline.Finite(opacity)||opacity < -2||opacity>1.01)return false;return true;
    }
}
