using System.Text.Json;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class EjectionEyeChecks
{
    public static int Run()
    {
        int passed=0;
        Test("eye codec retains aircraft lifetime pose and seat",()=>
        {
            var source=Eye(2,20);var value=(PilotEjectionStateEvent)RollingReplayBuffer.Decode(RollingReplayBuffer.Encode(source));
            Require(value.aircraftId==1&&value.unitId==2&&value.aircraftBirth==0&&value.seat==0&&value.eye.x==20&&value.rotation.w==1,"recorded eye identity");ReplayEventFactory.Return(value);
            foreach(Action<PilotEjectionStateEvent> corrupt in new Action<PilotEjectionStateEvent>[] {e=>e.eye.x=float.NaN,e=>e.aircraftBirth=3,e=>e.seat=16,e=>e.rotation.w=0,e=>e.unitId=1,e=>e.capsule=true})
            {var invalid=Eye(2,20);corrupt(invalid);Require(!invalid.HasValidPayload(),"invalid payload accepted");}
        });
        Test("eye timeline interpolates and separates parent lifetimes",()=>
        {
            var timeline=new PilotEyeTimeline();timeline.Add(Eye(1,10));timeline.Add(Eye(3,30));
            Require(!timeline.TryAt(.5,out _)&&timeline.TryAt(2,out var pose)&&Math.Abs(pose.position.x-20)<.0001,"eye interpolation");
            var replacement=Eye(4,100);replacement.aircraftBirth=4;timeline.Add(replacement);
            Require(timeline.AircraftBirth==4&&!timeline.TryAt(3,out _)&&timeline.TryAt(4,out pose)&&pose.position.x==100,"old parent eye retained");
        });
        Test("seek eye state is independent and removed on pilot despawn",()=>
        {
            using var state=new ReplaySeekState();state.Add(Spawn(1));state.Add(Spawn(2));state.Add(Eye(2,20));
            var queue=new Queue<IReplayEvent>();state.CopyTo(queue);Require(queue.OfType<PilotEjectionStateEvent>().Single().aircraftId==1,"eye checkpoint missing");foreach(var e in queue)ReplayEventFactory.Return(e);queue.Clear();
            state.Add(new DespawnEvent {Time=3,unitId=2});state.CopyTo(queue);Require(!queue.OfType<PilotEjectionStateEvent>().Any(),"despawn eye leaked");foreach(var e in queue)ReplayEventFactory.Return(e);
        });
        Test("rolling cut interpolates eyes and rebases aircraft birth",()=>
        {
            using var rolling=new RollingReplayBuffer(10);rolling.Add(Spawn(1));rolling.Add(Spawn(2));rolling.Add(Eye(1,10));rolling.Add(Eye(3,30));rolling.Add(new LandingEvent {Time=12,unitId=1});
            var events=rolling.Snapshot(12).Select(RollingReplayBuffer.Decode).ToArray();var first=events.OfType<PilotEjectionStateEvent>().OrderBy(e=>e.Time).First();
            Require(first.Time==0&&first.aircraftBirth==0&&Math.Abs(first.eye.x-20)<.0001,"cut eye did not retain exact opening pose");foreach(var e in events)ReplayEventFactory.Return(e);
        });
        Test("eye interpolation is fenced behind its pilot spawn",()=>
        {
            var buffer=new BoundedEventBuffer(8);buffer.Add(new SpawnEvent {Time=2,unitId=2,jsonKey="pilot",factionName="blue"},default);buffer.Add(Eye(2,20),default);
            Require(!buffer.TryTakeIfReady(1,out _),"future eye crossed spawn");Require(buffer.TryTakeIfReady(2,out var spawn)&&spawn is SpawnEvent,"spawn not first");ReplayEventFactory.Return(spawn);
            Require(buffer.TryTakeIfReady(2,out var eye)&&eye is PilotEjectionStateEvent,"eye not released after spawn");ReplayEventFactory.Return(eye);Require(buffer.FreeSpace==8,"buffer slot leak");
        });
        Test("rolling orphan eyes cannot attach to replacement aircraft at time zero",()=>
        {
            using var rolling=new RollingReplayBuffer(10);rolling.Add(Spawn(1));rolling.Add(Spawn(2));rolling.Add(Eye(2,20));
            rolling.Add(new DespawnEvent {Time=3,unitId=1});var replacement=Spawn(1);replacement.Time=4;rolling.Add(replacement);
            rolling.Add(Eye(6,60));rolling.Add(new LandingEvent {Time=15,unitId=1});
            var events=rolling.Snapshot(15).Select(RollingReplayBuffer.Decode).ToArray();
            var fresh=events.OfType<SpawnEvent>().Single(e=>e.unitId==1);Require(fresh.Time==0,"replacement must be present at clip opening");
            foreach(var eye in events.OfType<PilotEjectionStateEvent>())
            {
                Require(eye.orphanedAircraft&&eye.aircraftBirth==0,"orphan distinction lost in codec/cut");
                var timeline=new PilotEyeTimeline();timeline.Add(eye);Require(timeline.SourceBirth<0&&timeline.SourceBirth!=fresh.Time,"old pilot matched new aircraft lifetime");
            }
            Require(events.OfType<PilotEjectionStateEvent>().Count()==2,"baseline and retained orphan eyes required");
            foreach(var e in events)ReplayEventFactory.Return(e);
        });
        Test("first-person ejection is opt-in and saves with old-compatible project",()=>
        {
            var options=new JsonSerializerOptions {IncludeFields=true};var old=JsonSerializer.Deserialize<DirectorProject>("{\"version\":1}",options);Require(!old.firstPersonEjection&&old.Validate(20),"old project changed scope");
            old.firstPersonEjection=true;var copy=JsonSerializer.Deserialize<DirectorProject>(JsonSerializer.Serialize(old,options),options);Require(copy.firstPersonEjection&&copy.Validate(20),"camera option did not persist");
        });
        return passed;
        void Test(string title,Action action){action();passed++;Console.WriteLine("PASS ejection eye "+title);}
    }
    private static SpawnEvent Spawn(uint id)=>new SpawnEvent {Time=0,unitId=id,ownerId=id,jsonKey="actor",factionName="blue"};
    private static PilotEjectionStateEvent Eye(double time,float x)=>new PilotEjectionStateEvent {Time=time,unitId=2,aircraftId=1,aircraftBirth=0,eye=new GlobalPosition(x,2,3),rotation=new Quaternion {w=1}};
    private static void Require(bool value,string reason){if(!value)throw new Exception(reason);}
}
