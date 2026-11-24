using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class EjectionLiveProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    static async UniTask Until(Func<bool> condition,float seconds,string failure){float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new Exception(failure);await UniTask.NextFrame();}}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        RollingRecordingController.Instance.SetEnabled(false);await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Native ejection recording");
        await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Live mission unavailable");TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
        var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording||recorder.IsRolling)throw new Exception("Full recorder unavailable");string partial=recorder.RecordingPath;int liveWrapped=ReplayDeadLatchProbe.Wrapped,productionLiveWrapped=ProductionContinuationProbe.LiveWrapped;
        var def=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
        var craft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,130),null,hq,null,1f,1f);
        if(craft==null)throw new Exception("Native aircraft missing");uint id=craft.persistentID.Id;await Wait(2);
        log("EJECTION LIVE aircraft="+id+" server="+craft.IsServer+" pilots="+(craft.pilots?.Length??-1));craft.StartEjectionSequence();await Wait(7);
        int faults=recorder.CaptureFailureCount;await ReplayManager.i.SwitchState(ModStates.Idle);string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file))throw new Exception("Ejection recording missing");ReplayIntegrityCheck.Check(file);
        var moves=new List<(double time,GlobalPosition pos)>();double ejected=-1;int eyes=0;uint pilotId=0;var eyeTimes=new List<double>();
        using(var reader=new BinaryReader(File.OpenRead(file))){var header=ReplayRecordingHeader.Read(reader);for(long n=0;n<header.EventCount;n++){
            var e=ReplayRecordTransport.ReadEvent(reader,header.Format);if(e is EjectionEvent x&&x.unitId==id&&ejected<0)ejected=x.Time;
            if(e is UpdatePositionEvent move&&move.unitId==id)moves.Add((move.Time,move.position));
            if(e is PilotEjectionStateEvent eye&&eye.aircraftId==id){eyes++;pilotId=eye.unitId;eyeTimes.Add(eye.Time);}ReplayEventFactory.Return(e);
        }}
        if(ejected<0||faults!=0)throw new Exception("Native ejection was not recorded or capture faulted: ejection="+ejected+" faults="+faults);
        var after=moves.Where(m=>m.time>ejected+.2).ToArray();if(after.Length<4)throw new Exception("Aircraft movement after ejection missing: "+after.Length);
        var first=after.First();var last=after.Last();float distance=Vector3.Distance(first.pos.ToLocalPosition(),last.pos.ToLocalPosition());if(distance<5)throw new Exception("Native aircraft did not travel after ejection; fixture inconclusive");
        if(ReplayDeadLatchProbe.Enabled&&ReplayDeadLatchProbe.Wrapped!=liveWrapped)throw new Exception("Live ejection recording unexpectedly wrapped replay continuations");
        if(ReplayDeadLatchProbe.Enabled)log("CONTINUATION LIVE EJECTION PASS no live callbacks wrapped");
        if(ProductionContinuationProbe.Enabled&&ProductionContinuationProbe.LiveWrapped!=productionLiveWrapped)throw new Exception("Production cleanup wrapped live ejection callbacks");
        if(ProductionContinuationProbe.Enabled)log("PRODUCTION CONTINUATION LIVE EJECTION "+ProductionContinuationProbe.Stats);
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var replay=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||replay==null)throw new Exception("Ejection replay failed to open");replay.Pause();
        float worst=0;foreach(var sample in new[]{last,first,after[after.Length/2],last}){
            await replay.TimelineJump(sample.time,0,false);var actor=replay.FindActor(id);if(actor==null)throw new Exception("Aircraft missing during post-ejection movement at "+sample.time);
            float error=Vector3.Distance(actor.transform.position,sample.pos.ToLocalPosition());worst=Math.Max(worst,error);if(error>2)throw new Exception("Post-ejection aircraft position diverged by "+error);
        }
        if(eyeTimes.Count<4)throw new Exception("Pilot eye samples missing from recorded native ejection");
        var director=ReplayDirector.i;if(!director.SelectCameraTarget(id)||!director.SelectCameraRig(5))throw new Exception("Could not select recorded aircraft cockpit: "+director.Status);
        director.SetFirstPersonEjection(true);float worstEye=0;
        foreach(double time in new[]{eyeTimes[eyeTimes.Count/3],eyeTimes[eyeTimes.Count*2/3],eyeTimes[eyeTimes.Count/3]})
        {
            await replay.TimelineJump(time,0,false);await Wait(.25f);
            if(!replay.TryActorBirth(id,out double birth)||!replay.TryEjectionEye(id,birth,out var carrier,out var pose)||carrier==null||carrier.persistentID.Id==0)throw new Exception("Recorded ejection eye unavailable at "+time);
            var camera=SceneSingleton<CameraStateManager>.i;if(camera==null||!director.EjectionCameraActive)throw new Exception("First-person ejection camera did not activate at "+time+" status="+director.Status);
            float error=Vector3.Distance(camera.transform.position,pose.position.ToLocalPosition());worstEye=Math.Max(worstEye,error);if(error>.5f)throw new Exception("Ejection camera displaced from eye by "+error);
        }
        director.SetFirstPersonEjection(false);log("EJECTION EYE PASS three seeks activated cockpit ejection view at recorded pilot eye; maxDistance="+worstEye.ToString("F2")+"m");
        log("EJECTION LIVE PASS native server ejection recorded; aircraft travelled "+distance.ToString("F1")+"m across "+after.Length+" post-ejection samples; four seeks maxError="+worst.ToString("F2")+"m pilotEyeSamples="+eyes+" pilotId="+pilotId);
    }
}
