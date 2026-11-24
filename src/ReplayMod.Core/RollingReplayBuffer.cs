using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

// Main-thread owned. Immutable event bytes keep pooled objects out of the ring.
public sealed class RollingReplayBuffer : IDisposable
{
    private readonly Queue<Entry> events = new Queue<Entry>();
    private readonly ReplaySeekState anchor = new ReplaySeekState();
    private double window;
    private readonly long byteLimit;
    private long bytes;
    private double memoryCutTime;
    public double CutTime { get; private set; }
    public long Bytes => bytes;
    public bool MemoryLimited { get; private set; }
    private sealed class Entry { public double time; public byte[] data; }
    public RollingReplayBuffer(double seconds, long maxBytes = 128L*1024*1024)
    { if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 10 || maxBytes < 1024) throw new ArgumentOutOfRangeException(); window=seconds; byteLimit=maxBytes; }
    public static byte[] Encode(IReplayEvent e)
    { using(var s=new MemoryStream()) { using(var w=new BinaryWriter(s)) { w.Write((byte)e.EventType); e.Write(w); return s.ToArray(); } } }
    public static IReplayEvent Decode(byte[] bytes)
    { using(var s=new MemoryStream(bytes)) using(var r=new ReplayFrameReader(s)) return ReplayEventFactory.CreateAndRead((EventType)r.ReadByte(),r); }
    public void Add(IReplayEvent e)
    {
        var data=Encode(e); events.Enqueue(new Entry {time=e.Time,data=data}); bytes+=data.Length;
        Trim(e.Time);
    }
    public void Trim(double now)
    {
        while(events.Count>0 && (events.Peek().time < now-window || bytes>byteLimit))
        {
            var old=events.Dequeue();
            if (bytes>byteLimit && old.time>=now-window) memoryCutTime=Math.Max(memoryCutTime,old.time);
            bytes-=old.data.Length; anchor.Add(Decode(old.data)); CutTime=Math.Max(CutTime,old.time);
        }
        CutTime=Math.Max(CutTime,Math.Max(0,now-window));
        MemoryLimited=memoryCutTime>Math.Max(0,now-window)+0.001;
    }
    public void SetWindow(double seconds, double now)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 10)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        window=seconds; Trim(now);
    }
    public List<byte[]> Snapshot(double now)
    {
        Trim(now);
        anchor.ExpireCountermeasures(CutTime);
        var state=new Queue<IReplayEvent>(); anchor.CopyTo(state);
        Dictionary<string,HangarDoorStateEvent> previousDoors=null;
        WorldEnvironmentStateEvent previousEnvironment=null;
        try
        {
            var spawns=state.OfType<SpawnEvent>().ToDictionary(e=>e.unitId);
            var moves=state.OfType<UpdatePositionEvent>().ToDictionary(e=>e.unitId);
            var visuals=state.OfType<AircraftVisualStateEvent>().ToDictionary(e=>e.unitId);
            var previousVisuals=new Dictionary<uint,AircraftVisualSnapshot>();
            var eyes=state.OfType<PilotEjectionStateEvent>().ToDictionary(e=>e.unitId);
            foreach(var eye in eyes.Values)MarkOrphanedEyeAtCut(eye,spawns);
            var previousEyes=new Dictionary<uint,PilotEjectionStateEvent>(eyes);
            var doors=state.OfType<HangarDoorStateEvent>().ToDictionary(e=>e.unitId+":"+e.hangarPath);
            previousDoors=doors.ToDictionary(p=>p.Key,p=>(HangarDoorStateEvent)Decode(Encode(p.Value)));
            var partMotions=state.OfType<AircraftPartMotionEvent>().ToDictionary(e=>e.unitId);
            var previousParts=partMotions.ToDictionary(p=>p.Key,p=>AircraftPartMotionSnapshot.From(p.Value));
            var environment=state.OfType<WorldEnvironmentStateEvent>().FirstOrDefault();
            previousEnvironment=environment==null?null:(WorldEnvironmentStateEvent)Decode(Encode(environment));
            if(previousEnvironment!=null)WorldStateSnapshot.SampleAtCut(previousEnvironment,null,CutTime,environment);
            foreach(var pair in visuals)previousVisuals[pair.Key]=AircraftVisualSnapshot.From(pair.Value);
            var previous=new Dictionary<uint,PositionSnapshot>();
            var countermeasures=state.OfType<CountermeasureEvent>().ToDictionary(e=>e.countermeasureId);
            var previousCountermeasures=new Dictionary<uint,CountermeasureEvent>(countermeasures);
            var fuelFlames=state.OfType<FuelFlameStateEvent>().ToDictionary(e=>e.flameId);
            foreach(var fire in fuelFlames.Values)
                if(!spawns.TryGetValue(fire.unitId,out var owner)||Math.Abs(owner.Time-fire.ownerBirth)>.00001)fire.snapshot.attached=false;
            var previousFuelFlames=new Dictionary<uint,FuelFlameStateEvent>(fuelFlames);
            foreach(var pair in spawns)
            {
                var spawn=pair.Value;
                previous[pair.Key]=moves.TryGetValue(pair.Key,out var move)?PositionSnapshot.Create(move):
                    new PositionSnapshot {time=spawn.Time,position=spawn.pos,rotation=spawn.rotation,velocity=spawn.startingVelocity};
            }
            var retained=new List<byte[]>();
            foreach(var item in events)
            {
                var e=Decode(item.data);
                try
                {
                    // Only the first retained pose in this same actor lifetime is
                    // usable. An intervening lifecycle boundary invalidates it.
                    if(e is PilotEjectionStateEvent eye&&previousEyes.TryGetValue(eye.unitId,out var pastEye))
                    {
                        MarkOrphanedEyeAtCut(eye,spawns);
                        previousEyes.Remove(eye.unitId);
                        if(pastEye.aircraftId==eye.aircraftId&&pastEye.aircraftBirth==eye.aircraftBirth&&pastEye.seat==eye.seat&&pastEye.capsule==eye.capsule&&eye.Time>pastEye.Time&&eye.Time>=CutTime)
                        {var cut=ReplayPoseInterpolation.At(new PositionSnapshot {time=pastEye.Time,position=pastEye.eye,rotation=pastEye.rotation},new PositionSnapshot {time=eye.Time,position=eye.eye,rotation=eye.rotation},CutTime);pastEye.eye=cut.position;pastEye.rotation=cut.rotation;}
                    }
                    else if(e is SpawnEvent birthEye)previousEyes.Remove(birthEye.unitId);
                    else if(e is DespawnEvent deadEye)previousEyes.Remove(deadEye.unitId);
                    if(e is FuelFlameStateEvent fire)
                    {
                        if(fire.phase==FuelFlamePhase.Begin)previousFuelFlames.Remove(fire.flameId);
                        else if(previousFuelFlames.TryGetValue(fire.flameId,out var pastFire))
                        {previousFuelFlames.Remove(fire.flameId);if(fire.phase==FuelFlamePhase.Sample)pastFire.snapshot=FuelFlameTimeline.At(pastFire.snapshot,fire.snapshot,CutTime);}
                    }
                    else if(e is CountermeasureEvent flare)
                    {
                        if(flare.phase==CountermeasurePhase.Launch)previousCountermeasures.Remove(flare.countermeasureId);
                        else if(previousCountermeasures.TryGetValue(flare.countermeasureId,out var pastFlare))
                        {previousCountermeasures.Remove(flare.countermeasureId);CountermeasureTimeline.SampleAtCut(pastFlare,flare,CutTime);}
                    }
                    else if(e is SpawnEvent spawn) { previous.Remove(spawn.unitId); previousVisuals.Remove(spawn.unitId); ForgetWorldAnchors(spawn.unitId,previousDoors,previousParts); }
                    else if(e is DespawnEvent despawn) { previous.Remove(despawn.unitId); previousVisuals.Remove(despawn.unitId); ForgetWorldAnchors(despawn.unitId,previousDoors,previousParts); }
                    else if(e is MissileDetonationEvent impact) { previous.Remove(impact.unitId); }
                    else if(e is WorldEnvironmentStateEvent futureEnvironment && previousEnvironment!=null)
                    {WorldStateSnapshot.SampleAtCut(previousEnvironment,futureEnvironment,CutTime,environment);ReplayEventFactory.Return(previousEnvironment);previousEnvironment=null;}
                    else if(e is HangarDoorStateEvent door && previousDoors.TryGetValue(door.unitId+":"+door.hangarPath,out var pastDoor))
                    {
                        string key=door.unitId+":"+door.hangarPath;previousDoors.Remove(key);
                        HangarDoorSnapshot.SampleAtCut(pastDoor,door,CutTime,doors[key]);ReplayEventFactory.Return(pastDoor);
                    }
                    else if(e is AircraftPartMotionEvent motion && previousParts.TryGetValue(motion.unitId,out var pastParts))
                    {
                        previousParts.Remove(motion.unitId);
                        var nextParts=AircraftPartMotionSnapshot.From(motion);
                        if(motion.Time>=CutTime && motion.Time>pastParts.time && AircraftPartMotionSnapshot.SameLayout(pastParts,nextParts))
                            AircraftPartMotionSnapshot.At(pastParts,nextParts,CutTime).CopyTo(partMotions[motion.unitId]);
                    }
                    else if(e is AircraftVisualLayoutEvent layout) previousVisuals.Remove(layout.unitId);
                    else if(e is AircraftVisualStateEvent visual && previousVisuals.TryGetValue(visual.unitId,out var pastVisual))
                    {
                        previousVisuals.Remove(visual.unitId);
                        if(visual.Time>=CutTime && visual.Time>pastVisual.time && visual.layoutId==pastVisual.layoutId && visual.poses.Length==pastVisual.poses.Length)
                            AircraftVisualSnapshot.At(pastVisual,AircraftVisualSnapshot.From(visual),CutTime).CopyTo(visuals[visual.unitId]);
                    }
                    else if(e is UpdatePositionEvent move && previous.TryGetValue(move.unitId,out var past))
                    {
                        previous.Remove(move.unitId);
                        if(move.Time>=CutTime && move.Time>past.time)
                        {
                            var cut=ReplayPoseInterpolation.At(past,PositionSnapshot.Create(move),CutTime);
                            var baseline=spawns[move.unitId];
                            baseline.pos=cut.position;baseline.rotation=cut.rotation;baseline.startingVelocity=cut.velocity;
                            if(moves.TryGetValue(move.unitId,out var baselineMove))baselineMove.CopyFromSnapshot(cut);
                        }
                    }
                    double clipTime=Math.Max(0,e.Time-CutTime);
                    if(e is CountermeasureEvent countermeasure)countermeasure.RebaseTime(clipTime);else if(e is FuelFlameStateEvent flame)flame.RebaseTime(clipTime);else if(e is PilotEjectionStateEvent eyeState){MarkOrphanedEyeAtCut(eyeState,spawns);eyeState.Rebase(CutTime);}else if(e is BuildingStateEvent buildingState)buildingState.Rebase(CutTime);else if(e is GunSpinStateEvent spin)spin.RebaseAt(e.Time,clipTime);else e.Time=clipTime;
                    retained.Add(Encode(e));
                }
                finally {ReplayEventFactory.Return(e);}
            }
            var result=new List<byte[]>(state.Count+retained.Count);
            foreach(var e in state)
            {
                if(e is CountermeasureEvent flare)
                {CountermeasureTimeline.SampleAtCut(flare,null,CutTime);flare.RebaseTime(0);}
                else if(e is FuelFlameStateEvent fire)
                {fire.snapshot=FuelFlameTimeline.At(fire.snapshot,fire.snapshot,CutTime);fire.RebaseTime(0);}
                else if(e is PilotEjectionStateEvent eyeState){eyeState.Rebase(CutTime);eyeState.Time=0;}
                else if(e is BuildingStateEvent buildingState){buildingState.Rebase(CutTime);buildingState.Time=0;}
                else if(e is GunSpinStateEvent spin)spin.RebaseAt(CutTime,0);
                else e.Time=0;
                result.Add(Encode(e));
            }
            result.AddRange(retained);return result;
        }
        finally
        {
            if(previousDoors!=null)foreach(var e in previousDoors.Values)ReplayEventFactory.Return(e);
            if(previousEnvironment!=null)ReplayEventFactory.Return(previousEnvironment);
            while(state.Count>0)ReplayEventFactory.Return(state.Dequeue());
        }
    }
    private void MarkOrphanedEyeAtCut(PilotEjectionStateEvent eye,Dictionary<uint,SpawnEvent> spawns)
    {
        if(!eye.capsule&&eye.aircraftBirth<CutTime&&(!spawns.TryGetValue(eye.aircraftId,out var source)||source.Time!=eye.aircraftBirth))
            eye.orphanedAircraft=true;
    }
    private static void ForgetWorldAnchors(uint id,Dictionary<string,HangarDoorStateEvent> doors,Dictionary<uint,AircraftPartMotionSnapshot> parts)
    {
        parts.Remove(id);
        foreach(var key in doors.Keys.Where(k=>k.StartsWith(id+":",StringComparison.Ordinal)).ToArray())
        {ReplayEventFactory.Return(doors[key]);doors.Remove(key);}
    }
    public void Dispose() { var state=new Queue<IReplayEvent>(); anchor.Drain(state); while(state.Count>0) ReplayEventFactory.Return(state.Dequeue()); events.Clear(); bytes=0; }
}
