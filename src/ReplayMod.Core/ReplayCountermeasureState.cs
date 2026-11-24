using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

// Owns value snapshots, never pooled producer events. Capacity and per-trajectory
// history bounds also bound checkpoints during long multiplayer recordings.
public sealed class ReplayCountermeasureState
{
    private readonly Dictionary<uint,CountermeasureTimeline> tracks=new Dictionary<uint,CountermeasureTimeline>();
    private double nextExpiry=double.PositiveInfinity;
    private long estimatedBytes;
    public int Count=>tracks.Count;
    // Index memory admission queries this for every event, including unrelated
    // movement. Keep it constant-time instead of scanning every active trail.
    public long EstimatedBytes=>estimatedBytes;
    private static long Estimate(CountermeasureTimeline track)=>768L+(track.metadata.unitKey.Length+track.metadata.prefabName.Length)*2L+track.SampleCount*96L;
    public void Add(CountermeasureEvent value)
    {
        try
        {
            Expire(value.Time);
            if(!value.HasValidPayload())throw new InvalidDataException("Invalid countermeasure state");
            tracks.TryGetValue(value.countermeasureId,out var previous);
            long before=previous==null?0:Estimate(previous);
            if(value.phase==CountermeasurePhase.Launch)
            {
                if(!tracks.ContainsKey(value.countermeasureId)&&tracks.Count>=CountermeasureTimeline.MaxActive)throw new InvalidDataException("Too many active replay countermeasures");
                tracks[value.countermeasureId]=new CountermeasureTimeline(value);
            }
            else if(tracks.TryGetValue(value.countermeasureId,out var track))track.Add(value);
            if(tracks.TryGetValue(value.countermeasureId,out var current))
            {estimatedBytes+=Estimate(current)-before;nextExpiry=Math.Min(nextExpiry,current.ExpiresAt);}
        }
        finally{ReplayEventFactory.Return(value);}
    }
    public void Expire(double time)
    {
        if(!CountermeasureTimeline.Finite(time)||time<nextExpiry)return;
        nextExpiry=double.PositiveInfinity;
        foreach(var pair in tracks.ToArray())
        {if(pair.Value.ExpiresAt<=time){estimatedBytes-=Estimate(pair.Value);tracks.Remove(pair.Key);}else nextExpiry=Math.Min(nextExpiry,pair.Value.ExpiresAt);}
    }
    public void CopyTo(Queue<IReplayEvent> destination)
    {
        foreach(var track in tracks.Values.OrderBy(track=>track.Latest.time).ThenBy(track=>track.metadata.countermeasureId))
        {var value=ReplayEventFactory.GetEvent<CountermeasureEvent>();track.SnapshotEvent().CopyTo(value);destination.Enqueue(value);}
    }
    public void CloneTo(ReplayCountermeasureState destination)
    {foreach(var pair in tracks){var clone=pair.Value.Clone();destination.tracks.Add(pair.Key,clone);destination.estimatedBytes+=Estimate(clone);}destination.nextExpiry=nextExpiry;}
    public void Clear(){tracks.Clear();nextExpiry=double.PositiveInfinity;estimatedBytes=0;}
}
