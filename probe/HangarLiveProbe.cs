using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class HangarLiveProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    static async UniTask Until(Func<bool> condition,float seconds,string message){float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new Exception(message);await UniTask.NextFrame();}}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Native hangar recording test");
        await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Mission unavailable");TimeScaleManager.Scale=1;
        await ReplayManager.i.SwitchState(ModStates.Record);var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("Recorder unavailable");string partial=recorder.RecordingPath;
        var def=(ShipDefinition)Encyclopedia.Lookup["AssaultCarrier1"];
        var ship=NetworkSceneSingleton<Spawner>.i.SpawnShip(def.unitPrefab,new GlobalPosition(1000,0,1000),Quaternion.identity,null,null,1f,false);
        await Until(()=>ship!=null&&ship.GetComponentsInChildren<Hangar>(true).Any(h=>h.attachedUnit==ship&&h.doors!=null&&h.doors.Length>0),25,"Native carrier hangar unavailable");
        await Wait(3);var hangar=ship.GetComponentsInChildren<Hangar>(true).First(h=>h.attachedUnit==ship&&h.doors!=null&&h.doors.Length>0);uint id=ship.persistentID.Id;string path=ReplayWorldPlayback.HangarPath(ship,hangar);
        log("HANGAR LIVE native door initial="+hangar.doorState.openAmount.ToString("F3")+" speed="+hangar.doorSpeed+" path="+path);
        if(hangar.doorState.openAmount>.1f)await hangar.CloseDoors();
        await Wait(.4f);hangar.OpenDoors().Forget();await Until(()=>hangar.doorState.openAmount>.8f,45,"Native hangar did not open");await Wait(.5f);
        hangar.CloseDoors().Forget();await Until(()=>hangar.doorState.openAmount<.1f,45,"Native hangar did not close");await Wait(.5f);
        int faults=recorder.CaptureFailureCount;await ReplayManager.i.SwitchState(ModStates.Idle);string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file))throw new Exception("Hangar recording missing");ReplayIntegrityCheck.Check(file);
        var samples=new List<HangarDoorStateEvent>();using(var input=new BinaryReader(File.OpenRead(file))){var header=ReplayRecordingHeader.Read(input);for(long i=0;i<header.EventCount;i++){var e=ReplayRecordTransport.ReadEvent(input,header.Format);if(e is HangarDoorStateEvent d&&d.unitId==id&&d.hangarPath==path)samples.Add(new HangarDoorStateEvent{Time=d.Time,unitId=id,hangarPath=path,openAmount=d.openAmount,currentSpeed=d.currentSpeed,opening=d.opening,available=d.available,selfDisabled=d.selfDisabled,blendSeconds=d.blendSeconds});ReplayEventFactory.Return(e);}}
        if(faults!=0||samples.Count<5||!samples.Any(s=>s.openAmount>.75f)||!samples.Any(s=>s.openAmount<.1f))throw new Exception("Hangar motion missing from recording samples="+samples.Count+" faults="+faults);
        var high=samples.First(s=>s.openAmount>.75f);var after=samples.Last(s=>s.openAmount<.1f);var middle=samples.First(s=>s.openAmount>.35f&&s.openAmount<.65f);
        var geometry=new Dictionary<HangarDoorStateEvent,(Vector3[],Quaternion[])>();
        foreach(var sample in new[]{high,after,middle})
        {
            foreach(var door in hangar.doors)if(door!=null)door.Move(sample.openAmount);
            geometry[sample]=(hangar.doors.Select(d=>d.transform.localPosition).ToArray(),hangar.doors.Select(d=>d.transform.localRotation).ToArray());
        }
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||player==null)throw new Exception("Hangar replay failed");player.Pause();
        var resolve=typeof(ReplayWorldPlayback).GetMethod("ResolveHangar",BindingFlags.NonPublic|BindingFlags.Static);
        foreach(var expected in new[]{high,after,middle,high,after})
        {
            await player.TimelineJump(expected.Time,0,false);await Wait(.25f);var actor=player.FindActor(id);var replay=actor==null?null:(Hangar)resolve.Invoke(null,new object[]{actor,path});
            if(replay==null)throw new Exception("Hangar missing after seek");float error=Math.Abs(replay.doorState.openAmount-expected.openAmount);
            log("HANGAR LIVE seek="+expected.Time.ToString("F2")+" expected="+expected.openAmount.ToString("F3")+" actual="+replay.doorState.openAmount.ToString("F3")+" error="+error.ToString("F3"));
            if(error>.08f)throw new Exception("Hangar door state not restored after seek");
            var expectedGeometry=geometry[expected];if(replay.doors==null||replay.doors.Length!=expectedGeometry.Item1.Length)throw new Exception("Door geometry differs");
            float worstPosition=0,worstRotation=0;for(int i=0;i<replay.doors.Length;i++){worstPosition=Math.Max(worstPosition,Vector3.Distance(replay.doors[i].transform.localPosition,expectedGeometry.Item1[i]));worstRotation=Math.Max(worstRotation,Quaternion.Angle(replay.doors[i].transform.localRotation,expectedGeometry.Item2[i]));}
            log("HANGAR LIVE geometry positionError="+worstPosition.ToString("F3")+" rotationError="+worstRotation.ToString("F3"));
            if(worstPosition>.02f||worstRotation>.2f)throw new Exception("Hangar visual transforms differ after seek");
        }
        double begin=samples.First(s=>s.openAmount>.05f).Time,end=after.Time;
        await player.TimelineJump(begin,0,false);ReplayMod.Director.ReplayDirector.i.SetPlaybackSpeed(1);player.Resume();
        float timeout=Time.realtimeSinceStartup+25,worst=0;int frames=0;
        while(player.currentVirtualTime<end)
        {
            if(Time.realtimeSinceStartup>timeout)throw new Exception("Hangar playback stalled");await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
            double at=player.currentVirtualTime;int next=samples.FindIndex(s=>s.Time>=at);if(next<=0)continue;
            var expected=HangarDoorSnapshot.At(HangarDoorSnapshot.From(samples[next-1]),HangarDoorSnapshot.From(samples[next]),at);
            var actor=player.FindActor(id);var replay=actor==null?null:(Hangar)resolve.Invoke(null,new object[]{actor,path});if(replay==null)throw new Exception("Hangar missing during playback");
            worst=Math.Max(worst,Math.Abs(replay.doorState.openAmount-expected.openAmount));frames++;
        }
        player.Pause();log("HANGAR LIVE continuous frames="+frames+" maxStateError="+worst.ToString("F3"));
        if(frames<30||worst>.08f)throw new Exception("Hangar motion differs during playback");
        log("HANGAR LIVE PASS native open and close recorded and restored on repeated seeks; samples="+samples.Count);
    }
}
