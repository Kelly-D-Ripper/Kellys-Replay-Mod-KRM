using System.Collections.Concurrent;
using System.Reflection;
using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class SeekStateChecks
{
    public static int Run()
    {
        int passed = 0;
        Test("seek keeps the actual past pose time for target interpolation", () =>
        {
            using var state = new ReplaySeekState();
            state.Add(Spawn(1, 2)); state.Add(Move(1, 9, 90));
            var queue = Snapshot(state);
            var spawn = queue.OfType<SpawnEvent>().Single(); var anchor = queue.OfType<UpdatePositionEvent>().Single();
            Check(spawn.Time == 2 && spawn.pos.x == 90, "spawn time and restored pose");
            Check(anchor.Time == 9, "last past sample is not retimed to spawn or seek");
            var history = new List<PositionSnapshot>();
            SnapshotHistory.Insert(history, new PositionSnapshot {time=spawn.Time,position=spawn.pos}, p=>p.time);
            SnapshotHistory.Insert(history, PositionSnapshot.Create(anchor), p=>p.time);
            SnapshotHistory.Insert(history, PositionSnapshot.Create(Move(1,11,110)), p=>p.time);
            Check(SnapshotHistory.Locate(history,10,p=>p.time,out int a,out int b,out float t),"target has an anchor");
            Near(history[a].position.x+(history[b].position.x-history[a].position.x)*t,100);
            Return(queue);
        });
        Test("checkpoint clone is independent and retains timestamps and instant gear", () =>
        {
            using var state = new ReplaySeekState();
            state.Add(Spawn(1,2)); state.Add(Move(1,9,90));
            state.Add(new SetGearEvent {unitId=1,Time=3,deployed=true});
            state.Add(new AircraftStatusEvent {unitId=1,Time=8,rpm=new[]{.7f},ignition=true});
            long bytes = state.EstimatedBytes;
            using var checkpoint = state.Clone();
            state.Add(Move(1,12,120)); state.Add(new AircraftStatusEvent {unitId=1,Time=12,rpm=new[]{.2f}});
            var restored = Snapshot(checkpoint); var gear = restored.OfType<SetGearEvent>().Single();
            Check(gear.Instant && gear.Time==3,"playback-only instant survives clone");
            Check(restored.OfType<SpawnEvent>().Single().Time==2 && restored.OfType<UpdatePositionEvent>().Single().Time==9,"historical timestamps survive clone");
            restored.OfType<AircraftStatusEvent>().Single().rpm[0]=42;
            Return(restored); state.Clear();
            var repeated = Snapshot(checkpoint);
            Near(repeated.OfType<AircraftStatusEvent>().Single().rpm[0],.7f);
            Check(checkpoint.EstimatedBytes==bytes && bytes>0 && state.EstimatedBytes==0,"independent accounting and clear");
            Return(repeated);
        });
        Test("engine cores survive checkpoints separately from legacy propulsion state", () =>
        {
            using var state=new ReplaySeekState();state.Add(Spawn(1,0));
            state.Add(new AircraftStatusEvent{unitId=1,Time=2,ignition=true,rpm=new[]{100f}});
            state.Add(new EngineCoreStateEvent{unitId=1,Time=2,ignition=true,rpm=new[]{2100f,2200f}});
            using var checkpoint=state.Clone();state.Add(new EngineCoreStateEvent{unitId=1,Time=3,rpm=new[]{300f}});
            var restored=Snapshot(checkpoint);
            Check(restored.OfType<AircraftStatusEvent>().Single().rpm[0]==100f,"legacy array unchanged");
            var core=restored.OfType<EngineCoreStateEvent>().Single();Check(core.Time==2&&core.rpm.Length==2&&core.rpm[0]==2100f,"checkpoint retained recorded cores");
            core.rpm[0]=1;Return(restored);var again=Snapshot(checkpoint);Check(again.OfType<EngineCoreStateEvent>().Single().rpm[0]==2100f,"copy is independent");Return(again);
        });
        Test("checkpoint plus suffix equals rebuilding the same replay prefix", () =>
        {
            var stream = new IReplayEvent[] { Spawn(1,0), Spawn(2,2,1), Move(1,3,30),
                new SetGearEvent {unitId=1,Time=4,deployed=true}, new PartDamageEvent {unitId=1,partId=3,Time=5,hitPoints=70},
                new DespawnEvent {unitId=2,Time=6}, Move(1,7,70), new PartDamageEvent {unitId=1,partId=3,Time=8,hitPoints=20},
                new UpdateInputsEvent {id=1,Time=9,roll=-.4f,brake=.6f} }.Select(RollingReplayBuffer.Encode).ToArray();
            using var prefix = new ReplaySeekState(); using var full = new ReplaySeekState();
            for(int i=0;i<stream.Length;i++) {full.Add(RollingReplayBuffer.Decode(stream[i])); if(i<5)prefix.Add(RollingReplayBuffer.Decode(stream[i]));}
            using var cached = prefix.Clone();
            foreach(var bytes in stream.Skip(5)) cached.Add(RollingReplayBuffer.Decode(bytes));
            var first=Snapshot(full); var second=Snapshot(cached);
            Check(first.Count==second.Count && first.Zip(second,(a,b)=>RollingReplayBuffer.Encode(a).SequenceEqual(RollingReplayBuffer.Encode(b))).All(v=>v),"state content and restore order agree");
            Check(second.OfType<SetGearEvent>().Single().Instant,"suffix replay keeps instant baseline state");
            Return(first);Return(second);
        });
        Test("despawn and reused IDs discard old and orphan state", () =>
        {
            using var state = new ReplaySeekState();
            state.Add(Spawn(8,0)); state.Add(Move(8,4,40));
            state.Add(new PartDamageEvent {unitId=8,partId=1,Time=4,hitPoints=10});
            state.Add(new DespawnEvent {unitId=8,Time=5});
            Check(state.EstimatedBytes==0,"despawn releases all owned state");
            state.Add(Move(8,6,60)); state.Add(new PartDamageEvent {unitId=8,Time=6,hitPoints=1});
            Check(state.EstimatedBytes==0,"dead actors do not regain orphan snapshots");
            state.Add(Spawn(8,10)); state.Add(Move(8,9,999)); state.Add(new PartDamageEvent {unitId=8,Time=9,hitPoints=1});
            state.Add(Move(8,12,120)); state.Add(Move(8,11,110));
            var queue=Snapshot(state);
            Check(!queue.OfType<PartDamageEvent>().Any() && queue.OfType<UpdatePositionEvent>().Single().Time==12,"stale past generation and out of order samples ignored");
            Near(queue.OfType<SpawnEvent>().Single().pos.x,120);Return(queue);
        });
        Test("failed checkpoint copy leaves source reusable and destination untouched", () =>
        {
            using var state = new ReplaySeekState(); state.Add(Spawn(1,0));
            state.Add(new MissileStatusEvent {unitId=1,Time=1,fuel=new[]{1f},delay=Array.Empty<float>(),activated=new[]{true}});
            var queue = new Queue<IReplayEvent>(); bool failed=false;
            try {state.CopyTo(queue);} catch(IndexOutOfRangeException) {failed=true;}
            Check(failed && queue.Count==0 && state.EstimatedBytes>0,"failed serialization returns temporary copies");
            state.Add(new MissileStatusEvent {unitId=1,Time=2,fuel=new[]{.5f},delay=new[]{0f},activated=new[]{true}});
            state.Drain(queue); Check(queue.Count==2 && state.EstimatedBytes==0,"source can be replaced and drained after failure");
            Return(queue); state.Clear();state.Dispose();
        });
        Test("truncated event reads return the acquired object to its pool", () =>
        {
            var item=ReplayEventFactory.GetEvent<SetGearEvent>(); ReplayEventFactory.Return(item);
            var field=typeof(ReplayEventPool<SetGearEvent>).GetField("_pool",BindingFlags.Static|BindingFlags.NonPublic);
            var pool=(ConcurrentQueue<SetGearEvent>)field.GetValue(null); int before=pool.Count;
            using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))writer.Write(12d);
            bytes.Position=0;using var reader=new BinaryReader(bytes);bool failed=false;
            try {ReplayEventFactory.CreateAndRead(EventType.ToggleGear,reader);} catch(EndOfStreamException) {failed=true;}
            Check(failed && pool.Count==before && pool.All(e=>e.Time==0 && !e.Instant),"failed acquisition resets and returns exactly once");
        });
        return passed;
        void Test(string name,Action action) {action();passed++;Console.WriteLine("PASS "+name);}
    }
    private static SpawnEvent Spawn(uint id,double time,uint? owner=null) => new SpawnEvent {unitId=id,ownerId=owner??id,Time=time,jsonKey="jet",factionName="blue"};
    private static UpdatePositionEvent Move(uint id,double time,float x) => new UpdatePositionEvent {unitId=id,Time=time,position=new GlobalPosition(x,0,0)};
    private static Queue<IReplayEvent> Snapshot(ReplaySeekState state) {var queue=new Queue<IReplayEvent>();state.CopyTo(queue);return queue;}
    private static void Return(Queue<IReplayEvent> queue) {while(queue.Count>0)ReplayEventFactory.Return(queue.Dequeue());}
    private static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    private static void Near(float value,float expected) {if(Math.Abs(value-expected)>.00011f)throw new Exception($"{value} != {expected}");}
}
