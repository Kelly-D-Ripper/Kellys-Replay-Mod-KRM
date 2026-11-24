using System;
using System.Collections.Generic;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Data;

public struct CountermeasureSnapshot
{
    public double time;
    public GlobalPosition position;
    public Quaternion rotation;
    public Vector3 velocity;
    public float age,remaining;
}

// Trajectories use independent IDs: a flare remains visible after its aircraft
// dies, and a future aircraft with the same native ID cannot inherit its state.
public sealed class CountermeasureTimeline
{
    public const int MaxActive=512,MaxHistory=320,MaxPrefabSlots=1024;
    public const float TrailSeconds=15,MaxAge=3600,MaxBurn=300;
    private readonly List<CountermeasureSnapshot> samples=new List<CountermeasureSnapshot>();
    private readonly bool retainLookAhead;
    public readonly CountermeasureEvent metadata;
    public float EndAge {get;private set;}=-1;
    public double EndTime {get;private set;}=double.PositiveInfinity;
    public double FirstTime=>samples.Count==0?metadata.Time:samples[0].time;
    public CountermeasureSnapshot Latest=>samples[samples.Count-1];
    public int SampleCount=>samples.Count;
    public double ExpiresAt=>EndAge>=0?EndTime+metadata.tailSeconds:Latest.time+Latest.remaining+metadata.tailSeconds;
    public CountermeasureTimeline(CountermeasureEvent launch,bool retainLookAhead=false)
    {
        if(launch.phase!=CountermeasurePhase.Launch||!launch.HasValidPayload())throw new ArgumentException("A countermeasure trajectory needs a valid launch");
        this.retainLookAhead=retainLookAhead;metadata=new CountermeasureEvent();launch.CopyTo(metadata);metadata.history=Array.Empty<CountermeasureSnapshot>();
        foreach(var sample in launch.history)Insert(sample);
        Insert(launch.Snapshot);SetEnd(launch);
    }
    public void Add(CountermeasureEvent value)
    {
        if(value.countermeasureId!=metadata.countermeasureId||value.phase==CountermeasurePhase.Launch||!value.HasValidPayload())return;
        Insert(value.Snapshot);SetEnd(value);
    }
    private void SetEnd(CountermeasureEvent value)
    {
        if(value.endAge<0)return;
        double end=value.Time-(value.age-value.endAge);
        if(end<EndTime){EndTime=end;EndAge=value.endAge;}
    }
    private void Insert(CountermeasureSnapshot value)
    {
        int index=samples.BinarySearch(value,TimeComparer.Instance);
        if(index>=0)samples[index]=value;else samples.Insert(~index,value);
        double cut=samples[samples.Count-1].time-TrailSeconds-(retainLookAhead?PlaybackReadiness.LookAheadSeconds:0);
        int remove=0;while(remove+1<samples.Count&&samples[remove+1].time<cut)remove++;
        if(remove>0)samples.RemoveRange(0,remove);
        while(samples.Count>1&&samples[0].time<cut-.2)samples.RemoveAt(0);
        int limit=MaxHistory+(retainLookAhead?(int)(PlaybackReadiness.LookAheadSeconds*20)+10:0);
        if(samples.Count>limit)samples.RemoveRange(0,samples.Count-limit);
    }
    private sealed class TimeComparer:IComparer<CountermeasureSnapshot>
    {public static readonly TimeComparer Instance=new TimeComparer();public int Compare(CountermeasureSnapshot a,CountermeasureSnapshot b)=>a.time.CompareTo(b.time);}
    public bool TryAt(double time,out CountermeasureSnapshot result)
    {
        result=default;if(samples.Count==0||time<samples[0].time-.00001||time>=ExpiresAt)return false;
        int left=0,right=samples.Count;
        while(left<right){int mid=left+(right-left)/2;if(samples[mid].time<=time)left=mid+1;else right=mid;}
        int previous=Math.Max(0,left-1);result=At(samples[previous],left<samples.Count?samples[left]:samples[previous],time);return true;
    }
    public bool Burning(double time)=>time<EndTime&&TryAt(time,out var value)&&value.remaining>0;
    public static CountermeasureSnapshot At(CountermeasureSnapshot previous,CountermeasureSnapshot next,double time)
    {
        if(next.time>previous.time&&time<next.time)
        {
            float t=(float)Math.Max(0,Math.Min(1,(time-previous.time)/(next.time-previous.time)));
            return new CountermeasureSnapshot {time=time,position=new GlobalPosition(Lerp(previous.position.x,next.position.x,t),Lerp(previous.position.y,next.position.y,t),Lerp(previous.position.z,next.position.z,t)),
                rotation=Quaternion.Slerp(previous.rotation,next.rotation,t),velocity=Vector3.Lerp(previous.velocity,next.velocity,t),age=Lerp(previous.age,next.age,t),remaining=Lerp(previous.remaining,next.remaining,t)};
        }
        var value=time>=next.time?next:previous;float elapsed=(float)Math.Max(0,time-value.time);
        value.time=time;value.age+=elapsed;value.remaining=Math.Max(0,value.remaining-elapsed);return value;
    }
    private static float Lerp(float a,float b,float t)=>a+(b-a)*t;
    public CountermeasureEvent SnapshotEvent()
    {
        var result=new CountermeasureEvent();metadata.CopyTo(result);result.SetSnapshot(Latest);result.endAge=EndAge;
        int start=0;while(start<samples.Count-1&&samples[start].time<Latest.time-TrailSeconds-.1)start++;
        start=Math.Max(start,samples.Count-MaxHistory);result.history=samples.GetRange(start,samples.Count-start).ToArray();return result;
    }
    public CountermeasureTimeline Clone()=>new CountermeasureTimeline(SnapshotEvent(),retainLookAhead);
    // A rolling clip stores a launch snapshot at time zero with a short history
    // encoded as relative offsets, preserving smoke emitted before its cut.
    public static void SampleAtCut(CountermeasureEvent baseline,CountermeasureEvent next,double cut)
    {
        if(baseline.phase!=CountermeasurePhase.Launch||cut<baseline.Time)return;
        var timeline=new CountermeasureTimeline(baseline);
        var past=baseline.Snapshot;
        if(next!=null&&next.countermeasureId==baseline.countermeasureId&&next.Time>=cut&&next.Time>past.time&&next.phase!=CountermeasurePhase.Launch)
        {
            var sample=At(past,next.Snapshot,cut);var value=new CountermeasureEvent {countermeasureId=baseline.countermeasureId,phase=CountermeasurePhase.Sample,tailSeconds=baseline.tailSeconds};value.SetSnapshot(sample);
            if(next.endAge>=0&&cut>=next.Time)value.endAge=next.endAge;
            timeline.Add(value);
        }
        else
        {var value=new CountermeasureEvent {countermeasureId=baseline.countermeasureId,phase=CountermeasurePhase.Sample,tailSeconds=baseline.tailSeconds};value.SetSnapshot(At(past,past,cut));timeline.Add(value);}
        timeline.SnapshotEvent().CopyTo(baseline);
    }
    public static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    public static bool Valid(CountermeasureSnapshot value)
    {
        double norm=(double)value.rotation.x*value.rotation.x+(double)value.rotation.y*value.rotation.y+(double)value.rotation.z*value.rotation.z+(double)value.rotation.w*value.rotation.w;
        return Finite(value.time)&&Finite(value.position.x)&&Finite(value.position.y)&&Finite(value.position.z)&&Finite(value.velocity.x)&&Finite(value.velocity.y)&&Finite(value.velocity.z)&&
            Finite(norm)&&norm>.000001&&norm<4&&Finite(value.age)&&value.age>=0&&value.age<=MaxAge&&Finite(value.remaining)&&value.remaining>=0&&value.remaining<=MaxBurn;
    }
}
