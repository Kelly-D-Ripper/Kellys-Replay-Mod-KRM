using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ReplayMod.Data;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class VisualProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    public static async UniTask Run(Action<string> log)
    {
        string root=AppDomain.CurrentDomain.BaseDirectory;
        string name=Environment.GetEnvironmentVariable("REPLAY_CARGO_VISUAL_FILE")??"Terrain1_2026-11-16_23-28-32_138f6feb.replay";
        uint carrierId=0,vehicleId=0;double before=-1,after=-1,open=-1;var rampSamples=new AircraftPartMotionTimeline();
        using(var input=new BinaryReader(File.OpenRead(Path.Combine(root,"Replays",name))))
        {
            var header=ReplayRecordingHeader.Read(input);
            for(long i=0;i<header.EventCount;i++)
            {
                var e=ReplayRecordTransport.ReadEvent(input,header.Format);
                if(e is CargoMountStateEvent cargo){if(cargo.mounts.Any(m=>m.active&&!m.fired)){carrierId=cargo.unitId;before=cargo.Time;}if(after<0&&cargo.mounts.Any(m=>!m.active))after=cargo.Time;}
                if(e is SpawnEvent spawn&&spawn.jsonKey=="6x6_1_APC")vehicleId=spawn.unitId;
                if(e is DetachPartEvent detach)log("VISUAL recorded detach unit="+detach.unitID+" part="+detach.partID+" time="+detach.Time.ToString("F2"));
                if(e is AircraftPartMotionEvent motion&&motion.unitId==carrierId&&motion.parts.Any(p=>p.partId==24))rampSamples.Add(motion);
                if(e is CargoParachuteStateEvent chute&&chute.present&&chute.parachutes.Any(p=>p.canopyActive)&&open<0)open=chute.Time;
                ReplayEventFactory.Return(e);
            }
        }
        if(carrierId==0||vehicleId==0||before<0||after<0||open<0)throw new Exception("Cargo visual fixture incomplete");
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(name);
        var player=ReplayManager.i.Playback;player.Pause();
        string prefix=Environment.GetEnvironmentVariable("REPLAY_VISUAL_PREFIX")??"cargo-v14";
        if(prefix.IndexOfAny(Path.GetInvalidFileNameChars())>=0||prefix.Contains("/")||prefix.Contains("\\"))throw new Exception("Invalid visual evidence prefix");
        await Capture(player,before,carrierId,0,prefix+"-before.png",log);
        await Capture(player,Math.Max(after,open)+3,carrierId,vehicleId,prefix+"-after.png",log);
        var controller=(UnitController)typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
        var find=typeof(UnitController).GetMethod("FindRecordedAircraftPart",BindingFlags.NonPublic|BindingFlags.Instance);
        foreach(double at in new[]{9.85,10.1,10.6,11.1,11.34})
        {
            await player.TimelineJump(at,0,false);await Wait(.1f);
            var carrier=player.FindActor(carrierId);var part=(UnitPart)find.Invoke(controller,new object[]{carrier,(byte)24});
            if(part==null||!rampSamples.Locate(at,out var first,out var next,out float blend))throw new Exception("Ramp or recorded pose missing at "+at);
            var a=first.parts.First(p=>p.partId==24);var b=next.parts.First(p=>p.partId==24);
            if((a.flags&(AircraftPartFlags.Present|AircraftPartFlags.Detached))!=(AircraftPartFlags.Present|AircraftPartFlags.Detached))throw new Exception("No recorded detached ramp pose at "+at);
            var expected=Vector3.Lerp(a.position.ToLocalPosition(),b.position.ToLocalPosition(),blend);var rotation=Quaternion.Slerp(a.rotation,b.rotation,blend);
            float distance=Vector3.Distance(part.transform.position,expected),angle=Quaternion.Angle(part.transform.rotation,rotation);
            log("RAMP COMPARE time="+at.ToString("F2")+" recorded="+first.time.ToString("F3")+"/"+next.time.ToString("F3")+" distance="+distance.ToString("F3")+"m angle="+angle.ToString("F2")+"deg");
            if(distance>.1f||angle>1f)throw new Exception("Ramp replay differs from recorded live pose at "+at);
        }
        log("RAMP COMPARE PASS five replay poses matched recorded native ramp trajectory");
        await player.TimelineJump(9.85,0,false);ReplayDirector.i.SetPlaybackSpeed(1);player.Resume();
        float worstDistance=0,worstAngle=0;double worstTime=0;int frames=0;float timeout=Time.realtimeSinceStartup+15;
        while(player.currentVirtualTime<11.34)
        {
            if(Time.realtimeSinceStartup>timeout)throw new Exception("Ramp playback timed out");
            await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);double time=player.currentVirtualTime;
            var actor=player.FindActor(carrierId);var part=actor==null?null:(UnitPart)find.Invoke(controller,new object[]{actor,(byte)24});
            if(part==null||!rampSamples.Locate(time,out var aSnap,out var bSnap,out float blend))continue;
            var a=aSnap.parts.First(p=>p.partId==24);var b=bSnap.parts.First(p=>p.partId==24);
            if((a.flags&(AircraftPartFlags.Present|AircraftPartFlags.Detached))!=(AircraftPartFlags.Present|AircraftPartFlags.Detached))continue;
            var expected=Vector3.Lerp(a.position.ToLocalPosition(),b.position.ToLocalPosition(),blend);
            float dist=Vector3.Distance(part.transform.position,expected);if(dist>worstDistance){worstDistance=dist;worstTime=time;}
            worstAngle=Math.Max(worstAngle,Quaternion.Angle(part.transform.rotation,Quaternion.Slerp(a.rotation,b.rotation,blend)));frames++;
        }
        player.Pause();log("RAMP CONTINUOUS frames="+frames+" worst="+worstDistance.ToString("F3")+"m at="+worstTime.ToString("F3")+" angle="+worstAngle.ToString("F2")+"deg");
        if(frames<10||worstDistance>.1f||worstAngle>1f)throw new Exception("Ramp trajectory diverged during continuous playback");
        log("RAMP CONTINUOUS PASS live-recorded ramp trajectory matched during playback");


    }
    static async UniTask Capture(Player player,double time,uint carrierId,uint vehicleId,string filename,Action<string> log)
    {
        await player.TimelineJump(time,0,false);await Wait(1);
        var carrier=player.FindActor(carrierId);if(carrier==null)throw new Exception("Visual carrier missing");
        var vehicle=vehicleId==0?null:player.FindActor(vehicleId);if(vehicleId!=0&&vehicle==null)throw new Exception("Visual deployed vehicle missing");
        Vector3 target=vehicle==null?carrier.transform.position:vehicle.transform.position+new Vector3(0,12,0);
        Vector3 offset=vehicle==null?new Vector3(45,20,-65):new Vector3(40,25,-65);
        ReplayDirector.i.EnterFreeCamera();ReplayDirector.i.SetClean(true);
        var manager=SceneSingleton<CameraStateManager>.i;
        manager.SetCameraPosition((target+offset).ToGlobalPosition(),Quaternion.LookRotation(-offset,Vector3.up));
        await Wait(1);
        if(vehicle!=null)
        {
            var cam=manager.mainCamera;int found=0;
            foreach(var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if(renderer==null||!renderer.enabled||!renderer.gameObject.activeInHierarchy||Vector3.Distance(renderer.bounds.center,cam.transform.position)>1000)continue;
                var view=cam.WorldToViewportPoint(renderer.bounds.center);
                if(view.z<=0||view.x<.52f||view.x>.64f||view.y<.14f||view.y>.3f)continue;
                var part=renderer.GetComponentInParent<UnitPart>();var unit=renderer.GetComponentInParent<Unit>();
                log("VISUAL candidate name="+renderer.name+" part="+(part==null?"none":part.name+"#"+part.id)+" unit="+(unit==null?"none":unit.name)+" viewport="+view.ToString("F3")+" world="+renderer.bounds.center.ToString("F1"));
                if(++found>=50)break;
            }
            log("VISUAL candidate count="+found);
        }
        string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ghost-probe",filename);
        if(File.Exists(file))throw new Exception("Visual evidence destination already exists");
        ScreenCapture.CaptureScreenshot(file);await Wait(2);
        if(!File.Exists(file)||new FileInfo(file).Length<1000)throw new Exception("Visual screenshot missing");
        log("VISUAL captured "+filename+" at "+time.ToString("F2"));
    }
}
