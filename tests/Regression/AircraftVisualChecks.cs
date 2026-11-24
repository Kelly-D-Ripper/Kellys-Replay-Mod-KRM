using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class AircraftVisualChecks
{
    public static int Run()
    {
        int passed=0;
        Test("aircraft visual layout and complete gear pose survive the file codec",()=>
        {
            var layout=new AircraftVisualLayoutEvent {unitId=4,Time=2,layoutId=9,channels=new[]{"gear:2:0:hinge","sweep:0:0:part","surface:3:0:mesh"}};
            var decoded=(AircraftVisualLayoutEvent)RollingReplayBuffer.Decode(RollingReplayBuffer.Encode(layout));
            Check(decoded.unitId==4&&decoded.Time==2&&decoded.layoutId==9&&decoded.channels.SequenceEqual(layout.channels),"layout aliases and identity retained");
            var state=State(2,7);state.gearDeployed=true;state.gearState=2;
            var restored=(AircraftVisualStateEvent)RollingReplayBuffer.Decode(RollingReplayBuffer.Encode(state));
            Check(restored.gearDeployed&&restored.gearState==2&&restored.poses.Length==1&&restored.poses[0].position.x==7,"gear and geometry retained");
            ReplayEventFactory.Return(decoded);ReplayEventFactory.Return(restored);
        });
        Test("aircraft visual codec rejects excess, invalid and truncated channels before playback",()=>
        {
            var state=State(0,0);state.poses=new AircraftVisualPose[257];Expect<InvalidDataException>(()=>RollingReplayBuffer.Encode(state));
            state=State(0,0);state.poses[0].position.x=float.NaN;Expect<InvalidDataException>(()=>RollingReplayBuffer.Encode(state));
            state=State(0,0);state.poses[0].rotation=new Quaternion();Expect<InvalidDataException>(()=>RollingReplayBuffer.Encode(state));
            var bytes=RollingReplayBuffer.Encode(State(0,0));Expect<EndOfStreamException>(()=>RollingReplayBuffer.Decode(bytes.Take(bytes.Length-1).ToArray()));
            var layout=new AircraftVisualLayoutEvent {channels=new[]{new string('x',641)}};Expect<InvalidDataException>(()=>RollingReplayBuffer.Encode(layout));
            layout.channels=new[]{"same","same"};Expect<InvalidDataException>(()=>RollingReplayBuffer.Decode(RollingReplayBuffer.Encode(layout)));
        });
        Test("aircraft visual timeline owns pooled arrays and holds a paused exact frame",()=>
        {
            var timeline=new AircraftVisualTimeline();var first=State(0,0);var next=State(2,20);
            timeline.Add(next);timeline.Add(first);first.poses[0].position.x=999;ReplayEventFactory.Return(first);ReplayEventFactory.Return(next);
            for(int i=0;i<100;i++)
            {
                Check(timeline.Locate(1,out var a,out var b,out float fraction),"frame remains available");
                Near(Vector3.Lerp(a.poses[0].position,b.poses[0].position,fraction).x,10);
                Check(a.gearState==1,"discrete gear comes from preceding frame");
            }
            Check(!timeline.Locate(-1,out _,out _,out _),"future geometry never leaks before first snapshot");
        });
        Test("unchanged aircraft geometry is suppressed without drifting across stationary gaps",()=>
        {
            var tracker=new AircraftVisualChangeTracker();var emitted=new List<AircraftVisualSnapshot>();
            for(int i=0;i<=1000;i++)tracker.Observe(AircraftVisualSnapshot.From(State(i*.1,0)),emitted.Add);
            Check(emitted.Count==1,"stationary aircraft needs one visual sample");
            tracker.Observe(AircraftVisualSnapshot.From(State(100.1,1)),emitted.Add);
            Check(emitted.Count==2&&emitted[1].time==100.1&&Math.Abs(emitted[1].blendSeconds-.1f)<.0001,"only current-time motion event with bounded blend duration");
            var timeline=new AircraftVisualTimeline();foreach(var sample in emitted)
            {var e=State(sample.time,sample.poses[0].position.x);e.blendSeconds=sample.blendSeconds;timeline.Add(e);}
            Check(timeline.Locate(50,out var a,out var b,out var fraction),"stationary gap available");Near(Vector3.Lerp(a.poses[0].position,b.poses[0].position,fraction).x,0);
        });
        Test("visual checkpoint includes layout before snapshot and discards a despawned lifetime",()=>
        {
            using var reducer=new ReplaySeekState();reducer.Add(new SpawnEvent {unitId=4,Time=0,jsonKey="plane",factionName="faction",weapons=new()});
            reducer.Add(new AircraftVisualLayoutEvent {unitId=4,Time=0,layoutId=9,channels=new[]{"gear:2:0:hinge"}});
            reducer.Add(State(1,5));using var checkpoint=reducer.Clone();reducer.Add(State(2,10));
            var queue=new Queue<IReplayEvent>();checkpoint.CopyTo(queue);var restored=queue.ToArray();
            int layoutIndex=Array.FindIndex(restored,e=>e is AircraftVisualLayoutEvent),stateIndex=Array.FindIndex(restored,e=>e is AircraftVisualStateEvent);
            Check(layoutIndex>=0&&stateIndex>layoutIndex,"layout precedes decoded geometry");Near(((AircraftVisualStateEvent)restored[stateIndex]).poses[0].position.x,5);
            ((AircraftVisualStateEvent)restored[stateIndex]).poses[0].position.x=99;
            while(queue.Count>0)ReplayEventFactory.Return(queue.Dequeue());checkpoint.CopyTo(queue);
            Near(queue.OfType<AircraftVisualStateEvent>().Single().poses[0].position.x,5);while(queue.Count>0)ReplayEventFactory.Return(queue.Dequeue());
            reducer.Add(new DespawnEvent {unitId=4,Time=3});Check(reducer.EstimatedBytes==0,"layout and visual state released with actor");
        });
        Test("aircraft visual lookahead is interpolation but cannot cross a spawn boundary",()=>
        {
            var buffer=new BoundedEventBuffer(8);var spawn=new SpawnEvent {unitId=4,Time=2};var state=State(2,1);
            Check(BoundedEventBuffer.IsInterpolation(state),"geometry participates in timeline lookahead");
            buffer.Add(spawn,CancellationToken.None);buffer.Add(state,CancellationToken.None);
            Check(!buffer.TryTakeIfReady(1.9,out _),"unborn actor geometry is held");
            Check(buffer.TryTakeIfReady(2,out var first)&&ReferenceEquals(first,spawn),"spawn remains first");buffer.MarkActionExecuted(first);ReplayEventFactory.Return(first);
            Check(buffer.TryTakeIfReady(2,out var second)&&ReferenceEquals(second,state),"geometry follows its lifetime");ReplayEventFactory.Return(second);buffer.ClearAndReturnToPool();
        });
        Test("rolling clip visual cut samples exact motion and preserves a stationary lead-in",()=>
        {
            var previous=AircraftVisualSnapshot.From(State(0,0));var next=AircraftVisualSnapshot.From(State(10,100));next.blendSeconds=2;
            Near(AircraftVisualSnapshot.At(previous,next,7).poses[0].position.x,0);
            var cut=AircraftVisualSnapshot.At(previous,next,8.5);Near(cut.poses[0].position.x,25);Check(cut.time==8.5&&cut.gearState==previous.gearState,"cut carries preceding discrete state");
            // Rebase the cut and next timestamp while keeping transition duration.
            cut.time=0;next.time=1.5;Near(AircraftVisualSnapshot.At(cut,next,.5).poses[0].position.x,50);
            cut.poses[0].position.x=99;Near(previous.poses[0].position.x,0);
            next.layoutId=12;Expect<ArgumentException>(()=>AircraftVisualSnapshot.At(previous,next,1));
        });
        return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
    }
    private static AircraftVisualStateEvent State(double time,float x)=>new AircraftVisualStateEvent {unitId=4,layoutId=9,Time=time,gearState=1,blendSeconds=2,
        poses=new[]{new AircraftVisualPose {position=new Vector3(x,0,0),rotation=new Quaternion {w=1}}}};
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Near(float value,float expected){Check(Math.Abs(value-expected)<.0001f,"expected "+expected+", got "+value);}
    private static void Expect<T>(Action action)where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
}
