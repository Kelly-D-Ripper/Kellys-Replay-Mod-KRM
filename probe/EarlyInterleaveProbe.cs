using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class EarlyInterleaveProbe
{
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Interleaved pending actor events");
        TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
        var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("Interleave recorder unavailable");
        string partial=recorder.RecordingPath;var def=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
        Aircraft Spawn(float x)=>NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(x,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
        var a=Spawn(1000);if(a==null)throw new Exception("First aircraft missing");a.SetGear(true);
        var time=AccessTools.Field(typeof(Recorder),"_virtualTime");double before=(double)time.GetValue(recorder);time.SetValue(recorder,before+.1);
        var b=Spawn(1200);if(b==null)throw new Exception("Second aircraft missing");
        AccessTools.Method(typeof(Recorder),"HandleUnitRegister").Invoke(recorder,new object[]{b,b.persistentID});
        await ReplayManager.i.SwitchState(ModStates.Idle);
        if(recorder.CaptureFailureCount!=0)throw new Exception("Interleave capture warning: "+recorder.CaptureFailureCount);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file)||!ReplayIntegrityCheck.Check(file).Contains("passed"))throw new Exception("Interleaved recording invalid");
        int pos=0,spawnA=-1,spawnB=-1,gearA=-1;double spawnTime=-1;
        using(var input=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(input);for(long n=0;n<header.EventCount;n++)
            {
                var e=ReplayRecordTransport.ReadEvent(input,header.Format);pos++;
                if(e is SpawnEvent spawn){if(spawn.unitId==a.persistentID.Id){spawnA=pos;spawnTime=spawn.Time;}if(spawn.unitId==b.persistentID.Id)spawnB=pos;}
                if(e is SetGearEvent gear&&gear.unitId==a.persistentID.Id&&gear.deployed){gearA=pos;if(Math.Abs(gear.Time-(before+.1))>.001)throw new Exception("Deferred time was not safely clamped");}
                ReplayEventFactory.Return(e);
            }
        }
        if(spawnB<0||spawnA<=spawnB||gearA<=spawnA)throw new Exception("Interleaved event order incorrect");
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;
        if(player==null||!ReplayRuntime.IsPlayback)throw new Exception("Interleaved replay did not open");player.Pause();await player.TimelineJump(spawnTime,0,false);
        if(!(player.FindActor(a.persistentID.Id) is Aircraft restored)||!restored.gearDeployed)throw new Exception("Deferred gear not restored");
        log("EARLY INTERLEAVE PASS buffered event retimed after another actor, integrity passed and gear restored on seek");
    }
}
