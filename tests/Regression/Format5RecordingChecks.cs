using System.Collections.Concurrent;
using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

internal static class Format5RecordingChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("format 5 event payloads preserve door, world, gun address and independent part motion",()=>
        {
            Require((byte)EventType.HangarDoorState==24&&(byte)EventType.WorldEnvironmentState==25&&
                (byte)EventType.GunBullet==26&&(byte)EventType.AircraftPartMotion==27,"format 5 wire IDs stay append-only");
            var door=RoundTrip(Door(3,100,.6f,.25f,2));
            Near(door.openAmount,.6f);Near(door.currentSpeed,.25f);Near(door.blendSeconds,2);
            Require(door.hangarPath=="0/1@0"&&door.opening&&door.available&&!door.selfDisabled&&door.unitId==100,"door identity and discrete flags");
            var world=RoundTrip(World(3,23,.2f,30));
            Near(world.timeOfDay,23);Near(world.windOffset.z,32);Near(world.cloudDisplacement.y,60);
            Require(world.moonPhase==27&&world.hasCloudPhase,"world phase fields");
            var gun=RoundTrip(Shot(3,2));
            Require(gun.station==2&&gun.weaponSlot==1&&gun.partId==4&&gun.partGunSlot==3&&gun.weaponName=="MP cannon α"&&gun.sound&&gun.tracer&&gun.explosive,"native station and complex-part gun address");
            Near(gun.velocity.z,900);Near(gun.color.g,.4f);Near(gun.reliability,.95f);
            var part=RoundTrip(Parts(3,2,100,2,AircraftPartFlags.Present|AircraftPartFlags.Detached));
            Require(part.unitId==2&&part.parts.Length==2&&part.parts[1].partId==9&&part.parts[1].flags==(AircraftPartFlags.Present|AircraftPartFlags.Detached),"independent detached part identity");
            Near(part.parts[0].position.x,100);Near(part.parts[1].position.x,105);Near(part.blendSeconds,2);
            Return(door,world,gun,part);
        });
        test("format 5 multiplayer recording survives 204 alternating checkpoint restores",()=>
        {
            string path=Path.Combine(Path.GetTempPath(),"replay-v5-"+Guid.NewGuid().ToString("N")+".replay");
            try
            {
                using var events=new BlockingCollection<IReplayEvent>();
                events.Add(World(0,23,.2f,0));events.Add(Spawn(100,0,"hangar","blue"));
                events.Add(Spawn(1,0,"client-blue-jet","blue"));events.Add(Layout(1,0,11));
                events.Add(Spawn(2,0,"remote-red-jet","red"));events.Add(Layout(2,0,22));
                for(int n=0;n<=2400;n++)
                {
                    double time=n*.05;
                    foreach(uint unit in new[]{1u,2u})
                    {
                        float x=(float)(time*20)+(unit==1?0:200);
                        events.Add(new UpdatePositionEvent{Time=time,unitId=unit,position=new GlobalPosition(x,1000,20),rotation=Identity,velocity=new Vector3(20,0,0)});
                        events.Add(Window(time,unit,unit==1?11u:22u,(float)(n%100)/100,.05f));
                        events.Add(Parts(time,unit,x,.05f,n>=1200?AircraftPartFlags.Present|AircraftPartFlags.Detached:AircraftPartFlags.Present));
                    }
                    if(n%10==0)
                    {
                        var weather=World(time,WorldStateSnapshot.Cycle(23+(float)time*.02f,24),time<60?.2f:.8f,(float)time*10);
                        weather.moonPhase=WorldStateSnapshot.Cycle(27+(float)time*.01f,28);events.Add(weather);
                        float phase=(float)(n%400)/200;
                        var door=Door(time,100,phase<=1?phase:2-phase,.1f,.5f);door.opening=phase<1;events.Add(door);
                    }
                    if(n%20==0){events.Add(Shot(time,1));events.Add(Shot(time,2));}
                    if(n==1200)
                    {
                        events.Add(Spawn(3,time,"remote-missile","red"));
                        events.Add(new MissileDetonationEvent{Time=time,unitId=3,position=new GlobalPosition(1300,1000,20),normal=new Vector3(0,1,0),armed=true,hitArmor=true});
                        events.Add(new DespawnEvent{Time=time,unitId=3});
                    }
                }
                long count=events.Count;events.CompleteAdding();
                using(var file=File.Create(path))using(var writer=new BinaryWriter(file))
                {
                    writer.Write(12549);writer.Write((byte)5);writer.Write((byte)0);writer.Write((byte)0);
                    writer.Write((byte)0);writer.Write("generated-multiplayer-map");writer.Write(DateTime.UtcNow.Ticks);writer.Write(0);
                    writer.Write(120d);writer.Write(count);writer.Write((ushort)0);
                    Require(ReplayFileWriter.Drain(events,writer)==count,"actual written event count");
                }
                RealReplayAudit.AuditGenerated(path);
            }
            finally{if(File.Exists(path))File.Delete(path);}
        });
        test("format 5 rolling clip starts midway through door, canopy, part and world phases",()=>
        {
            using var ring=new RollingReplayBuffer(10);
            var firstDoor=Door(0,100,0,.1f,0);var nextDoor=Door(10,100,.8f,.3f,10);nextDoor.opening=false;
            var firstWorld=World(0,23,.2f,0);var nextWorld=World(10,1,.8f,100);nextWorld.moonPhase=1;
            var firstParts=Parts(0,1,100,0,AircraftPartFlags.Present);var nextParts=Parts(10,1,200,10,AircraftPartFlags.Present|AircraftPartFlags.Detached);
            var firstWindow=Window(0,1,7,0,0);var nextWindow=Window(10,1,7,1,10);
            var expectedDoor=Door(0,100,0,0,0);HangarDoorSnapshot.SampleAtCut(firstDoor,nextDoor,5,expectedDoor);
            var expectedWorld=new WorldEnvironmentStateEvent();WorldStateSnapshot.SampleAtCut(firstWorld,nextWorld,5,expectedWorld);
            var expectedParts=AircraftPartMotionSnapshot.At(AircraftPartMotionSnapshot.From(firstParts),AircraftPartMotionSnapshot.From(nextParts),5);
            Add(ring,firstWorld,Spawn(100,0,"hangar","blue"),firstDoor,Spawn(1,0,"jet","red"),Layout(1,0,7),firstWindow,firstParts,Shot(2,1),Shot(8,1),nextDoor,nextWorld,nextWindow,nextParts,new LandingEvent{Time=15,unitId=1});
            var one=ring.Snapshot(15);var two=ring.Snapshot(15);
            Require(one.Count==two.Count&&one.Zip(two,(a,b)=>a.SequenceEqual(b)).All(v=>v),"repeat saves preserve immutable baseline");
            var saved=one.Select(RollingReplayBuffer.Decode).ToArray();
            try
            {
                Require(ring.CutTime==5&&saved.All(e=>e.Time>=0&&e.Time<=10),"exact clip window and rebased timestamps");
                var door=saved.OfType<HangarDoorStateEvent>().Single(e=>e.Time==0);
                Near(door.openAmount,.4f);Near(door.openAmount,expectedDoor.openAmount);Near(door.currentSpeed,.2f);
                Require(door.opening&&door.blendSeconds==0,"opening flag remains discrete and baseline is instant");
                var world=saved.OfType<WorldEnvironmentStateEvent>().Single(e=>e.Time==0);
                Near(world.timeOfDay,0);Near(world.windOffset.x,50);Near(world.cloudDisplacement.y,100);Near(world.conditions,.2f);
                Near(world.moonPhase.Value,0);Near(world.timeOfDay,expectedWorld.timeOfDay);
                var parts=saved.OfType<AircraftPartMotionEvent>().Single(e=>e.Time==0);
                Near(parts.parts[0].position.x,150);Near(parts.parts[0].position.x,expectedParts.parts[0].position.x);
                Require(parts.parts[1].flags==AircraftPartFlags.Present,"future detachment cannot happen before its timestamp");
                Near(saved.OfType<AircraftVisualStateEvent>().Single(e=>e.Time==0).poses[0].position.y,.5f);
                var shots=saved.OfType<GunBulletEvent>().ToArray();Require(shots.Length==1&&shots[0].Time==3&&shots[0].sound,"historical guns discarded and retained shot rebased");
                // Sample halfway through the remaining transition: the rebased
                // clip follows the same phase as the original at 7.5 seconds.
                var futureDoor=saved.OfType<HangarDoorStateEvent>().Single(e=>e.Time==5);
                Near(HangarDoorSnapshot.At(HangarDoorSnapshot.From(door),HangarDoorSnapshot.From(futureDoor),2.5).openAmount,.6f);
                var futureParts=saved.OfType<AircraftPartMotionEvent>().Single(e=>e.Time==5);
                Near(AircraftPartMotionSnapshot.At(AircraftPartMotionSnapshot.From(parts),AircraftPartMotionSnapshot.From(futureParts),2.5).parts[0].position.x,175);
                var futureWorld=saved.OfType<WorldEnvironmentStateEvent>().Single(e=>e.Time==5);
                Near(WorldStateSnapshot.At(WorldStateSnapshot.From(world),WorldStateSnapshot.From(futureWorld),2.5).timeOfDay,.5f);
            }
            finally{foreach(var e in saved)ReplayEventFactory.Return(e);Return(expectedDoor,expectedWorld);}
        });
        test("format 5 rolling anchors never interpolate across reused actors or visual layouts",()=>
        {
            using var ring=new RollingReplayBuffer(10);
            Add(ring,World(0,12,.2f,0),Spawn(1,0,"old-jet","blue"),Layout(1,0,1),Window(0,1,1,0,0),Parts(0,1,100,0,AircraftPartFlags.Present),
                Spawn(100,0,"old-hangar","blue"),Door(0,100,.1f,.1f,0),
                new DespawnEvent{Time=8,unitId=1},Spawn(1,8,"new-jet","red"),Layout(1,8,2),
                new DespawnEvent{Time=8,unitId=100},Spawn(100,8,"new-hangar","red"),
                Window(10,1,2,1,10),Parts(10,1,9000,10,AircraftPartFlags.Present),Door(10,100,.9f,.9f,10),new LandingEvent{Time=15,unitId=1});
            var saved=ring.Snapshot(15).Select(RollingReplayBuffer.Decode).ToArray();
            try
            {
                Near(saved.OfType<AircraftPartMotionEvent>().Single(e=>e.Time==0).parts[0].position.x,100);
                Near(saved.OfType<AircraftVisualStateEvent>().Single(e=>e.Time==0).poses[0].position.y,0);
                Near(saved.OfType<HangarDoorStateEvent>().Single(e=>e.Time==0).openAmount,.1f);
                Require(saved.OfType<SpawnEvent>().Count(e=>e.unitId==1)==2,"old and new actor lifetimes retained in order");
                Require(saved.OfType<WorldEnvironmentStateEvent>().Any(e=>e.Time==0),"global world baseline survives actor boundaries");
            }
            finally{foreach(var e in saved)ReplayEventFactory.Return(e);}
        });
        test("format 5 checkpoint ownership keeps world state but forgets old door and part lifetimes",()=>
        {
            using var state=new ReplaySeekState();
            foreach(var e in new IReplayEvent[]{World(0,12,.2f,0),Spawn(100,0,"hangar","blue"),Door(0,100,.2f,.1f,0),Spawn(1,0,"jet","red"),Parts(0,1,100,0,AircraftPartFlags.Present),Shot(1,1)})state.Add(e);
            using var checkpoint=state.Clone();
            foreach(var reducer in new[]{state,checkpoint})
            {
                reducer.Add(new DespawnEvent{Time=2,unitId=100});reducer.Add(Spawn(100,2,"replacement","red"));
                reducer.Add(new DespawnEvent{Time=2,unitId=1});reducer.Add(Spawn(1,2,"replacement-jet","blue"));
            }
            var a=new Queue<IReplayEvent>();var b=new Queue<IReplayEvent>();state.CopyTo(a);checkpoint.CopyTo(b);
            try
            {
                Require(a.First() is WorldEnvironmentStateEvent,"world phase restores before actor spawning");
                Require(a.OfType<WorldEnvironmentStateEvent>().Count()==1&&!a.OfType<HangarDoorStateEvent>().Any()&&!a.OfType<AircraftPartMotionEvent>().Any(),"only actor-owned old state is forgotten");
                Require(!a.OfType<GunBulletEvent>().Any(),"checkpoint never fires historical guns");
                Require(a.Select(e=>Convert.ToBase64String(RollingReplayBuffer.Encode(e))).SequenceEqual(b.Select(e=>Convert.ToBase64String(RollingReplayBuffer.Encode(e)))),"clone and baseline ownership remain independent and equivalent");
            }
            finally{Clear(a);Clear(b);}
        });
        test("format 5 lookahead releases global and actor samples after their own boundaries",()=>
        {
            var buffer=new BoundedEventBuffer(32);
            foreach(var e in new IReplayEvent[]{World(0,12,.2f,0),Spawn(100,0,"hangar","blue"),Door(0,100,0,.1f,0),Spawn(1,0,"jet","red"),Layout(1,0,1),Parts(0,1,100,0,AircraftPartFlags.Present),Window(0,1,1,0,0),Shot(1,1),Door(2,100,.5f,.1f,2),Parts(2,1,200,2,AircraftPartFlags.Present),World(2,13,.8f,20),Layout(1,3,2),Window(3,1,2,1,1)})buffer.Add(e,default);
            var delivered=new List<IReplayEvent>();
            try
            {
                while(buffer.TryTakeIfReady(0,out var e)){delivered.Add(e);if(!BoundedEventBuffer.IsInterpolation(e))buffer.MarkActionExecuted(e);}
                Require(delivered.OfType<WorldEnvironmentStateEvent>().Count()==2,"global weather lookahead needs no actor lifetime");
                Require(delivered.OfType<AircraftPartMotionEvent>().Any(e=>e.Time==2)&&delivered.OfType<HangarDoorStateEvent>().Any(e=>e.Time==2),"other actor lookahead remains available");
                Require(!delivered.OfType<GunBulletEvent>().Any()&&!delivered.OfType<AircraftVisualStateEvent>().Any(e=>e.layoutId==2),"guns stay at their event time and future layout stays held");
                Require(buffer.TryTakeIfReady(1,out var shot)&&shot is GunBulletEvent,"recorded gun is an action, not lookahead");delivered.Add(shot);
                Require(buffer.TryTakeIfReady(3,out var layout)&&layout is AircraftVisualLayoutEvent,"layout applied before its visual sample");buffer.MarkActionExecuted(layout);delivered.Add(layout);
                Require(buffer.TryTakeIfReady(3,out var visual)&&visual is AircraftVisualStateEvent v&&v.layoutId==2,"new layout sample released");delivered.Add(visual);
                Require(buffer.Count==0&&buffer.FreeSpace==32,"all format 5 buffer ownership released");
            }
            finally{foreach(var e in delivered)ReplayEventFactory.Return(e);buffer.ClearAndReturnToPool();}
        });
    }
    private static Quaternion Identity=>new Quaternion{w=1};
    private static SpawnEvent Spawn(uint unit,double time,string key,string faction)=>new SpawnEvent{Time=time,unitId=unit,ownerId=unit,jsonKey=key,factionName=faction,rotation=Identity};
    private static HangarDoorStateEvent Door(double time,uint unit,float amount,float speed,float blend)=>new HangarDoorStateEvent{Time=time,unitId=unit,hangarPath="0/1@0",openAmount=amount,currentSpeed=speed,blendSeconds=blend,opening=true,available=true};
    private static WorldEnvironmentStateEvent World(double time,float hour,float weather,float phase)=>new WorldEnvironmentStateEvent{Time=time,timeOfDay=hour,timeFactor=60,conditions=weather,cloudHeight=1000,windSpeed=5,windTurbulence=.2f,windHeading=350,windMainHeading=5,windRandomArc=20,windVelocity=new Vector3(4,0,3),windOffset=new Vector3(phase,phase+1,phase+2),moonPhase=27,hasCloudPhase=true,cloudDisplacement=new Vector2(phase,phase*2)};
    private static AircraftVisualLayoutEvent Layout(uint unit,double time,uint id)=>new AircraftVisualLayoutEvent{Time=time,unitId=unit,layoutId=id,channels=new[]{"canopy:0","airbrake:0"}};
    private static AircraftVisualStateEvent Window(double time,uint unit,uint layout,float amount,float blend)=>new AircraftVisualStateEvent{Time=time,unitId=unit,layoutId=layout,blendSeconds=blend,poses=new[]{new AircraftVisualPose{position=new Vector3(0,amount,0),rotation=Identity},new AircraftVisualPose{position=new Vector3(amount,0,0),rotation=Identity}}};
    private static AircraftPartMotionEvent Parts(double time,uint unit,float x,float blend,AircraftPartFlags flags)=>new AircraftPartMotionEvent{Time=time,unitId=unit,blendSeconds=blend,parts=new[]{new AircraftPartPose{partId=2,flags=AircraftPartFlags.Present,position=new GlobalPosition(x,1000,0),rotation=Identity},new AircraftPartPose{partId=9,flags=flags,position=new GlobalPosition(x+5,1002,0),rotation=Identity}}};
    private static GunBulletEvent Shot(double time,uint unit)=>new GunBulletEvent{Time=time,unitId=unit,station=2,weaponSlot=1,partId=4,partGunSlot=3,weaponName="MP cannon α",position=new GlobalPosition(1,1000,2),velocity=new Vector3(0,0,900),life=5,tracerSize=1.2f,reliability=.95f,color=new Color(1,.4f,.2f,1),tracer=true,active=true,explosive=true,sound=true};
    private static T RoundTrip<T>(T source)where T:class,IReplayEvent
    {try{return (T)RollingReplayBuffer.Decode(RollingReplayBuffer.Encode(source));}finally{ReplayEventFactory.Return(source);}}
    private static void Add(RollingReplayBuffer ring,params IReplayEvent[] events)
    {foreach(var e in events){try{ring.Add(e);}finally{ReplayEventFactory.Return(e);}}}
    private static void Return(params IReplayEvent[] events){foreach(var e in events)ReplayEventFactory.Return(e);}
    private static void Clear(Queue<IReplayEvent> queue){while(queue.Count>0)ReplayEventFactory.Return(queue.Dequeue());}
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Near(float actual,float expected){if(Math.Abs(actual-expected)>.0002f)throw new Exception($"{actual} != {expected}");}
}
