using System;
using System.Collections.Generic;
using System.IO;

namespace ReplayMod.Core;

public readonly struct ReplayActorLifetime
{
    public readonly uint Actor;
    public readonly long Generation;
    public readonly double BirthTime,DeathTime;
    public ReplayActorLifetime(uint actor,long generation,double birth,double death)
    {Actor=actor;Generation=generation;BirthTime=birth;DeathTime=death;}
}

// File-order generations distinguish a final hit from a same-timestamp reused
// ID. Build once with the validated file index; no event payloads are retained.
public sealed class CameraActorLifetimeIndex
{
    public const int MaximumLifetimes=250000;
    private readonly int maximum;
    public CameraActorLifetimeIndex(int maximum=MaximumLifetimes)
    {if(maximum<1||maximum>MaximumLifetimes)throw new ArgumentOutOfRangeException(nameof(maximum));this.maximum=maximum;}
    private sealed class Lifetime
    {
        public uint Actor;public long Generation;public double Birth,Death=double.PositiveInfinity;
        public ReplayActorLifetime Snapshot()=>new ReplayActorLifetime(Actor,Generation,Birth,Death);
    }
    private readonly Dictionary<uint,List<Lifetime>> actors=new Dictionary<uint,List<Lifetime>>();
    private readonly Dictionary<uint,Lifetime> current=new Dictionary<uint,Lifetime>();
    private readonly Dictionary<long,Lifetime> generations=new Dictionary<long,Lifetime>();
    public void Spawn(uint actor,double time,long generation)
    {
        if(!Finite(time)||time<0||generation<=0||generations.ContainsKey(generation))throw new ArgumentException("Invalid camera actor lifetime");
        if(generations.Count>=maximum)throw new InvalidDataException("Replay contains too many actor lifetimes for safe camera indexing");
        if(current.TryGetValue(actor,out var previous))previous.Death=time;
        if(!actors.TryGetValue(actor,out var list))actors[actor]=list=new List<Lifetime>();
        var entry=new Lifetime {Actor=actor,Generation=generation,Birth=time};list.Add(entry);current[actor]=entry;generations.Add(generation,entry);
    }
    public void Despawn(uint actor,double time)
    {
        if(!Finite(time)||time<0)throw new ArgumentException("Invalid camera actor removal time");
        if(current.TryGetValue(actor,out var entry)){entry.Death=time;current.Remove(actor);}
    }
    public long CurrentGeneration(uint actor)=>current.TryGetValue(actor,out var value)?value.Generation:0;
    public bool TryGeneration(uint actor,long generation,out ReplayActorLifetime lifetime)
    {
        lifetime=default;
        if(!generations.TryGetValue(generation,out var value)||value.Actor!=actor)return false;
        lifetime=value.Snapshot();return true;
    }
    public bool TryAt(uint actor,double time,out ReplayActorLifetime lifetime)
    {
        lifetime=default;
        if(!Finite(time)||!actors.TryGetValue(actor,out var list))return false;
        int index=LastBorn(list,time);if(index<0)return false;
        var value=list[index];if(time>value.Death)return false;
        // Old bookmarks lack a generation token. At a tied removal/new birth,
        // choosing either actor would be a guess, so allow time-only jumping.
        if(index>0&&list[index-1].Death==time&&value.Birth==time)return false;
        lifetime=value.Snapshot();return true;
    }
    public long VisibleGenerationAt(uint actor,double time)
    {
        if(!Finite(time)||!actors.TryGetValue(actor,out var list))return 0;
        int index=LastBorn(list,time);if(index<0)return 0;
        var value=list[index];return time<value.Death?value.Generation:0;
    }
    private static int LastBorn(List<Lifetime> list,double time)
    {
        int lo=0,hi=list.Count;
        while(lo<hi){int mid=lo+(hi-lo)/2;if(list[mid].Birth<=time)lo=mid+1;else hi=mid;}
        return lo-1;
    }
    public void Clear(){actors.Clear();current.Clear();generations.Clear();}
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
}
