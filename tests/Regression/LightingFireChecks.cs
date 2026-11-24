using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class LightingFireChecks
{
    public static int Run()
    {
        int passed=0;
        Test("recorded auto landing lamps retract while explicit airborne lighting remains available",()=>
        {
            Check(!AircraftLightingPolicy.Resolve(true,true,true,AircraftLampControl.Auto,false,0),"stale auto lamp cannot project with retracted gear");
            Check(!AircraftLightingPolicy.Resolve(true,true,true,AircraftLampControl.Never,false,0),"nonmanual gear lamp still follows gear restriction");
            Check(AircraftLightingPolicy.Resolve(true,true,true,AircraftLampControl.ForceOn,false,0),"recorded pilot ForceOn remains airborne");
            Check(AircraftLightingPolicy.Resolve(true,true,false,AircraftLampControl.Auto,false,0),"navigation lamp is independent of landing gear");
            Check(AircraftLightingPolicy.Resolve(false,true,true,AircraftLampControl.Auto,false,1),"explicit presentation On follows native ForceOn semantics");
            Check(!AircraftLightingPolicy.Resolve(true,false,true,AircraftLampControl.ForceOn,true,1),"broken/detached physical light cannot be forced back");
            Check(!AircraftLightingPolicy.Resolve(true,true,false,AircraftLampControl.ForceOn,true,2),"presentation Off always hides selected light");
            Check(AircraftLightingPolicy.Resolve(true,true,true,AircraftLampControl.Unknown,false,0),"unknown legacy control is not invented");
            Check(AircraftLightingPolicy.GearAutoOn(2)&&AircraftLightingPolicy.GearAutoOn(4)&&!AircraftLightingPolicy.GearAutoOn(1)&&!AircraftLightingPolicy.GearAutoOn(3),"native extended/extending enum states only");
        });
        Test("lighting codec preserves distinct lamps and rejects ambiguous layouts",()=>
        {
            var value=Lights(1);var copy=(AircraftLightingEvent)RollingReplayBuffer.Decode(RollingReplayBuffer.Encode(value));
            Check(copy.flags.SequenceEqual(value.flags)&&copy.controls.SequenceEqual(value.controls)&&copy.keys.SequenceEqual(value.keys),"observed state and actual control mode roundtrip");ReplayEventFactory.Return(copy);
            value=Lights(1);value.keys[1]=value.keys[0];Throws(()=>RollingReplayBuffer.Encode(value));
            value=Lights(1);value.controls[0]=4;Throws(()=>RollingReplayBuffer.Encode(value));
            value=Lights(1);value.flags[0]=128;Throws(()=>RollingReplayBuffer.Encode(value));
            value=Lights(1);value.keys[0]=new string('a',513);Throws(()=>RollingReplayBuffer.Encode(value));
            value=Lights(1);value.keys=new string[257];Throws(()=>RollingReplayBuffer.Encode(value));
        });
        Test("lighting seek snapshots return recorded baseline after overrides and actor reuse",()=>
        {
            using var state=new ReplaySeekState();state.Add(Spawn(7,0));state.Add(Lights(1));using var clone=state.Clone();
            var queue=new Queue<IReplayEvent>();clone.CopyTo(queue);var light=queue.OfType<AircraftLightingEvent>().Single();
            Check(light.Time==1&&light.controls[0]==(byte)AircraftLampControl.Auto,"baseline retained independently of presentation override");light.flags[0]=0;Return(queue);
            var untouched=new Queue<IReplayEvent>();state.CopyTo(untouched);Check(untouched.OfType<AircraftLightingEvent>().Single().flags[0]!=0,"pooled snapshot cannot mutate baseline");Return(untouched);
            state.Add(new DespawnEvent {unitId=7,Time=2});state.Add(Spawn(7,3));var reused=new Queue<IReplayEvent>();state.CopyTo(reused);Check(!reused.OfType<AircraftLightingEvent>().Any(),"old lighting cannot cross actor lifetime");Return(reused);
        });
        Test("fuel flame codec retains observed attachment emission fade and light without gameplay",()=>
        {
            foreach(var original in new[]{Begin(1),FireSample(1,1),FireEnd(1,2)})
            {
                var frame=ReplayRecordTransport.EncodeEvent(original);using var reader=new BinaryReader(new MemoryStream(frame));var decoded=(FuelFlameStateEvent)ReplayRecordTransport.ReadEvent(reader,8);
                Check(decoded.flameId==original.flameId&&decoded.phase==original.phase&&decoded.Time==original.Time,"instance lifecycle roundtrip");
                if(original.phase!=FuelFlamePhase.End)
                {Near(decoded.snapshot.position.x,original.snapshot.position.x);Check(decoded.snapshot.emittingMask==3&&decoded.snapshot.opacity.SequenceEqual(original.snapshot.opacity),"observed particle state retained");}
                ReplayEventFactory.Return(decoded);
            }
            var value=Begin(2);value.snapshot.opacity=new float[33];Throws(()=>RollingReplayBuffer.Encode(value));
            value=Begin(2);value.snapshot.speed=float.NaN;Throws(()=>RollingReplayBuffer.Encode(value));
            value=Begin(2);value.snapshot.opacity[0]=float.PositiveInfinity;Throws(()=>RollingReplayBuffer.Encode(value));
            value=Begin(2);value.snapshot.emittingMask=4;Throws(()=>RollingReplayBuffer.Encode(value));
            value=Begin(2);value.unitKey=new string('a',513);Throws(()=>RollingReplayBuffer.Encode(value));
            var bytes=RollingReplayBuffer.Encode(Begin(2));Expect<EndOfStreamException>(()=>RollingReplayBuffer.Decode(bytes.Take(bytes.Length-1).ToArray()));
        });
        Test("fuel flames hold quiet gaps and advance age on the exact paused/seek frame",()=>
        {
            var a=Begin(1).snapshot;var b=FireSample(1,10).snapshot;b.blendSeconds=.05f;
            var held=FuelFlameTimeline.At(a,b,9);Near(held.position.x,0);Near(held.age,9);
            var moving=FuelFlameTimeline.At(a,b,9.975);Near(moving.position.x,50);Near(moving.lightIntensity,2);
            var timeline=new FuelFlameTimeline(Begin(1));var sample=FireSample(1,1);timeline.Add(sample);ReplayEventFactory.Return(sample);
            for(int i=0;i<100;i++){Check(timeline.TryAt(.5,out var pose),"paused frame available");Near(pose.position.x,5);Near(pose.age,.5f);}
            Check(!timeline.TryAt(-.1,out _),"future fire does not appear before beginning");
        });
        Test("fuel flames change attachment and emission only at their observed moment",()=>
        {
            var a=Begin(1).snapshot;var b=FireSample(1,1).snapshot;b.attached=false;b.emittingMask=0;b.position.x=500;
            var before=FuelFlameTimeline.At(a,b,.99);Check(before.attached&&before.emittingMask==3,"future detached fade cannot hide flame early");Near(before.position.x,0);Near(before.age,.99f);
            var after=FuelFlameTimeline.At(a,b,1);Check(!after.attached&&after.emittingMask==0,"native detach/stop is discrete");Near(after.position.x,500);
        });
        Test("fuel fire seek warmup never fabricates stopped or fully faded particles",()=>
        {
            var state=FireSample(1,30).snapshot;Near(FuelFlameWarmPolicy.Duration(state,0),5);
            state.emittingMask=0;Near(FuelFlameWarmPolicy.Duration(state,0),0);
            state.emittingMask=3;state.opacity[0]=0;Near(FuelFlameWarmPolicy.Duration(state,0),0);
            state.opacity[0]=-.5f;Near(FuelFlameWarmPolicy.Duration(state,0),0);
            state.opacity[0]=1;state.age=.25f;Near(FuelFlameWarmPolicy.Duration(state,0),.25f);
            Near(FuelFlameWarmPolicy.Duration(state,2),0);state.age=float.NaN;Near(FuelFlameWarmPolicy.Duration(state,0),0);
        });
        Test("fuel fire reducer survives owner despawn with independent pooled snapshots",()=>
        {
            using var state=new ReplaySeekState();state.Add(Spawn(7,0));state.Add(Begin(1));state.Add(FireSample(1,1));state.Add(new DespawnEvent {unitId=7,Time=1.1});
            var orphan=FireSample(1,2);orphan.snapshot.attached=false;orphan.snapshot.emittingMask=0;state.Add(orphan);
            for(int i=0;i<40;i++)
            {
                using var clone=state.Clone();var queue=new Queue<IReplayEvent>();clone.CopyTo(queue);var effect=queue.OfType<FuelFlameStateEvent>().Single();
                Check(!effect.snapshot.attached&&effect.snapshot.emittingMask==0&&effect.phase==FuelFlamePhase.Begin,"detached fading smoke survives without resurrecting owner");
                effect.snapshot.opacity[0]=999;Return(queue);
            }
            var intact=new Queue<IReplayEvent>();state.CopyTo(intact);Near(intact.OfType<FuelFlameStateEvent>().Single().snapshot.opacity[0],1);Return(intact);
            state.Add(FireEnd(1,3));var ended=new Queue<IReplayEvent>();state.CopyTo(ended);Check(!ended.OfType<FuelFlameStateEvent>().Any(),"actual native object end removes checkpoint state");Return(ended);
        });
        Test("fuel fire checkpoint admission remains bounded and reuses capacity after actual end",()=>
        {
            var state=new ReplayFuelFlameState();state.Add(FireSample(99,1));Check(state.Count==0,"unknown sample cannot invent fire");
            for(uint i=1;i<=FuelFlameTimeline.MaxActive;i++)state.Add(Begin(i));Check(state.Count==256,"hard active bound");Throws(()=>state.Add(Begin(257)));
            state.Add(FireEnd(1,2));state.Add(Begin(258,2));Check(state.Count==256,"same-time end releases one slot before next begin");
            long size=state.EstimatedBytes;state.Add(FireSample(258,3));Check(state.EstimatedBytes==size,"steady sample storage does not accumulate history");
            var clone=new ReplayFuelFlameState();state.CloneTo(clone);Check(clone.EstimatedBytes==size&&clone.Count==state.Count,"clone accounting exact");clone.Clear();state.Clear();Check(state.EstimatedBytes==0&&clone.EstimatedBytes==0,"cleanup releases all instance references");
        });
        Test("same-time actor replacement cannot inherit an older checkpoint fire attachment",()=>
        {
            using var state=new ReplaySeekState();state.Add(Spawn(7,0));state.Add(Begin(1));state.Add(new DespawnEvent {unitId=7,Time=0});state.Add(Spawn(7,0));
            var snapshot=new Queue<IReplayEvent>();state.CopyTo(snapshot);
            Check(!snapshot.OfType<FuelFlameStateEvent>().Single().snapshot.attached,"despawn detaches prior lifetime even when replacement birth timestamp is identical");Return(snapshot);
        });
        Test("fuel fire lookahead has a hard sample memory bound",()=>
        {
            var timeline=new FuelFlameTimeline(Begin(1));for(int i=1;i<FuelFlameTimeline.MaxSamples;i++)timeline.Add(FireSample(1,i*.001));
            Throws(()=>timeline.Add(FireSample(1,1)));Check(timeline.TryAt(0,out _),"original presentation anchor remains intact before error cleanup");
        });
        Test("rolling fuel fire cuts retain exact age pose fade and owner lifetime",()=>
        {
            using var rolling=new RollingReplayBuffer(10);rolling.Add(Spawn(7,0));rolling.Add(Begin(1));rolling.Add(FireSample(1,1));rolling.Add(FireSample(1,2));rolling.Add(FireSample(1,3));
            var decoded=rolling.Snapshot(12.5).Select(RollingReplayBuffer.Decode).ToList();
            try
            {var baseline=decoded.OfType<FuelFlameStateEvent>().First();Near(baseline.snapshot.position.x,25);Near(baseline.snapshot.age,2.5f);Check(baseline.Time==0&&baseline.ownerBirth==-2.5&&baseline.snapshot.attached,"cut clock and owner birth rebase together");}
            finally{foreach(var value in decoded)ReplayEventFactory.Return(value);}
        });
        Test("rolling detached fuel flames cannot attach to a reused precut actor ID",()=>
        {
            using var rolling=new RollingReplayBuffer(10);rolling.Add(Spawn(7,0));rolling.Add(Begin(1));rolling.Add(FireSample(1,1));rolling.Add(new DespawnEvent {unitId=7,Time=2});rolling.Add(Spawn(7,3));rolling.Add(FireSample(1,4));
            var decoded=rolling.Snapshot(15).Select(RollingReplayBuffer.Decode).ToList();
            try{var old=decoded.OfType<FuelFlameStateEvent>().Single();Check(!old.snapshot.attached,"old owner lifetime differs from current actor before timestamps collapse to clip zero");}
            finally{foreach(var value in decoded)ReplayEventFactory.Return(value);}
        });
        Test("fuel flame samples cannot bypass begin/end and do not collide with flare IDs",()=>
        {
            var buffer=new BoundedEventBuffer(10);var begin=Begin(7,2);var sample=FireSample(7,2.1);buffer.Add(begin,default);buffer.Add(sample,default);
            Check(!buffer.TryTakeIfReady(1,out _),"future samples wait behind fire birth");Check(buffer.TryTakeIfReady(2,out var taken)&&ReferenceEquals(taken,begin),"birth first");buffer.MarkActionExecuted(taken);ReplayEventFactory.Return(taken);
            Check(buffer.TryTakeIfReady(2,out taken)&&ReferenceEquals(taken,sample),"launched fire allows trajectory lookahead");ReplayEventFactory.Return(taken);
            var flare=new CountermeasureEvent {countermeasureId=7,phase=CountermeasurePhase.Launch,Time=3};buffer.Add(flare,default);sample=FireSample(7,3.1);buffer.Add(sample,default);
            Check(buffer.TryTakeIfReady(2,out taken)&&ReferenceEquals(taken,sample),"countermeasure7 cannot block fire7");ReplayEventFactory.Return(taken);
            Check(buffer.TryTakeIfReady(3,out taken)&&ReferenceEquals(taken,flare),"unrelated flare birth remains ordered");buffer.MarkActionExecuted(taken);ReplayEventFactory.Return(taken);
            buffer.Add(FireEnd(7,4),default);buffer.Add(FireSample(7,4.1),default);Check(!buffer.TryTakeIfReady(3.5,out _),"post-end samples cannot overtake actual end");buffer.ClearAndReturnToPool();Check(buffer.FreeSpace==10,"held ownership releases once");
        });
        Console.WriteLine($"{passed} lighting/fire regression groups passed");return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
    }
    private static AircraftLightingEvent Lights(double time)=>new AircraftLightingEvent {unitId=7,Time=time,keys=new[]{"root/nav:0:0","root/nav:0:1"},
        flags=new[]{(byte)(AircraftLampFlags.On|AircraftLampFlags.Usable|AircraftLampFlags.Gear),(byte)(AircraftLampFlags.On|AircraftLampFlags.Usable)},controls=new[]{(byte)AircraftLampControl.Auto,(byte)AircraftLampControl.ForceOn}};
    private static FuelFlameStateEvent Begin(uint id,double time=0)
    {var value=FireSample(id,time);value.phase=FuelFlamePhase.Begin;value.unitId=7;value.partId=3;value.prefabSlot=2;value.unitKey="test_aircraft";value.prefabName="NativeFuelFire";value.ownerBirth=0;return value;}
    private static FuelFlameStateEvent FireSample(uint id,double time)=>new FuelFlameStateEvent {flameId=id,phase=FuelFlamePhase.Sample,snapshot=new FuelFlameSnapshot {time=time,attached=true,position=new GlobalPosition((float)time*10,100,0),rotation=new Quaternion {w=1},localRotation=new Quaternion {w=1},
        age=(float)time,speed=100,lightIntensity=(float)time/5+1,emittingMask=3,opacity=new[]{1f,.5f},blendSeconds=(float)time}};
    private static FuelFlameStateEvent FireEnd(uint id,double time)=>new FuelFlameStateEvent {flameId=id,phase=FuelFlamePhase.End,Time=time};
    private static SpawnEvent Spawn(uint id,double time)=>new SpawnEvent {unitId=id,ownerId=id,Time=time,isAircraft=true,jsonKey="test_aircraft",factionName="test",weapons=new List<WeaponMount>(),rotation=new Quaternion {w=1}};
    private static void Return(Queue<IReplayEvent> queue){while(queue.Count>0)ReplayEventFactory.Return(queue.Dequeue());}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Near(float actual,float expected){if(Math.Abs(actual-expected)>.003)throw new Exception($"Expected {expected}, got {actual}");}
    private static void Throws(Action action)=>Expect<InvalidDataException>(action);
    private static void Expect<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
}
