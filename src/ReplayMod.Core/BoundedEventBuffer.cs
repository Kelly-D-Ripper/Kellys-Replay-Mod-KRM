using System;
using System.Collections.Generic;
using System.Threading;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
namespace ReplayMod.Core;

// Single producer/consumer with bounded ownership. Merge due events in original
// file order; only future interpolation may bypass actions, and never across an
// unexecuted actor spawn/despawn boundary.
public class BoundedEventBuffer
{
    private readonly struct Entry
    {
        public readonly IReplayEvent Event;
        public readonly long Sequence;
        public Entry(IReplayEvent e,long sequence){Event=e;Sequence=sequence;}
    }
    private readonly Queue<Entry> actions=new Queue<Entry>();
    private readonly Queue<Entry> interpolation=new Queue<Entry>();
    private readonly Dictionary<ulong,Queue<Entry>> held=new Dictionary<ulong,Queue<Entry>>();
    private readonly Dictionary<ulong,Queue<Entry>> boundaries=new Dictionary<ulong,Queue<Entry>>();
    private readonly Dictionary<uint,Queue<Entry>> motionBarriers=new Dictionary<uint,Queue<Entry>>();
    private readonly object gate=new object();
    private readonly int capacity;
    private readonly SemaphoreSlim freeSpace;
    private long sequence;
    private int count;
    public int Count=>Volatile.Read(ref count);
    public int FreeSpace=>capacity-Count;
    public BoundedEventBuffer(int capacity)
    {
        if(capacity<=0)throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity=capacity;freeSpace=new SemaphoreSlim(capacity,capacity);
    }
    public static bool IsInterpolation(IReplayEvent e)=>e is AircraftStatusEvent||e is EngineCoreStateEvent||e is PilotEjectionStateEvent||e is CargoParachuteStateEvent||e is UpdatePositionEvent||e is UpdateInputsEvent||e is UpdateTurretEvent||e is AircraftVisualStateEvent||e is AircraftPartMotionEvent||e is HangarDoorStateEvent||e is WorldEnvironmentStateEvent||e is CountermeasureEvent flare&&flare.phase==CountermeasurePhase.Sample||e is FuelFlameStateEvent fire&&fire.phase==FuelFlamePhase.Sample;
    private static ulong SampleActor(IReplayEvent e)=>e is EngineCoreStateEvent core?core.unitId:e is AircraftStatusEvent engine?engine.unitId:e is PilotEjectionStateEvent eye?eye.unitId:e is CargoParachuteStateEvent chutes?chutes.unitId:e is FuelFlameStateEvent fire?FuelFlameKey(fire.flameId):e is CountermeasureEvent flare?CountermeasureKey(flare.countermeasureId):e is UpdatePositionEvent move?move.unitId:e is UpdateInputsEvent input?input.id:e is AircraftVisualStateEvent visual?visual.unitId:e is AircraftPartMotionEvent motion?motion.unitId:e is HangarDoorStateEvent door?door.unitId:e is WorldEnvironmentStateEvent?0:((UpdateTurretEvent)e).attachedUnitId;
    private static ulong FuelFlameKey(uint id)=>(2UL<<32)|id;
    private static ulong CountermeasureKey(uint id)=>(1UL<<32)|id;
    private static bool BoundaryKey(IReplayEvent e,out ulong key)
    {
        if(e is CountermeasureEvent flare&&flare.phase!=CountermeasurePhase.Sample){key=CountermeasureKey(flare.countermeasureId);return true;}
        if(e is FuelFlameStateEvent fire&&fire.phase!=FuelFlamePhase.Sample){key=FuelFlameKey(fire.flameId);return true;}
        if(BoundaryActor(e,out var id)){key=id;return true;}
        key=0;return false;
    }
    private static bool BoundaryActor(IReplayEvent e,out uint id)
    {
        if(e is SpawnEvent spawn){id=spawn.unitId;return true;}
        if(e is DespawnEvent despawn){id=despawn.unitId;return true;}
        // A client can receive the authoritative impact behind its predicted
        // flight pose. Never interpolate toward post-impact samples before the
        // recorded impact action has executed.
        if(e is MissileDetonationEvent impact){id=impact.unitId;return true;}
        if(e is AircraftVisualLayoutEvent layout){id=layout.unitId;return true;}
        id=0;return false;
    }
    private static bool MotionBarrierActor(IReplayEvent e,out uint id)
    {
        if(BoundaryActor(e,out id))return true;
        if(e is PartDamageEvent damage){id=damage.unitId;return true;}
        if(e is DetachPartEvent detach){id=detach.unitID;return true;}
        if(e is EjectionEvent eject){id=eject.unitId;return true;}
        if(e is UnitStatusEvent status&&status.disabled){id=status.unitId;return true;}
        if(e is FuelTankStatusEvent fuel&&(fuel.ruptured||fuel.onFire||fuel.flameActive)){id=fuel.unitId;return true;}
        id=0;return false;
    }
    public double NextMotionBarrier(uint id)
    {
        lock(gate)return motionBarriers.TryGetValue(id,out var queue)&&queue.Count>0?queue.Peek().Event.Time:double.PositiveInfinity;
    }
    public void Add(IReplayEvent e,CancellationToken token)
    {
        if(e==null)throw new ArgumentNullException(nameof(e));
        freeSpace.Wait(token);
        lock(gate)
        {
            var entry=new Entry(e,++sequence);
            Interlocked.Increment(ref count);
            if(IsInterpolation(e))interpolation.Enqueue(entry);
            else
            {
                if(MotionBarrierActor(e,out var motionId))
                {
                    if(!motionBarriers.TryGetValue(motionId,out var motionQueue))motionBarriers[motionId]=motionQueue=new Queue<Entry>();
                    motionQueue.Enqueue(entry);
                }
                if(BoundaryKey(e,out var id))
                {
                    if(!boundaries.TryGetValue(id,out var queue))boundaries[id]=queue=new Queue<Entry>();
                    queue.Enqueue(entry);
                }
                actions.Enqueue(entry);
            }
        }
    }
    private bool IsBlocked(Entry sample)=>boundaries.TryGetValue(SampleActor(sample.Event),out var queue)&&queue.Count>0&&queue.Peek().Sequence<sample.Sequence;
    public void MarkActionExecuted(IReplayEvent e)
    {
        lock(gate)
        {
            if(MotionBarrierActor(e,out var motionId)&&motionBarriers.TryGetValue(motionId,out var motionQueue)&&motionQueue.Count>0&&ReferenceEquals(motionQueue.Peek().Event,e))
            {motionQueue.Dequeue();if(motionQueue.Count==0)motionBarriers.Remove(motionId);}
            if(!BoundaryKey(e,out var id))return;
            if(!boundaries.TryGetValue(id,out var queue)||queue.Count==0||!ReferenceEquals(queue.Peek().Event,e))
                throw new InvalidOperationException("Replay lifecycle actions executed out of order");
            queue.Dequeue();if(queue.Count==0)boundaries.Remove(id);
        }
    }
    private static bool Before(Entry a,Entry? b)=>!b.HasValue||a.Event.Time<b.Value.Event.Time||(a.Event.Time==b.Value.Event.Time&&a.Sequence<b.Value.Sequence);
    private void ReleaseSlot(){Interlocked.Decrement(ref count);freeSpace.Release();}
    public bool TryTakeIfReady(double time,out IReplayEvent e)
    {
        lock(gate)
        {
            e=null;
            // Move blocked future samples off the shared head so other actors
            // retain their interpolation; their capacity slots remain owned.
            while(interpolation.Count>0)
            {
                var head=interpolation.Peek();ulong id=SampleActor(head.Event);
                if(head.Event.Time<=time||(!IsBlocked(head)&&(!held.TryGetValue(id,out var older)||older.Count==0)))break;
                interpolation.Dequeue();
                if(!held.TryGetValue(id,out var queue))held[id]=queue=new Queue<Entry>();
                queue.Enqueue(head);
            }
            Entry? best=null;Queue<Entry> selected=null;
            if(actions.Count>0&&actions.Peek().Event.Time<=time){best=actions.Peek();selected=actions;}
            if(interpolation.Count>0&&interpolation.Peek().Event.Time<=time&&Before(interpolation.Peek(),best))
            {best=interpolation.Peek();selected=interpolation;}
            foreach(var queue in held.Values)
                if(queue.Count>0&&queue.Peek().Event.Time<=time&&Before(queue.Peek(),best)){best=queue.Peek();selected=queue;}
            if(best==null)
            {
                if(interpolation.Count>0&&interpolation.Peek().Event.Time<=time+PlaybackReadiness.LookAheadSeconds)
                {best=interpolation.Peek();selected=interpolation;}
                foreach(var queue in held.Values)
                    if(queue.Count>0&&queue.Peek().Event.Time<=time+PlaybackReadiness.LookAheadSeconds&&!IsBlocked(queue.Peek())&&Before(queue.Peek(),best))
                    {best=queue.Peek();selected=queue;}
            }
            if(best==null)return false;
            e=selected.Dequeue().Event;
            if(selected!=actions&&selected!=interpolation&&selected.Count==0)held.Remove(SampleActor(e));
            ReleaseSlot();return true;
        }
    }
    // Producer must be cancelled and joined before this ownership reset.
    public void ClearAndReturnToPool()
    {
        lock(gate)
        {
            boundaries.Clear();
            motionBarriers.Clear();
            while(actions.Count>0){ReplayEventFactory.Return(actions.Dequeue().Event);ReleaseSlot();}
            while(interpolation.Count>0){ReplayEventFactory.Return(interpolation.Dequeue().Event);ReleaseSlot();}
            foreach(var queue in held.Values)while(queue.Count>0){ReplayEventFactory.Return(queue.Dequeue().Event);ReleaseSlot();}
            held.Clear();
        }
    }
}
