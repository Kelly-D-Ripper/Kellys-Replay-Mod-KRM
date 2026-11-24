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

public static class RegistrationReuseProbe
{
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Pending ID reuse test");
        TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
        var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("Reuse recorder unavailable");
        string partial=recorder.RecordingPath;var def=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];
        var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
        Aircraft Spawn(float x)=>NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(x,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
        var old=Spawn(1000);var replacement=Spawn(1200);if(old==null||replacement==null)throw new Exception("Reuse aircraft unavailable");
        var id=old.persistentID;var part=old.partLookup.First(p=>p!=null);
        ReplayMod.Patches.PartDamagePatch.OnDamage?.Invoke(part,3,0,0,0);
        var pending=(System.Collections.IDictionary)AccessTools.Field(typeof(Recorder),"_pendingUnitRegistrations").GetValue(recorder);
        if(!pending.Contains(id))throw new Exception("Old actor was not pending");
        // Simulate an incoming registry callback reusing the old actor's ID.
        // The live native objects themselves are not modified.
        AccessTools.Method(typeof(Recorder),"HandleUnitRegister").Invoke(recorder,new object[]{replacement,id});
        if(recorder.CaptureFailureCount==0)throw new Exception("ID reuse was not reported");
        await ReplayManager.i.SwitchState(ModStates.Idle);
        if(ReplayManager.i.LastRecordingStopStatus.IndexOf("details may be missing",StringComparison.OrdinalIgnoreCase)<0)throw new Exception("ID reuse warning not shown");
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file)||!ReplayIntegrityCheck.Check(file).Contains("passed"))throw new Exception("Reuse recording missing or corrupt");
        using(var input=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(input);int orphanDamage=0;
            for(long n=0;n<header.EventCount;n++){var e=ReplayRecordTransport.ReadEvent(input,header.Format);if(e is PartDamageEvent damage&&damage.unitId==id.Id&&damage.pierce==3)orphanDamage++;ReplayEventFactory.Return(e);}
            if(orphanDamage!=0)throw new Exception("Old actor's early damage was transferred to replacement");
        }
        log("PENDING ID REUSE PASS old actor actions discarded with warning; checked recording integrity");
    }
}
