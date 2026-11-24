using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;
public static class RollingCutChecks
{
    public static int Run()
    {
        int passed=0;
        Test("rolling clip starts at the interpolated cut pose and continues the original curve",()=>
        {
            using var ring=new RollingReplayBuffer(10);
            var past=Move(1,2,10,2,0);var next=Move(1,12,100,12,90);
            ring.Add(Spawn(1,0));ring.Add(past);ring.Add(next);ring.Add(new LandingEvent {unitId=1,Time=16});
            var data=ring.Snapshot(16).Select(RollingReplayBuffer.Decode).ToArray();
            var spawn=data.OfType<SpawnEvent>().Single();var cut=data.OfType<UpdatePositionEvent>().Single(e=>e.Time==0);
            Near(spawn.pos.x,46);Near(cut.position.x,46);Near(cut.velocity.x,6);
            Near(cut.rotation.y,(float)Math.Sin(Math.PI/10));Near(cut.rotation.w,(float)Math.Cos(Math.PI/10));
            var final=data.OfType<UpdatePositionEvent>().Single(e=>e.Time==6);
            var fromClip=ReplayPoseInterpolation.At(PositionSnapshot.Create(cut),PositionSnapshot.Create(final),2);
            var fromOriginal=ReplayPoseInterpolation.At(PositionSnapshot.Create(past),PositionSnapshot.Create(next),8);
            Near(fromClip.position.x,fromOriginal.position.x);Near(fromClip.velocity.x,fromOriginal.velocity.x);
            Near(fromClip.rotation.y,fromOriginal.rotation.y);Near(fromClip.rotation.w,fromOriginal.rotation.w);
            Return(data);
        });
        Test("rolling clips interpolate from the original spawn when there is no past move",()=>
        {
            using var ring=new RollingReplayBuffer(10);ring.Add(Spawn(1,0));ring.Add(Move(1,12,120,12,0));
            var data=ring.Snapshot(16).Select(RollingReplayBuffer.Decode).ToArray();
            Near(data.OfType<SpawnEvent>().Single().pos.x,60);Near(data.OfType<SpawnEvent>().Single().startingVelocity.x,6);
            Check(data.OfType<UpdatePositionEvent>().Single().Time==6,"future sample remains retained");Return(data);
        });
        Test("rolling cut lookahead cannot cross an actor despawn or reused spawn",()=>
        {
            foreach(bool includeDespawn in new[]{true,false})
            {
                using var ring=new RollingReplayBuffer(10);ring.Add(Spawn(1,0));ring.Add(Move(1,2,10,0,0));
                if(includeDespawn)ring.Add(new DespawnEvent {unitId=1,Time=8});
                ring.Add(Spawn(1,9,900));ring.Add(Move(1,12,1000,0,0));
                var data=ring.Snapshot(16).Select(RollingReplayBuffer.Decode).ToArray();
                Near(data.OfType<SpawnEvent>().Single(e=>e.Time==0).pos.x,10);
                Near(data.OfType<SpawnEvent>().Single(e=>e.Time==3).pos.x,900);
                Check(data.OfType<UpdatePositionEvent>().Single(e=>e.Time==0).position.x==10,"new lifetime did not influence old anchor");Return(data);
            }
        });
        Test("cut snapshots remain repeatable and do not retime the owned recording baseline",()=>
        {
            using var ring=new RollingReplayBuffer(10);ring.Add(Spawn(1,0));ring.Add(Move(1,2,10,2,0));ring.Add(Move(1,12,100,12,0));
            var first=ring.Snapshot(16);var second=ring.Snapshot(16);
            Check(first.Count==second.Count && first.Zip(second,(a,b)=>a.SequenceEqual(b)).All(v=>v),"independent repeatable save");
            ring.Add(Move(1,14,120,14,0));var third=ring.Snapshot(16).Select(RollingReplayBuffer.Decode).ToArray();
            Near(third.OfType<SpawnEvent>().Single().pos.x,46);Near(third.OfType<UpdatePositionEvent>().Single(e=>e.Time==0).velocity.x,6);Return(third);
        });
        Test("cut rotation uses the same shortest-arc Slerp across angle wrap",()=>
        {
            var a=PositionSnapshot.Create(Move(1,0,0,0,350));var b=PositionSnapshot.Create(Move(1,10,10,0,10));
            var cut=ReplayPoseInterpolation.At(a,b,5);Near(cut.position.x,5);Near(Math.Abs(cut.rotation.w),1);Near(cut.rotation.y,0);
        });
        Test("rolling clip preserves exact gear geometry at a moving cut without changing retained bytes",()=>
        {
            using var ring=new RollingReplayBuffer(10);ring.Add(Spawn(1,0));
            ring.Add(new AircraftVisualLayoutEvent {unitId=1,layoutId=2,channels=new[]{"gear:0"},Time=0});
            ring.Add(new AircraftVisualStateEvent {unitId=1,layoutId=2,Time=1,poses=new[]{new AircraftVisualPose {rotation=Yaw(0)}}});
            ring.Add(new AircraftVisualStateEvent {unitId=1,layoutId=2,Time=12,blendSeconds=4,gearDeployed=true,poses=new[]{new AircraftVisualPose {position=new Vector3(4,0,0),rotation=Yaw(0)}}});
            var first=ring.Snapshot(19);var data=first.Select(RollingReplayBuffer.Decode).ToArray();
            var cut=data.OfType<AircraftVisualStateEvent>().Single(e=>e.Time==0);Near(cut.poses[0].position.x,1);
            Check(!cut.gearDeployed,"future deployment flags not applied early");
            var future=data.OfType<AircraftVisualStateEvent>().Single(e=>e.Time==3);
            Near(AircraftVisualSnapshot.At(AircraftVisualSnapshot.From(cut),AircraftVisualSnapshot.From(future),1).poses[0].position.x,2);
            Check(first.Zip(ring.Snapshot(19),(a,b)=>a.SequenceEqual(b)).All(v=>v),"visual baseline remains repeatable");Return(data);
        });
        return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
    }
    private static SpawnEvent Spawn(uint id,double time,float x=0)=>new SpawnEvent {unitId=id,ownerId=id,Time=time,jsonKey="jet",factionName="blue",pos=new GlobalPosition(x,0,0),rotation=Yaw(0)};
    private static UpdatePositionEvent Move(uint id,double time,float x,float velocity,float yaw)=>new UpdatePositionEvent {unitId=id,Time=time,position=new GlobalPosition(x,0,0),velocity=new Vector3(velocity,0,0),rotation=Yaw(yaw)};
    private static Quaternion Yaw(float degrees)=>new Quaternion {y=(float)Math.Sin(degrees*Math.PI/360),w=(float)Math.Cos(degrees*Math.PI/360)};
    private static void Return(IEnumerable<IReplayEvent> events){foreach(var e in events)ReplayEventFactory.Return(e);}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Near(float value,float expected){if(Math.Abs(value-expected)>.00011f)throw new Exception($"{value} != {expected}");}
}
