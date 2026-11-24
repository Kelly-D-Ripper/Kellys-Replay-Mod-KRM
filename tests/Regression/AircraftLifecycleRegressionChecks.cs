using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class AircraftLifecycleRegressionChecks
{
    public static int Run()
    {
        int passed=0;
        Test("local detach intent and remote receiver record one observed transition per lifetime",()=>
        {
            var tracker=new ReplayPartDetachTracker();
            Check(tracker.Observe(7,3),"sender or first remote reception captured");
            Check(!tracker.Observe(7,3),"host receiver cannot duplicate sender event");
            Check(tracker.Observe(8,3)&&tracker.Observe(7,4),"other aircraft and parts remain independent");
            tracker.Forget(7);Check(tracker.Observe(7,3),"reused actor ID starts a new lifetime");tracker.Clear();Check(tracker.Observe(8,3),"new recording has no old detach state");
        });
        Test("aircraft world part motion codec preserves breakup flags and independent arrays",()=>
        {
            var source=Motion(2,20);source.parts[0].flags|=AircraftPartFlags.Detached|AircraftPartFlags.Fragmented;
            var bytes=RollingReplayBuffer.Encode(source);var decoded=(AircraftPartMotionEvent)RollingReplayBuffer.Decode(bytes);
            Check(decoded.unitId==7&&decoded.Time==2&&decoded.parts[0].flags==source.parts[0].flags,"part identity, time and breakup state survive");
            var snapshot=AircraftPartMotionSnapshot.From(decoded);ReplayEventFactory.Return(decoded);
            Near(snapshot.parts[0].position.x,20);var copy=new AircraftPartMotionEvent();snapshot.CopyTo(copy);copy.parts[0].position.x=999;
            Near(snapshot.parts[0].position.x,20);Check(RollingReplayBuffer.Encode(source).SequenceEqual(bytes),"source bytes unchanged by consumer");
        });
        Test("aircraft part codec rejects oversized, duplicated, invalid and truncated payloads",()=>
        {
            var e=Motion(0,0);e.parts=new AircraftPartPose[257];Expect<InvalidDataException>(()=>RollingReplayBuffer.Encode(e));
            e=Motion(0,0);e.parts=new[]{e.parts[0],e.parts[0]};Expect<InvalidDataException>(()=>RollingReplayBuffer.Encode(e));
            e=Motion(0,0);e.parts[0].position.x=float.NaN;Expect<InvalidDataException>(()=>RollingReplayBuffer.Encode(e));
            e=Motion(0,0);e.parts[0].flags=(AircraftPartFlags)128;Expect<InvalidDataException>(()=>RollingReplayBuffer.Encode(e));
            e=Motion(0,0);e.blendSeconds=float.PositiveInfinity;Expect<InvalidDataException>(()=>RollingReplayBuffer.Encode(e));
            var bytes=RollingReplayBuffer.Encode(Motion(0,0));Expect<EndOfStreamException>(()=>RollingReplayBuffer.Decode(bytes.Take(bytes.Length-1).ToArray()));
        });
        Test("detached part continues moving after ejection and holds the exact paused frame",()=>
        {
            var timeline=new AircraftPartMotionTimeline();var first=Motion(10,100);first.parts[0].flags|=AircraftPartFlags.Detached;
            var future=Motion(12,120);future.parts[0].flags|=AircraftPartFlags.Detached;timeline.Add(future);timeline.Add(first);
            ReplayEventFactory.Return(first);ReplayEventFactory.Return(future);
            for(int i=0;i<120;i++)
            {
                Check(timeline.Locate(11,out var a,out var b,out var fraction),"recorded falling body remains available");
                Near(AircraftPartMotionSnapshot.At(a,b,11).parts[0].position.x,110);Near(fraction,.5f);
            }
            Check(!timeline.Locate(9,out _,out _,out _),"future detached geometry is not applied before its first observation");
        });
        Test("part interpolation holds stationary gaps and changes visibility only at observed time",()=>
        {
            var a=AircraftPartMotionSnapshot.From(Motion(0,0));var b=AircraftPartMotionSnapshot.From(Motion(10,100));b.blendSeconds=2;
            Near(AircraftPartMotionSnapshot.At(a,b,7).parts[0].position.x,0);Near(AircraftPartMotionSnapshot.At(a,b,9).parts[0].position.x,50);
            b.parts[0].flags=AircraftPartFlags.None;
            var before=AircraftPartMotionSnapshot.At(a,b,9.99);Check((before.parts[0].flags&AircraftPartFlags.Present)!=0,"no early disappearance");Near(before.parts[0].position.x,0);
            Check(AircraftPartMotionSnapshot.At(a,b,10).parts[0].flags==AircraftPartFlags.None,"native disappearance restored at observation");
            b.parts[0].partId=8;Expect<ArgumentException>(()=>AircraftPartMotionSnapshot.At(a,b,5));
        });
        Test("seek and reused actor IDs keep independent aircraft part motion histories",()=>
        {
            using var reducer=new ReplaySeekState();reducer.Add(Spawn(0));reducer.Add(Motion(2,20));using var checkpoint=reducer.Clone();reducer.Add(Motion(3,30));
            var queue=new Queue<IReplayEvent>();checkpoint.CopyTo(queue);Near(queue.OfType<AircraftPartMotionEvent>().Single().parts[0].position.x,20);Return(queue);
            reducer.Add(new DespawnEvent {unitId=7,Time=4});Check(reducer.EstimatedBytes==0,"despawn releases aircraft motion ownership");
            reducer.Add(Spawn(5));reducer.Add(Motion(3,999));reducer.CopyTo(queue);Check(!queue.OfType<AircraftPartMotionEvent>().Any(),"old lifetime samples cannot enter a reused actor");Return(queue);
        });
        Test("rolling replay cuts falling part motion exactly and retains the original later curve",()=>
        {
            using var ring=new RollingReplayBuffer(10);ring.Add(Spawn(0));ring.Add(Motion(1,0));var next=Motion(12,40);next.blendSeconds=4;ring.Add(next);
            var first=ring.Snapshot(19);var events=first.Select(RollingReplayBuffer.Decode).ToArray();
            var cut=events.OfType<AircraftPartMotionEvent>().Single(e=>e.Time==0);var future=events.OfType<AircraftPartMotionEvent>().Single(e=>e.Time==3);
            Near(cut.parts[0].position.x,10);Near(AircraftPartMotionSnapshot.At(AircraftPartMotionSnapshot.From(cut),AircraftPartMotionSnapshot.From(future),1).parts[0].position.x,20);
            Check(first.Zip(ring.Snapshot(19),(a,b)=>a.SequenceEqual(b)).All(v=>v),"cut does not mutate original retained event bytes");foreach(var e in events)ReplayEventFactory.Return(e);
        });
        Test("aircraft part lookahead cannot cross an unexecuted actor spawn",()=>
        {
            var buffer=new BoundedEventBuffer(4);var spawn=Spawn(2);var motion=Motion(2,20);Check(BoundedEventBuffer.IsInterpolation(motion),"part motion joins replay interpolation");
            buffer.Add(spawn,CancellationToken.None);buffer.Add(motion,CancellationToken.None);Check(!buffer.TryTakeIfReady(1.9,out _),"new actor has no early world parts");
            Check(buffer.TryTakeIfReady(2,out var first)&&ReferenceEquals(first,spawn),"spawn is first");buffer.MarkActionExecuted(first);ReplayEventFactory.Return(first);
            Check(buffer.TryTakeIfReady(2,out var second)&&ReferenceEquals(second,motion),"part sample follows correct actor");ReplayEventFactory.Return(second);buffer.ClearAndReturnToPool();
        });
        return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
    }
    private static AircraftPartMotionEvent Motion(double time,float x)=>new AircraftPartMotionEvent {unitId=7,Time=time,blendSeconds=2,parts=new[]{new AircraftPartPose {
        partId=3,flags=AircraftPartFlags.Present,position=new GlobalPosition(x,0,0),rotation=new Quaternion {w=1}}}};
    private static SpawnEvent Spawn(double time)=>new SpawnEvent {unitId=7,ownerId=7,Time=time,jsonKey="aircraft",factionName="blue",rotation=new Quaternion {w=1}};
    private static void Return(Queue<IReplayEvent> events){while(events.Count>0)ReplayEventFactory.Return(events.Dequeue());}
    private static void Near(float actual,float expected){Check(Math.Abs(actual-expected)<.0001f,$"Expected {expected}; got {actual}");}
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Expect<T>(Action action)where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
}
