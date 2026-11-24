using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public sealed class ReplayFuelFlameState
{
    private readonly Dictionary<uint,FuelFlameStateEvent> states=new Dictionary<uint,FuelFlameStateEvent>();
    private long bytes;
    public int Count=>states.Count;
    public long EstimatedBytes=>bytes;
    private static long Estimate(FuelFlameStateEvent value)=>768L+(value.unitKey.Length+value.prefabName.Length)*2L+value.snapshot.opacity.Length*16L;
    public void Add(FuelFlameStateEvent value)
    {
        try
        {
            if(!value.HasValidPayload())throw new InvalidDataException("Invalid recorded fuel fire");
            if(value.phase==FuelFlamePhase.End){if(states.TryGetValue(value.flameId,out var removed)){bytes-=Estimate(removed);states.Remove(value.flameId);}return;}
            if(value.phase==FuelFlamePhase.Begin)
            {
                if(!states.ContainsKey(value.flameId)&&states.Count>=FuelFlameTimeline.MaxActive)throw new InvalidDataException("Too many live fuel fire effects");
                if(states.TryGetValue(value.flameId,out var old))bytes-=Estimate(old);
                var copy=new FuelFlameStateEvent();value.CopyTo(copy);states[value.flameId]=copy;bytes+=Estimate(copy);
            }
            else if(states.TryGetValue(value.flameId,out var current)&&value.Time>=current.Time)
            {bytes-=Estimate(current);current.snapshot=value.snapshot.Copy();bytes+=Estimate(current);}
        }
        finally{ReplayEventFactory.Return(value);}
    }
    public void CopyTo(Queue<IReplayEvent> destination)
    {foreach(var item in states.Values.OrderBy(value=>value.Time)){var copy=ReplayEventFactory.GetEvent<FuelFlameStateEvent>();item.CopyTo(copy);destination.Enqueue(copy);}}
    public void CloneTo(ReplayFuelFlameState destination)
    {foreach(var item in states){var copy=new FuelFlameStateEvent();item.Value.CopyTo(copy);destination.states.Add(item.Key,copy);destination.bytes+=Estimate(copy);}}
    public void DetachOwner(uint id)
    {
        // A timestamp can be shared by despawn and a replacement spawn. Once
        // the original lifetime ends, checkpoints must never bind its old fire
        // to the replacement merely because both births compare equal.
        foreach(var effect in states.Values)if(effect.unitId==id)effect.snapshot.attached=false;
    }
    public void Clear(){states.Clear();bytes=0;}
}
