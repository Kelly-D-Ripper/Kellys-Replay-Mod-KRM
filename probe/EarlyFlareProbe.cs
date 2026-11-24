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

public static class EarlyFlareProbe
{
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Pending flare test");
        TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
        var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("Flare recorder unavailable");
        bool chaff=Environment.GetEnvironmentVariable("REPLAY_EARLY_CHAFF")=="1";
        string partial=recorder.RecordingPath;var def=chaff?Encyclopedia.Lookup.Values.OfType<AircraftDefinition>().FirstOrDefault(d=>d.unitPrefab!=null&&d.unitPrefab.GetComponentsInChildren<ChaffEjector>(true).Length>0):(AircraftDefinition)Encyclopedia.Lookup["CAS1"];
        if(def==null)throw new Exception("No aircraft prefab with chaff available");log("COUNTERMEASURE aircraft="+def.jsonKey);var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
        var craft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
        await UniTask.Delay(TimeSpan.FromSeconds(2));
        var ejector=UnityEngine.Object.FindObjectsOfType<FlareEjector>().FirstOrDefault(e=>e.aircraft==craft&&e.flarePrefab!=null&&e.ejectionPoints!=null&&e.ejectionPoints.Length>0&&e.ejectionPoints[0].transform!=null);
        var chaffEjector=UnityEngine.Object.FindObjectsOfType<ChaffEjector>().FirstOrDefault(e=>e.aircraft==craft&&e.chaffPrefab!=null&&e.ejectionPoints!=null&&e.ejectionPoints.Length>0&&e.ejectionPoints[0].transform!=null);
        if((!chaff&&ejector==null)||(chaff&&chaffEjector==null))throw new Exception("No initialized native countermeasure ejector found");
        AccessTools.Method(typeof(Recorder),"HandleUnitUnregister").Invoke(recorder,new object[]{craft});
        var loadout=craft.loadout;craft.loadout=null;AccessTools.Method(typeof(Recorder),"HandleUnitRegister").Invoke(recorder,new object[]{craft,craft.persistentID});craft.loadout=loadout;
        bool overflow=Environment.GetEnvironmentVariable("REPLAY_FLARE_OVERFLOW")=="1";
        bool reuse=Environment.GetEnvironmentVariable("REPLAY_FLARE_REUSE")=="1";
        if(overflow)for(int i=0;i<140;i++)ReplayMod.Patches.AircraftPatch.onSetGear?.Invoke(craft,false);
        var point=chaff?chaffEjector.ejectionPoints[0].transform:ejector.ejectionPoints[0].transform;
        var obj=UnityEngine.Object.Instantiate(chaff?chaffEjector.chaffPrefab:ejector.flarePrefab,point.position,point.rotation);Component source;
        if(chaff){var value=obj.GetComponent<RadarChaff>();if(value==null)throw new Exception("Native chaff component missing");value.LaunchChaff(craft,point,craft.rb.velocity+Vector3.down*20);source=value;}
        else{var value=obj.GetComponent<IRFlare>();if(value==null)throw new Exception("Native flare component missing");value.LaunchFlare(craft,point,craft.rb.velocity+Vector3.down*20);source=value;}
        var active=(System.Collections.IDictionary)AccessTools.Field(typeof(Recorder),"_recordedCountermeasures").GetValue(recorder);
        if(overflow&&active.Contains(source))throw new Exception("Dropped flare launch left an active tracker");
        if(reuse)
        {
            if(!active.Contains(source))throw new Exception("Pending flare was not tracked before ID reuse");
            var replacement=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1200,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
            AccessTools.Method(typeof(Recorder),"HandleUnitRegister").Invoke(recorder,new object[]{replacement,craft.persistentID});
            if(active.Contains(source))throw new Exception("Abandoned flare tracker survived ID reuse");
        }
        bool samplePending=Environment.GetEnvironmentVariable("REPLAY_FLARE_SAMPLE")=="1";
        if(samplePending)
        {
            var time=AccessTools.Field(typeof(Recorder),"_virtualTime");time.SetValue(recorder,(double)time.GetValue(recorder)+.1);
            AccessTools.Method(typeof(Recorder),"CaptureCountermeasuresLate").Invoke(recorder,null);
        }
        recorder.RecordCountermeasureEnd(source);uint id=craft.persistentID.Id;
        await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file)||!ReplayIntegrityCheck.Check(file).Contains("passed"))throw new Exception("Pending flare recording missing or corrupt");
        int spawns=0,flares=0,before=0,ends=0,samples=0;uint countermeasureId=0;using(var input=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(input);for(long n=0;n<header.EventCount;n++)
            {var e=ReplayRecordTransport.ReadEvent(input,header.Format);if(e is SpawnEvent spawn&&spawn.unitId==id)spawns++;if(e is CountermeasureEvent cm){if(cm.unitId==id&&cm.phase==CountermeasurePhase.Launch&&cm.kind==(chaff?CountermeasureKind.Chaff:CountermeasureKind.Flare)){flares++;countermeasureId=cm.countermeasureId;if(spawns<2)before++;}else if(cm.countermeasureId==countermeasureId&&countermeasureId!=0){if(cm.phase==CountermeasurePhase.Sample)samples++;if(cm.phase==CountermeasurePhase.End)ends++;if(spawns<2)before++;}}ReplayEventFactory.Return(e);}
        }
        if(overflow||reuse)
        {
            if(flares!=0||ends!=0||recorder.CaptureFailureCount==0||ReplayManager.i.LastRecordingStopStatus.IndexOf("details may be missing",StringComparison.OrdinalIgnoreCase)<0)throw new Exception("Discarded countermeasure was recorded or warning missing");
            log("EARLY FLARE "+(reuse?"REUSE":"OVERFLOW")+" PASS no orphan tracker or launch; capture warning shown");return;
        }
        if(spawns!=2||flares<1||ends!=1||before>0||(samplePending&&samples<1))throw new Exception("Pending native countermeasure launch/sample/end missing or before its spawn: spawns="+spawns+" launches="+flares+" samples="+samples+" ends="+ends+" early="+before);
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("Flare replay did not open");
        log("EARLY "+(chaff?"CHAFF":"FLARE")+" PASS native launch, "+samples+" pending samples and immediate end captured after aircraft spawn and replay reopened");
    }
}
