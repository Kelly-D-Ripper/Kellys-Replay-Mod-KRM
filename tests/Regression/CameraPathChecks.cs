using ReplayMod.Director;
using UnityEngine;

internal static class CameraPathChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("camera project JSON round-trips nested paths and legacy defaults",()=>
        {
            var p=new DirectorProject{replayHash="fixture",animatePathFov=true,cameraPathSmoothness=.8f};
            p.cameras.Add(new CameraKey{time=2,x=11,y=23,z=34,qz=.5f,qw=.8660254f,fov=37,relative=true,target=777,segmentMode=2});
            p.bookmarks.Add(new Bookmark{time=3,name="launch",actor=777});p.speeds.Add(new SpeedKey{time=4,scale=.2f});
            p.shots.Add(new Shot{name="one",start=1,end=5});p.studioLights.Add(new StudioLightSettings{name="rim",intensity=9});
            p.environment.enabled=true;p.environment.hour=19;
            p.firstPoint=new PoseRecord{time=0,x=5};
            p.path.Add(new SplineRecord{a=new PoseRecord{time=0},b=new PoseRecord{time=1},c=new PoseRecord{time=2},d=new PoseRecord{time=5}});
            if(!p.Validate(12))throw new Exception("fixture project invalid");
            string json=ProjectJsonCodec.Write(p);var restored=ProjectJsonCodec.Read(json);
            if(!restored.Validate(12)||restored.cameras.Count!=1||restored.cameras[0].target!=777||!restored.cameras[0].relative||restored.cameras[0].fov!=37||restored.cameras[0].segmentMode!=2||restored.bookmarks.Count!=1||restored.speeds.Count!=1||restored.shots.Count!=1||restored.studioLights.Count!=1||restored.studioLights[0].name!="rim"||restored.environment.hour!=19||!restored.environment.enabled||restored.path.Count!=1||restored.firstPoint.x!=5)throw new Exception("nested project data failed to round-trip");
            var oversized=new DirectorProject();for(int i=0;i<6000;i++)oversized.bookmarks.Add(new Bookmark{time=0,name=new string('測',120)});
            if(!oversized.Validate(12))throw new Exception("oversized valid project fixture invalid");
            try{ProjectJsonCodec.Write(oversized);throw new Exception("oversized project was accepted");}catch(System.IO.InvalidDataException){}
            var legacy=ProjectJsonCodec.Read("{\"version\":1,\"replayHash\":\"old\",\"fov\":35}");
            if(!legacy.Validate(12)||legacy.fov!=35||legacy.cameras.Count!=0||legacy.environment==null||legacy.studioLights==null)throw new Exception("legacy project lost defaults");
            var untrusted=ProjectJsonCodec.Read("{\"$type\":\"System.IO.FileInfo, mscorlib\",\"replayHash\":\"old\"}");
            if(untrusted.replayHash!="old"||!untrusted.Validate(12))throw new Exception("untrusted type metadata affected project");
        });
        test("native pause menu resumes only the same replay if it was playing",()=>
        {
            var state=new ReplayMod.Core.ReplayPauseMenuState<object>();var a=new object();var b=new object();
            state.Open(a,true);if(!state.Close(a,true)||state.Close(a,true))throw new Exception("playing replay did not resume once");
            state.Open(a,false);if(state.Close(a,true))throw new Exception("previously paused replay resumed");
            state.Open(a,true);if(state.Close(b,true))throw new Exception("replacement replay resumed");
            state.Open(a,true);if(state.Close(a,false))throw new Exception("inactive replay resumed");
            state.Open(a,true);state.Clear();if(state.Close(a,true))throw new Exception("quit left a resume pending");
        });
        test("per-segment constant speed overrides curve and easing without changing saved times",()=>
        {
            var keys=new[]{Key(0,0,0),Key(2,10,0),Key(5,10,10)};
            keys[0].segmentMode=1;keys[0].easing=1;
            Near(Sample(keys,.5,true).position.x,2.5);
            Near(Sample(keys,1.5,true).position.x,7.5);
            Near(keys[0].time,0,0);Near(keys[1].time,2,0);
        });
        test("per-segment jump holds the whole camera pose until the next key",()=>
        {
            var keys=new[]{Key(1,0,0),Key(3,20,0)};keys[0].segmentMode=3;keys[0].fov=30;keys[1].fov=90;
            keys[0].qz=(float)Math.Sin(Math.PI/8);keys[0].qw=(float)Math.Cos(Math.PI/8);
            var held=Sample(keys,2.999);Near(held.position.x,0);Near(held.fov,30);Near(held.rotation.z,keys[0].qz);
            var endpoint=Sample(keys,3);Near(endpoint.position.x,20);Near(endpoint.fov,90);
        });
        test("segment modes persist and older projects default to their existing path behavior",()=>
        {
            var options=new System.Text.Json.JsonSerializerOptions {IncludeFields=true};
            var project=new DirectorProject();project.cameras.Add(Key(0,0,0));project.cameras.Add(Key(2,10,0));project.cameras[0].segmentMode=2;
            var copy=System.Text.Json.JsonSerializer.Deserialize<DirectorProject>(System.Text.Json.JsonSerializer.Serialize(project,options),options);
            if(!copy.Validate(10)||copy.cameras[0].segmentMode!=2)throw new Exception("segment mode not preserved");
            var legacy=System.Text.Json.JsonSerializer.Deserialize<CameraKey>("{\"time\":1,\"qw\":1,\"fov\":60}",options);
            if(legacy.segmentMode!=0)throw new Exception("legacy path default changed");
            copy.cameras[0].segmentMode=4;if(copy.Validate(10))throw new Exception("invalid segment mode accepted");
        });
        test("path capture preserves 6 and 9 seconds and the three-second camera move",()=>
        {
            var keys=new List<CameraKey>();
            if(!DirectorProject.TryStoreCameraPoint(keys,Key(6,0,0),20,out _,out _)||!DirectorProject.TryStoreCameraPoint(keys,Key(9,30,0),20,out int index,out bool replaced)||index!=1||replaced)throw new Exception("capture failed");
            Near(keys[0].time,6,0);Near(keys[1].time,9,0);
            foreach(bool smooth in new[]{false,true})
            {Near(Sample(keys.ToArray(),7.5,smooth).position.x,15);Near(Sample(keys.ToArray(),9,smooth).position.x,30);}
            var close=Key(9.01,31,0);if(!DirectorProject.TryStoreCameraPoint(keys,close,20,out _,out _))throw new Exception("short segment rejected");Near(keys[2].time,9.01,0);
        });
        test("path recapture replaces the exact paused time and rewind inserts in time order",()=>
        {
            var keys=new List<CameraKey>{Key(6,0,0),Key(9,30,0)};var replacement=Key(9,60,0);replacement.fov=35;replacement.relative=true;replacement.target=42;
            if(!DirectorProject.TryStoreCameraPoint(keys,replacement,20,out int index,out bool replaced)||index!=1||!replaced||keys.Count!=2||!ReferenceEquals(keys[1],replacement))throw new Exception("paused capture duplicated time");
            if(!DirectorProject.TryStoreCameraPoint(keys,Key(7.5,15,0),20,out index,out replaced)||index!=1||replaced)throw new Exception("middle insert failed");
            if(!DirectorProject.TryStoreCameraPoint(keys,Key(3,5,0),20,out index,out replaced)||index!=0||replaced)throw new Exception("earlier insert failed");
            Near(keys[0].time,3,0);Near(keys[1].time,6,0);Near(keys[2].time,7.5,0);Near(keys[3].time,9,0);Near(keys[3].fov,35);if(keys[3].target!=42||!keys[3].relative)throw new Exception("key metadata changed");
        });
        test("camera point limits permit recapture and reject bad times without mutation",()=>
        {
            var keys=Enumerable.Range(0,2000).Select(i=>Key(i,0,0)).ToList();var original=keys.ToArray();
            foreach(double time in new[]{-1,double.NaN,double.PositiveInfinity,2001})
            {if(DirectorProject.TryStoreCameraPoint(keys,Key(time,0,0),2000,out _,out _)||!keys.SequenceEqual(original))throw new Exception("invalid capture mutated path");}
            if(DirectorProject.TryStoreCameraPoint(keys,Key(1999.5,0,0),2000,out _,out _)||!keys.SequenceEqual(original))throw new Exception("point cap escaped");
            if(!DirectorProject.TryStoreCameraPoint(keys,Key(1000,12,0),2000,out int index,out bool replaced)||!replaced||index!=1000||keys.Count!=2000)throw new Exception("point cap prevents recapture");
            var bad=new List<CameraKey>{Key(9,0,0),Key(6,0,0)};if(DirectorProject.TryStoreCameraPoint(bad,Key(7,0,0),20,out _,out _)||bad[0].time!=9)throw new Exception("unordered path silently retimed");
        });
        test("capture times survive project serialization and replay-speed-independent evaluation",()=>
        {
            var project=new DirectorProject();DirectorProject.TryStoreCameraPoint(project.cameras,Key(6,0,0),20,out _,out _);DirectorProject.TryStoreCameraPoint(project.cameras,Key(9,30,0),20,out _,out _);
            var options=new System.Text.Json.JsonSerializerOptions {IncludeFields=true};
            var copy=System.Text.Json.JsonSerializer.Deserialize<DirectorProject>(System.Text.Json.JsonSerializer.Serialize(project,options),options);
            if(!copy.Validate(20))throw new Exception("saved path invalid");Near(copy.cameras[0].time,6,0);Near(copy.cameras[1].time,9,0);
            foreach(double speed in new[]{.1,.25,.5,1})
            {double realElapsed=1.5/speed;double replayTime=6+realElapsed*speed;Near(Sample(copy.cameras.ToArray(),replayTime).position.x,15);}
        });
        test("take preparation countdown and final hold reject early playback but running and cancellation release it",()=>
        {
            var take=new CameraTakeClock();long revision=take.Prepare();
            if(!take.HoldsPlayback)throw new Exception("preparation lets time run");take.Ready(revision,10);
            if(!take.HoldsPlayback)throw new Exception("countdown lets time run");take.Tick(15,false,true,2);
            if(take.HoldsPlayback)throw new Exception("scheduled start remains blocked");take.Tick(20,true,true,2);
            if(!take.HoldsPlayback)throw new Exception("end hold can resume early");take.Cancel();
            if(take.HoldsPlayback)throw new Exception("cancelled take retains playback gate");
        });
        test("camera take clock limit commits the exact end before a long frame can execute later actions",()=>
        {
            Near(ReplayMod.Core.PlaybackControlState.LimitTime(10.4,100,10),10);
            Near(ReplayMod.Core.PlaybackControlState.LimitTime(9.8,100,10),9.8);
            Near(ReplayMod.Core.PlaybackControlState.LimitTime(120,100,null),100);
            Near(ReplayMod.Core.PlaybackControlState.LimitTime(20,100,double.NaN),20);
        });
        test("smooth path turns continuously through unequal-duration camera points",()=>
        {
            var keys=new[]{Key(0,0,0),Key(2,10,0),Key(7,10,20),Key(10,30,20)};
            var middle=Sample(keys,2);Near(middle.position.x,10);Near(middle.position.z,0);
            const double h=.0005;var left=Sample(keys,2-h);var right=Sample(keys,2+h);
            Near((middle.position.x-left.position.x)/h,(right.position.x-middle.position.x)/h,.02);
            Near((middle.position.z-left.position.z)/h,(right.position.z-middle.position.z)/h,.02);
            var turn=Sample(keys,1);if(Math.Abs(turn.position.z)<.01)throw new Exception("smooth path remained on straight corner segments");
            var linear=Sample(keys,1,false);Near(linear.position.x,5);Near(linear.position.z,0);
        });
        test("camera path holds endpoints without requiring an absent future subject",()=>
        {
            var keys=new[]{Key(3,1,2),Key(8,10,20)};
            bool ResolveFirst(CameraKey k,out Vector3 p,out Quaternion q){p=new Vector3(k.x,0,k.z);q=new Quaternion {w=1};return ReferenceEquals(k,keys[0]);}
            if(!CameraPathSampler.TrySample(keys,0,true,ResolveFirst,out var first))throw new Exception("unavailable future subject prevented opening frame");
            Near(first.position.x,1);Near(Sample(keys,100).position.z,20);
            if(CameraPathSampler.TrySample(keys,double.NaN,true,ResolveFirst,out _))throw new Exception("nonfinite clock accepted");
        });
        test("attached path resolves all controls in one moving reference frame",()=>
        {
            var keys=new[]{Key(0,0,0),Key(5,10,0),Key(10,10,10)};foreach(var k in keys){k.relative=true;k.target=42;}
            double origin=100;
            bool ResolveAttached(CameraKey k,out Vector3 p,out Quaternion q){p=new Vector3((float)(origin+k.x),0,k.z);q=new Quaternion {w=1};return true;}
            CameraPathSampler.TrySample(keys,4,true,ResolveAttached,out var before);origin+=30;
            CameraPathSampler.TrySample(keys,4,true,ResolveAttached,out var after);
            Near(after.position.x-before.position.x,30);Near(after.position.z,before.position.z);
            Near(keys[1].x,10);Near(keys[1].time,5);
        });
        test("camera path lens and shortest-arc orientation interpolate without mutating saved points",()=>
        {
            var keys=new[]{Key(0,0,0),Key(4,4,0)};keys[0].fov=30;keys[1].fov=90;
            keys[0].qy=(float)Math.Sin(350*Math.PI/360);keys[0].qw=(float)Math.Cos(350*Math.PI/360);
            keys[1].qy=(float)Math.Sin(10*Math.PI/360);keys[1].qw=(float)Math.Cos(10*Math.PI/360);
            var middle=Sample(keys,2);Near(middle.fov,60);Near(Math.Abs(middle.rotation.w),1);Near(middle.rotation.y,0);
            Near(keys[0].fov,30);Near(keys[1].fov,90);
        });
        test("camera take countdown starts once after preparation and cancellation prevents late starts",()=>
        {
            var take=new CameraTakeClock();long first=take.Prepare();
            if(take.Tick(100,false,true,0)!=CameraTakeAction.None)throw new Exception("preparation time consumed countdown");
            if(!take.Ready(first,100)||take.Tick(104.999,false,true,0)!=CameraTakeAction.None)throw new Exception("countdown duration changed");
            if(take.Tick(105,false,true,0)!=CameraTakeAction.StartPlayback||take.Tick(106,false,true,0)!=CameraTakeAction.None)throw new Exception("take start duplicated or missing");
            take.Cancel();if(take.Tick(200,false,true,0)!=CameraTakeAction.None||take.Ready(first,200))throw new Exception("cancelled asynchronous preparation restarted take");
            long next=take.Prepare();take.Ready(next,300);take.Cancel();if(take.Tick(400,false,true,0)!=CameraTakeAction.None)throw new Exception("cancelled countdown resumed playback");
        });
        test("camera take final hold uses real seconds and manual ending remains available",()=>
        {
            var take=new CameraTakeClock();long revision=take.Prepare();take.Ready(revision,10);take.Tick(15,false,true,2);
            if(take.Tick(20,true,true,2)!=CameraTakeAction.HoldEnd||take.Tick(21.99,true,true,2)!=CameraTakeAction.None||take.Tick(22,true,true,2)!=CameraTakeAction.Finish||take.Active)throw new Exception("end hold timing or cleanup failed");
            revision=take.Prepare();take.Ready(revision,30);take.Tick(35,false,false,0);
            if(take.Tick(1000,true,false,0)!=CameraTakeAction.None||!take.Active)throw new Exception("manual take ended automatically");take.Cancel();
        });
        test("matched rail uses displayed replay progress and stops without creeping at zero speed",()=>
        {
            Near(RailSpeedMatch.EffectiveSpeed(300,.1,1),30);Near(RailSpeedMatch.EffectiveSpeed(300,0,1),0);
            Near(RailSpeedMatch.EffectiveSpeed(300,-1,1),0);Near(RailSpeedMatch.EffectiveSpeed(double.NaN,1,1),0);
            var rail=new CameraRailState();rail.Start(0,0,0,1,0,0);rail.AdvanceAtSpeed(1,0);Near(rail.X,0);
            rail.AdvanceAtSpeed(1,RailSpeedMatch.EffectiveSpeed(300,.1,1));Near(rail.X,30);
            rail.AdvanceAtSpeed(.5,300);Near(rail.X,180);Near(rail.Speed,5);
        });
    }
    private static CameraKey Key(double time,float x,float z)=>new CameraKey {time=time,x=x,z=z,qw=1,fov=60,easing=0};
    private static CameraPathSample Sample(CameraKey[] keys,double time,bool smooth=true)
    {
        bool Resolve(CameraKey k,out Vector3 p,out Quaternion q){p=new Vector3(k.x,k.y,k.z);q=new Quaternion {x=k.qx,y=k.qy,z=k.qz,w=k.qw};return true;}
        if(!CameraPathSampler.TrySample(keys,time,smooth,Resolve,out var sample))throw new Exception("path sample failed");return sample;
    }
    private static void Near(double actual,double expected,double tolerance=.0001){if(Math.Abs(actual-expected)>tolerance)throw new Exception($"{actual} != {expected}");}
}
