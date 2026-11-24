using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class InvalidPoseCaptureProbe
{
    static async UniTask Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)await UniTask.NextFrame();}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Invalid pose capture test");
        TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
        var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("Capture unavailable");string partial=recorder.RecordingPath;
        var definition=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
        var aircraft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,definition.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
        await Wait(1);if(aircraft==null)throw new Exception("Aircraft did not spawn");
        var clock=typeof(Recorder).GetField("_virtualTime",BindingFlags.Instance|BindingFlags.NonPublic);var queue=typeof(Recorder).GetMethod("QueueEvent",BindingFlags.Instance|BindingFlags.NonPublic);
        int oldFaults=recorder.CaptureFailureCount;
        var bad=ReplayEventFactory.GetEvent<UpdatePositionEvent>();bad.Time=(double)clock.GetValue(recorder);bad.unitId=aircraft.persistentID.Id;bad.position=new GlobalPosition(float.NaN,0,0);bad.rotation=Quaternion.identity;
        queue.Invoke(recorder,new object[]{bad});
        var turret=ReplayEventFactory.GetEvent<UpdateTurretEvent>();turret.Time=(double)clock.GetValue(recorder);turret.attachedUnitId=aircraft.persistentID.Id;turret.elevationAngle=float.PositiveInfinity;queue.Invoke(recorder,new object[]{turret});
        var damage=ReplayEventFactory.GetEvent<PartDamageEvent>();damage.Time=(double)clock.GetValue(recorder);damage.unitId=aircraft.persistentID.Id;damage.hitPoints=float.NaN;queue.Invoke(recorder,new object[]{damage});
        if(!recorder.IsRecording||recorder.CaptureFailureCount!=oldFaults+3||!recorder.CaptureIssueNotice.Contains("missing"))throw new Exception("Invalid samples did not warn while preserving capture");
        await Wait(1);await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file))throw new Exception("Recording was not published after rejected pose");
        string check=ReplayIntegrityCheck.Check(file);int movements=0;
        using(var reader=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(reader);
            for(long i=0;i<header.EventCount;i++)
            {var e=ReplayRecordTransport.ReadEvent(reader,header.Format);if(e is UpdatePositionEvent move&&move.unitId==aircraft.persistentID.Id)movements++;ReplayEventFactory.Return(e);}
        }
        if(movements<2)throw new Exception("Valid movement did not continue after rejected sample");
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("Recording failed to reopen");
        log("INVALID POSE PASS skipped injected non-finite movement, turret and damage events, warned, continued recording "+movements+" valid actor poses, integrity checked and reopened; "+check);
    }
}
