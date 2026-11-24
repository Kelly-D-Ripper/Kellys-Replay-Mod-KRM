using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class CountermeasureChecks
{
    public static int Run()
    {
        int passed=0;
        Test("format7 countermeasure launch/sample/end roundtrip preserves resolved trajectories",()=>
        {
            foreach(var source in new[]{Launch(1),Sample(1,2),End(1,3)})
            {
                var frame=ReplayRecordTransport.EncodeEvent(source);
                using var stream=new MemoryStream(frame);using var reader=new BinaryReader(stream);
                var result=(CountermeasureEvent)ReplayRecordTransport.ReadEvent(reader,7);
                Check(result.countermeasureId==source.countermeasureId&&result.phase==source.phase,"identity and lifecycle survive framing");
                Near(result.position.x,source.position.x);Near(result.velocity.x,source.velocity.x);Near(result.endAge,source.endAge);
                Check(result.Time==source.Time&&stream.Position==stream.Length,"whole CRC event consumed");
                if(source.phase==CountermeasurePhase.Launch)Check(result.unitKey=="test_aircraft"&&result.prefabName=="NativeFlare"&&result.prefabSlot==2,"native prefab address retained");
                ReplayEventFactory.Return(result);
            }
        });
        Test("countermeasure codec bounds identities histories and corrupt payloads",()=>
        {
            var value=Launch(1);value.countermeasureId=0;Throws(()=>RollingReplayBuffer.Encode(value));
            value=Launch(1);value.unitKey=new string('x',513);Throws(()=>RollingReplayBuffer.Encode(value));
            value=Launch(1);value.prefabSlot=1024;Throws(()=>RollingReplayBuffer.Encode(value));
            value=Launch(1);value.phase=(CountermeasurePhase)99;Throws(()=>RollingReplayBuffer.Encode(value));
            value=Launch(1);value.rotation=default;Throws(()=>RollingReplayBuffer.Encode(value));
            value=Launch(1);value.velocity.x=float.NaN;Throws(()=>RollingReplayBuffer.Encode(value));
            value=Launch(1);value.history=new CountermeasureSnapshot[321];Throws(()=>RollingReplayBuffer.Encode(value));
            value=Launch(1);value.history=new[]{Sample(1,1).Snapshot};Throws(()=>RollingReplayBuffer.Encode(value));
            value=End(1,2);value.remaining=1;Throws(()=>RollingReplayBuffer.Encode(value));
            var bytes=RollingReplayBuffer.Encode(Launch(1));Expect<EndOfStreamException>(()=>RollingReplayBuffer.Decode(bytes.Take(bytes.Length-1).ToArray()));
            var frame=ReplayRecordTransport.EncodeEvent(Launch(1));frame[frame.Length-4]^=8;
            Throws(()=>{using var reader=new BinaryReader(new MemoryStream(frame));ReplayRecordTransport.ReadEvent(reader,7);});
        });
        Test("flare trajectory samples interpolate observed motion and hold collision positions",()=>
        {
            var launch=Launch(5);var timeline=new CountermeasureTimeline(launch);
            var a=Sample(5,1);a.position.x=100;a.velocity.x=0;timeline.Add(a);
            Check(timeline.TryAt(.5,out var half),"intermediate pose exists");Near(half.position.x,50);Near(half.velocity.x,50);
            Check(timeline.TryAt(1.25,out var held),"last pose remains available");Near(held.position.x,100);Near(held.remaining,a.remaining-.25f);
            Check(!timeline.TryAt(-.01,out _),"future launch cannot appear early");
            var collision=Sample(5,2);collision.position.x=100;collision.velocity.x=0;timeline.Add(collision);
            Check(timeline.TryAt(1.75,out var stopped),"observed stopped flare stays available");Near(stopped.position.x,100);
        });
        Test("countermeasure seek state survives owner death and native actor ID reuse",()=>
        {
            using var state=new ReplaySeekState();state.Add(Spawn(7,0));state.Add(Launch(7,7));state.Add(Sample(7,1));
            state.Add(new DespawnEvent {unitId=7,Time=1.1});state.Add(Spawn(7,1.2));state.Add(Launch(8,7,1.2));state.Add(Sample(7,2));
            var restored=new Queue<IReplayEvent>();state.CopyTo(restored);
            var flares=restored.OfType<CountermeasureEvent>().ToArray();Check(flares.Length==2,"both flare lifetimes survive owner boundary");
            Check(flares.Any(e=>e.countermeasureId==7&&e.age==2),"old flare keeps its own trajectory");
            Check(flares.Any(e=>e.countermeasureId==8&&e.unitId==7),"new aircraft can deploy a different flare");Return(restored);
        });
        Test("seek snapshots own histories across pooling and repeated rewinds",()=>
        {
            using var original=new ReplaySeekState();original.Add(Launch(11));for(int i=1;i<=100;i++)original.Add(Sample(11,i*.05));
            original.Add(End(11,5.1));
            for(int attempt=0;attempt<40;attempt++)
            {
                using var clone=original.Clone();var queue=new Queue<IReplayEvent>();clone.CopyTo(queue);
                var snapshot=queue.OfType<CountermeasureEvent>().Single();Check(snapshot.HasValidPayload()&&snapshot.history.Length==102,"complete independent trail snapshot");
                var playback=new CountermeasureTimeline(snapshot,true);Check(playback.TryAt(4.95,out var pose),"rewound trajectory can be sampled");Near(pose.position.x,495);
                snapshot.history[0].position.x=99999;Return(queue);
                var untouched=new Queue<IReplayEvent>();original.CopyTo(untouched);Near(untouched.OfType<CountermeasureEvent>().Single().history[0].position.x,0);Return(untouched);
            }
        });
        Test("countermeasure trails expire independently and no unknown sample creates a flare",()=>
        {
            var state=new ReplayCountermeasureState();state.Add(Sample(99,1));state.Add(End(99,2));Check(state.Count==0,"orphan samples cannot invent launches");
            state.Add(Launch(1));state.Add(End(1,2));state.Expire(16.999);Check(state.Count==1,"native smoke remains during15s tail");
            state.Expire(17);Check(state.Count==0&&state.EstimatedBytes==0,"expired tail is removed at its boundary");
            state.Add(Launch(2));state.Clear();Check(state.Count==0&&state.EstimatedBytes==0,"new session contains no previous flare");
        });
        Test("active and residual countermeasure populations and dense histories stay bounded",()=>
        {
            var state=new ReplayCountermeasureState();
            for(uint id=1;id<=CountermeasureTimeline.MaxActive;id++){state.Add(Launch(id));state.Add(End(id,1));}
            Check(state.Count==512,"residual smoke consumes the same active budget");Throws(()=>state.Add(Launch(513,7,2)));
            state.Expire(16);state.Add(Launch(514,7,16));Check(state.Count==1,"capacity becomes reusable after tails expire");
            var timeline=new CountermeasureTimeline(Launch(9));for(int i=1;i<=2000;i++)timeline.Add(Sample(9,i*.01));
            Check(timeline.SampleCount<=320&&timeline.SnapshotEvent().HasValidPayload(),"dense trajectories obey serialized bounds");
        });
        Test("sparse countermeasure histories never poison checkpoint clone or rolling snapshots",()=>
        {
            var value=Launch(3);value.remaining=300;var state=new ReplayCountermeasureState();state.Add(value);
            var later=Sample(3,30);later.remaining=270;state.Add(later);
            var copy=new ReplayCountermeasureState();state.CloneTo(copy);var queue=new Queue<IReplayEvent>();copy.CopyTo(queue);
            var snapshot=queue.OfType<CountermeasureEvent>().Single();Check(snapshot.HasValidPayload()&&snapshot.history.All(p=>p.time>=14.8),"old sparse anchor does not exceed history window");
            var raw=RollingReplayBuffer.Encode(snapshot);var decoded=(CountermeasureEvent)RollingReplayBuffer.Decode(raw);Check(decoded.HasValidPayload(),"checkpoint remains decodable");ReplayEventFactory.Return(decoded);Return(queue);
        });
        Test("playback lookahead preserves displayed15s trail while exports remain bounded",()=>
        {
            var launch=Launch(5);launch.remaining=60;var timeline=new CountermeasureTimeline(launch,true);
            for(int i=1;i<=480;i++){var sample=Sample(5,i*.05);sample.remaining=60-sample.age;timeline.Add(sample);}
            Check(timeline.TryAt(5.05,out var old),"displayed20s keeps trail back to5s despite samples queued through24s");Near(old.position.x,505);
            Check(timeline.SampleCount<=410,"future retention is separately bounded");var snapshot=timeline.SnapshotEvent();
            Check(snapshot.history.Length<=320&&snapshot.HasValidPayload(),"runtime snapshot never leaks enlarged future window into codec");
        });
        Test("rolling cuts interpolate flares and preserve smoke history before clip zero",()=>
        {
            using var rolling=new RollingReplayBuffer(10);rolling.Add(Launch(1));rolling.Add(Sample(1,1));rolling.Add(Sample(1,2));rolling.Add(Sample(1,3));rolling.Trim(12.5);
            var decoded=rolling.Snapshot(12.5).Select(RollingReplayBuffer.Decode).ToList();
            try
            {
                var baseline=decoded.OfType<CountermeasureEvent>().First();Check(baseline.phase==CountermeasurePhase.Launch&&baseline.Time==0,"self-contained clip starts with live flare");
                Near(baseline.position.x,250);Near(baseline.age,2.5f);Check(baseline.history.Any(p=>p.time<0),"precut smoke samples retained as relative history");
                var timeline=new CountermeasureTimeline(baseline,true);Check(timeline.TryAt(-1.5,out var trail),"particle warmup can read trail emitted before clip start");Near(trail.position.x,100);
                Check(timeline.TryAt(0,out var first),"first clip frame exact");Near(first.position.x,250);
                Check(decoded.OfType<CountermeasureEvent>().Last().Time==.5,"future trajectory rebases exactly");
            }
            finally{foreach(var item in decoded)ReplayEventFactory.Return(item);}
        });
        Test("rolling cuts preserve ended trail lifetime after aircraft disappears",()=>
        {
            using var rolling=new RollingReplayBuffer(10);rolling.Add(Spawn(7,0));rolling.Add(Launch(1));rolling.Add(End(1,2));rolling.Add(new DespawnEvent {unitId=7,Time=3});rolling.Trim(15);
            var decoded=rolling.Snapshot(15).Select(RollingReplayBuffer.Decode).ToList();
            try
            {
                Check(!decoded.OfType<SpawnEvent>().Any(),"dead owner is absent");var flare=decoded.OfType<CountermeasureEvent>().Single();
                Check(flare.Time==0&&flare.endAge==2,"burnout age preserved across cut");Near(flare.age,5);
                var timeline=new CountermeasureTimeline(flare);Check(!timeline.Burning(0)&&timeline.TryAt(0,out _),"only fading smoke is restored");
                Check(timeline.ExpiresAt==12,"tail expires at source17 minus cut5, not15s after clip start");
            }
            finally{foreach(var item in decoded)ReplayEventFactory.Return(item);}
            var expired=rolling.Snapshot(27).Select(RollingReplayBuffer.Decode).ToList();Check(!expired.OfType<CountermeasureEvent>().Any(),"expired precut trail is omitted");foreach(var item in expired)ReplayEventFactory.Return(item);
        });
        Test("countermeasure lookahead cannot cross launch/end or collide with actor boundaries",()=>
        {
            var buffer=new BoundedEventBuffer(12);var launch=Launch(7,99,2);var future=Sample(7,2.1);buffer.Add(launch,default);buffer.Add(future,default);
            Check(!buffer.TryTakeIfReady(1,out _),"future trajectory waits for actual launch action");
            Check(buffer.TryTakeIfReady(2,out var taken)&&ReferenceEquals(taken,launch),"launch executes first");buffer.MarkActionExecuted(taken);ReplayEventFactory.Return(taken);
            Check(buffer.TryTakeIfReady(2,out taken)&&ReferenceEquals(taken,future),"own launched trajectory may look ahead");ReplayEventFactory.Return(taken);
            var actorBirth=Spawn(7,3);var flareSample=Sample(7,3.1);buffer.Add(actorBirth,default);buffer.Add(flareSample,default);
            Check(buffer.TryTakeIfReady(2,out taken)&&ReferenceEquals(taken,flareSample),"unrelated actor7 does not block flare7");ReplayEventFactory.Return(taken);
            Check(buffer.TryTakeIfReady(3,out taken)&&ReferenceEquals(taken,actorBirth),"unrelated actor birth remains ordered");buffer.MarkActionExecuted(taken);ReplayEventFactory.Return(taken);
            var end=End(7,3.5);var illegal=Sample(7,4);buffer.Add(end,default);buffer.Add(illegal,default);
            Check(!buffer.TryTakeIfReady(3,out _),"post-end future samples remain behind end action");
            Check(buffer.TryTakeIfReady(3.5,out taken)&&ReferenceEquals(taken,end),"end action remains due in file order");buffer.MarkActionExecuted(taken);ReplayEventFactory.Return(taken);
            buffer.ClearAndReturnToPool();Check(buffer.Count==0&&buffer.FreeSpace==12,"held samples and lifecycle slots return once");
        });
        Console.WriteLine($"{passed} countermeasure regression groups passed");return passed;
        void Test(string name,Action body){body();passed++;Console.WriteLine("PASS "+name);}
    }
    private static CountermeasureEvent Launch(uint id,uint owner=7,double time=0)=>new CountermeasureEvent
    {countermeasureId=id,unitId=owner,Time=time,phase=CountermeasurePhase.Launch,kind=CountermeasureKind.Flare,unitKey="test_aircraft",prefabName="NativeFlare",prefabSlot=2,
        position=new GlobalPosition((float)time*100,100,0),velocity=new Vector3(100,0,0),rotation=new Quaternion {w=1},remaining=30,tailSeconds=15};
    private static CountermeasureEvent Sample(uint id,double time)=>new CountermeasureEvent
    {countermeasureId=id,Time=time,phase=CountermeasurePhase.Sample,position=new GlobalPosition((float)time*100,100,0),velocity=new Vector3(100,0,0),rotation=new Quaternion {w=1},
        age=(float)time,remaining=Math.Max(0,30-(float)time),tailSeconds=15};
    private static CountermeasureEvent End(uint id,double time)
    {var value=Sample(id,time);value.phase=CountermeasurePhase.End;value.remaining=0;value.endAge=value.age;return value;}
    private static SpawnEvent Spawn(uint id,double time)=>new SpawnEvent {unitId=id,ownerId=id,Time=time,jsonKey="test_aircraft",factionName="test",isAircraft=true,rotation=new Quaternion {w=1},weapons=new List<WeaponMount>()};
    private static void Return(Queue<IReplayEvent> values){while(values.Count>0)ReplayEventFactory.Return(values.Dequeue());}
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Near(float actual,float expected){if(Math.Abs(actual-expected)>.002)throw new Exception($"Expected {expected}, got {actual}");}
    private static void Throws(Action action)=>Expect<InvalidDataException>(action);
    private static void Expect<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
}
