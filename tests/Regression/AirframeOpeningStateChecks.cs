using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class AirframeOpeningStateChecks
{
    public static int Run()
    {
        int passed=0;
        Test("paused opening frame has existing wreck geometry without advancing the clock",()=>
        {
            var timeline=new AircraftPartMotionTimeline();
            timeline.Add(Motion(0,0));
            Require(timeline.Locate(0,out var opening,out var next,out var fraction),"zero-time opening sample absent");
            Require(opening.time==0&&ReferenceEquals(opening,next)&&fraction==0,"opening required a later frame");
            Require(opening.parts[0].flags==AircraftPartFlags.Present&&opening.parts[1].flags==(AircraftPartFlags.Present|AircraftPartFlags.Detached|AircraftPartFlags.Fragmented)&&
                opening.parts[2].flags==AircraftPartFlags.None,"existing part appearance/breakup lost");
        });
        Test("binary and checkpoint restoration retain an independent zero-time airframe",()=>
        {
            using var original=new ReplaySeekState();
            original.Add(new SpawnEvent {Time=0,unitId=17,ownerId=17,jsonKey="opening-aircraft",factionName="test",isAircraft=true});
            var encoded=RollingReplayBuffer.Encode(Motion(0,40));original.Add(RollingReplayBuffer.Decode(encoded));
            using var checkpoint=original.Clone();original.Clear();
            var events=new Queue<IReplayEvent>();checkpoint.CopyTo(events);
            try
            {
                var restored=events.OfType<AircraftPartMotionEvent>().Single();
                var timeline=new AircraftPartMotionTimeline();timeline.Add(restored);
                Require(timeline.Locate(0,out var opening,out _,out _)&&opening.parts[0].position.x==40&&
                    (opening.parts[1].flags&AircraftPartFlags.Fragmented)!=0,"opening wreck lost after original-state disposal");
                Require(!events.OfType<DetachPartEvent>().Any()&&!events.OfType<EjectionEvent>().Any(),"opening state invented an earlier action");
            }
            finally {while(events.Count>0)ReplayEventFactory.Return(events.Dequeue());}
        });
        Test("future breakup cannot replace the exact paused opening state",()=>
        {
            var opening=Motion(0,0);opening.parts[1].flags=AircraftPartFlags.Present;
            var later=Motion(.1,100);later.blendSeconds=.1f;
            var timeline=new AircraftPartMotionTimeline();timeline.Add(opening);timeline.Add(later);
            Require(timeline.Locate(0,out var previous,out var next,out var fraction)&&fraction==0&&previous.parts[1].flags==AircraftPartFlags.Present,
                "future breakup leaked into paused time zero");
            Require(AircraftPartMotionSnapshot.At(previous,next,0).parts[0].position.x==0,"opening pose advanced into lookahead");
            Require(timeline.Locate(.1,out previous,out _,out _)&&(previous.parts[1].flags&AircraftPartFlags.Fragmented)!=0,"recorded later breakup disappeared");
        });
        return passed;
        void Test(string title,Action action){action();passed++;Console.WriteLine("PASS airframe opening "+title);}
    }
    private static AircraftPartMotionEvent Motion(double time,float x)=>new AircraftPartMotionEvent
    {
        Time=time,unitId=17,parts=new[]
        {
            Pose(0,AircraftPartFlags.Present,x),
            Pose(1,AircraftPartFlags.Present|AircraftPartFlags.Detached|AircraftPartFlags.Fragmented,x+12),
            Pose(2,AircraftPartFlags.None,x-8)
        }
    };
    private static AircraftPartPose Pose(byte id,AircraftPartFlags flags,float x)=>new AircraftPartPose
    {partId=id,flags=flags,position=new GlobalPosition(x,250,75),rotation=new Quaternion {w=1}};
    private static void Require(bool value,string reason){if(!value)throw new Exception(reason);}
}
