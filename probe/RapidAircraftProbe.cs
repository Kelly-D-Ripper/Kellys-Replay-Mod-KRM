using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class RapidAircraftProbe
{
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Immediate stop registration test");
        TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
        var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("Rapid test recorder unavailable");
        string partial=recorder.RecordingPath;var def=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];
        var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
        Action<Aircraft,bool> observeGear=(a,value)=>{var pending=(System.Collections.IDictionary)HarmonyLib.AccessTools.Field(typeof(Recorder),"_pendingUnitRegistrations").GetValue(recorder);var tracked=(System.Collections.IDictionary)HarmonyLib.AccessTools.Field(typeof(Recorder),"_unitsOnTrack").GetValue(recorder);log("RAPID CALLBACK gear="+value+" id="+a.persistentID.Id+" pending="+pending.Contains(a.persistentID)+" tracked="+tracked.Contains(a.persistentID)+" loadout="+(a.loadout!=null));};
        if(Environment.GetEnvironmentVariable("REPLAY_EARLY_GEAR")=="1")ReplayMod.Patches.AircraftPatch.onSetGear+=observeGear;
        var craft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
        if(craft==null||craft.loadout?.weapons==null)throw new Exception("Rapid aircraft did not initialize");
        bool fallback=Environment.GetEnvironmentVariable("REPLAY_FALLBACK_AIRCRAFT")=="1";
        uint id=craft.persistentID.Id;string[] expected=fallback?craft.weaponManager.hardpointSets.Select(w=>w?.weaponMount?.jsonKey??"NULLPTR").ToArray():craft.loadout.weapons.Select(w=>w?.jsonKey??"NULLPTR").ToArray();
        bool earlyGun=Environment.GetEnvironmentVariable("REPLAY_EARLY_GUN")=="1";
        if(earlyGun)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(2));
            var allGuns=UnityEngine.Object.FindObjectsOfType<Gun>();log("EARLY GUN scene="+allGuns.Length+" mounts="+string.Join(",",craft.loadout.weapons.Select(w=>w?.jsonKey??"none"))+" details="+string.Join(";",allGuns.Select(g=>(g.info?.weaponName??"unnamed")+"/attached="+(g.attachedUnit==craft)+"/guided="+(g.guidedProjectile!=null))));
            var gun=allGuns.FirstOrDefault(g=>g!=null&&g.attachedUnit==craft&&g.guidedProjectile==null);
            if(gun==null)throw new Exception("No native aircraft gun found for early shot");
            var unregister=HarmonyLib.AccessTools.Method(typeof(Recorder),"HandleUnitUnregister");var register=HarmonyLib.AccessTools.Method(typeof(Recorder),"HandleUnitRegister");
            unregister.Invoke(recorder,new object[]{craft});
            var loadout=craft.loadout;craft.loadout=null;register.Invoke(recorder,new object[]{craft,craft.persistentID});craft.loadout=loadout;
            log("EARLY GUN invoking "+gun.info?.weaponName+" while registration is pending");gun.SpawnBullet(0);
        }
        bool overflow=Environment.GetEnvironmentVariable("REPLAY_EARLY_OVERFLOW")=="1";
        bool earlyGear=Environment.GetEnvironmentVariable("REPLAY_EARLY_GEAR")=="1"||overflow;
        if(earlyGear)
        {
            craft.SetGear(true);ReplayMod.Patches.AircraftPatch.onSetGear-=observeGear;
            var part=craft.partLookup.First(x=>x!=null);
            ReplayMod.Patches.PartDamagePatch.OnDamage?.Invoke(part,3,0,0,0);
            ReplayMod.Patches.EjectionVisualPatch.OnEjection?.Invoke(craft,false);
            if(overflow)for(int i=0;i<150;i++)ReplayMod.Patches.AircraftPatch.onSetGear?.Invoke(craft,true);
        }
        // Stop in the same frame in which the native registry fires, before a
        // normal recorder Update can flush its pending registrations.
        var nativeLoadout=craft.loadout;
        if(fallback)craft.loadout=null;
        var stopping=ReplayManager.i.SwitchState(ModStates.Idle);
        if(fallback)craft.loadout=nativeLoadout;
        await stopping;
        if((fallback||overflow)&&(recorder.CaptureFailureCount==0||ReplayManager.i.LastRecordingStopStatus.IndexOf("details may be missing",StringComparison.OrdinalIgnoreCase)<0))throw new Exception("Fallback did not warn about incomplete stores");
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file)||!ReplayIntegrityCheck.Check(file).Contains("passed"))throw new Exception("Rapid recording missing or invalid");
        int spawns=0,earlyGearEvents=0,earlyOtherEvents=0,shots=0;var actionOrder=new System.Collections.Generic.List<string>();string[] stored=null;double spawnTime=-1;
        using(var input=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(input);
            for(long n=0;n<header.EventCount;n++)
            {
                var e=ReplayRecordTransport.ReadEvent(input,header.Format);
                if(e is SpawnEvent spawn&&spawn.unitId==id){spawns++;spawnTime=spawn.Time;stored=spawn.weapons?.Select(w=>w?.jsonKey??"NULLPTR").ToArray();log("RAPID ORDER spawn time="+spawn.Time.ToString("F4"));}
                if(e is GunBulletEvent bullet&&bullet.unitId==id){shots++;if(spawns<(earlyGun?2:1))earlyOtherEvents++;}
                if(e is SetGearEvent gear&&gear.unitId==id){if(!overflow)log("RAPID ORDER gear="+gear.deployed+" time="+gear.Time.ToString("F4")+" spawns="+spawns);if(spawns==0)earlyGearEvents++;else actionOrder.Add("gear:"+gear.deployed);}
                if(e is PartDamageEvent damage&&damage.unitId==id&&damage.pierce==3){if(spawns==0)earlyOtherEvents++;else actionOrder.Add("damage");}
                if(e is EjectionEvent eject&&eject.unitId==id){if(spawns==0)earlyOtherEvents++;else actionOrder.Add("ejection");}
                ReplayEventFactory.Return(e);
            }
        }
        if(earlyGun&&(shots<1||earlyOtherEvents>0))throw new Exception("Early native gun shot missing or before spawn: shots="+shots+" before="+earlyOtherEvents);
        if(earlyGun)log("EARLY GUN PASS native shot captured after aircraft spawn");
        if(earlyGear&&(earlyGearEvents>0||earlyOtherEvents>0||actionOrder.Count<4||actionOrder[0]!="gear:False"||actionOrder[1]!="gear:True"||actionOrder[2]!="damage"||actionOrder[3]!="ejection"))throw new Exception("Early action order failed: pre-spawn="+(earlyGearEvents+earlyOtherEvents)+" after="+string.Join(",",actionOrder));
        if(earlyGear)log("EARLY EVENTS PASS gear, damage and ejection callbacks follow spawn in original order");
        if(overflow){if(actionOrder.Count!=129)throw new Exception("Early event buffer count was not bounded: "+actionOrder.Count);log("EARLY OVERFLOW PASS 128 deferred actions retained; overflow warned and recording reopened");}
        if(spawns!=(earlyGun?2:1)||stored==null||!stored.SequenceEqual(expected))throw new Exception("Same-frame stop lost spawn or loadout");
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;
        if(!ReplayRuntime.IsPlayback||player==null)throw new Exception("Same-frame spawn recording could not reopen");
        player.Pause();await player.TimelineJump(spawnTime,0,false);
        var replayAircraft=player.FindActor(id) as Aircraft;
        if(replayAircraft==null)throw new Exception("Same-frame aircraft missing at its recorded spawn time");
        if(earlyGear&&!replayAircraft.gearDeployed)throw new Exception("Early deployed gear was not restored on seek");
        log((fallback?"FALLBACK AIRCRAFT PASS same-frame stop warned and saved available hardpoints: ":"RAPID AIRCRAFT PASS same-frame stop recorded one initialized spawn with all ")+stored.Length+" loadout slots and reopened");
    }
}
