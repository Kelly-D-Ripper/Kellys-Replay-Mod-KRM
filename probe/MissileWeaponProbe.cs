using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using UnityEngine;

// Fires each weapon station of realistic loadouts natively, one at a time, and
// compares every mounted store's presentation live versus at the same replay time.
public static class MissileWeaponProbe
{
    internal static readonly List<string> ignitions=new List<string>();
    static async UniTask Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)await UniTask.NextFrame();}
    static async UniTask Until(Func<bool> ready,float timeout,string reason){float end=Time.realtimeSinceStartup+timeout;while(!ready()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(reason);await UniTask.NextFrame();}}
    static string Path(Transform t)
    {
        var part=t.GetComponentInParent<UnitPart>();var path=new List<string>();var current=t;
        while(current!=null&&(part==null||current!=part.transform)&&path.Count<64){path.Add(current.name+"#"+(current.parent!=null?current.GetSiblingIndex():0));current=current.parent;}
        path.Reverse();return (part!=null?"part"+part.id:"root")+"/"+string.Join("/",path);
    }
    static MountedMissile[] Mounts(Aircraft a)=>a.partLookup.Where(p=>p!=null).SelectMany(p=>p.GetComponentsInChildren<MountedMissile>(true))
        .Concat(a.GetComponentsInChildren<MountedMissile>(true)).Where(m=>m!=null).Distinct().ToArray();
    static Dictionary<string,string> Visual(Aircraft a)
    {
        var result=new Dictionary<string,string>();
        foreach(var m in Mounts(a))
        {
            int shown=m.GetComponentsInChildren<MeshRenderer>(true).Count(r=>r.enabled&&r.gameObject.activeInHierarchy);
            var local=m.transform.localPosition;
            result[Path(m.transform)]=$"active={m.gameObject.activeInHierarchy} shown={shown} pos=({local.x:F2},{local.y:F2},{local.z:F2})";
        }
        return result;
    }
    static NuclearOption.SavedMission.Loadout Mixed(AircraftDefinition d)
    {
        var loadouts=d.aircraftParameters?.loadouts;
        if(loadouts!=null&&loadouts.Count>0)return loadouts.OrderByDescending(l=>l.weapons.Where(w=>w!=null).Select(w=>w.jsonKey).Distinct().Count()).First();
        var sets=d.unitPrefab.GetComponent<Aircraft>().weaponManager.hardpointSets;var loadout=new NuclearOption.SavedMission.Loadout();
        foreach(var set in sets)loadout.weapons.Add(set.weaponOptions?.FirstOrDefault(w=>w!=null));return loadout;
    }
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        float oldVolume=AudioListener.volume;AudioListener.volume=0;
        try
        {
            await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Mixed weapon presentation test");
            await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Live scene unavailable");TimeScaleManager.Scale=1;
            var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
            await ReplayManager.i.SwitchState(ModStates.Record);
            await Until(()=>Recorder.Active!=null&&Recorder.Active.IsRecording,15,"Recorder unavailable");
            var recorder=Recorder.Active;string partial=recorder.RecordingPath;
            var clock=AccessTools.Field(typeof(Recorder),"_virtualTime");double Now()=>(double)clock.GetValue(recorder);
            var names=(Environment.GetEnvironmentVariable("REPLAY_WEAPON_AIRCRAFT")??"SmallFighter1,Multirole1,AttackHelo1,CAS1,Fighter1,COIN").Split(',');
            var steps=new List<(uint id,string craft,string action,double time,Dictionary<string,string> live)>();
            float x=1000;
            foreach(var name in names)
            {
                if(!Encyclopedia.Lookup.TryGetValue(name,out var value)||!(value is AircraftDefinition definition)){log("WEAPON missing "+name);continue;}
                var loadout=Mixed(definition);
                var craft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,definition.unitPrefab,loadout,1f,default(LiveryKey),new GlobalPosition(x,4000,1000),Quaternion.identity,new Vector3(0,0,120),null,hq,null,1f,1f);
                x+=600;uint id=craft.persistentID.Id;await Wait(3);
                log("WEAPON "+name+" loadout="+string.Join(",",loadout.weapons.Select(w=>w?.jsonKey??"-"))+" mountsAll="+Mounts(craft).Length+" mountsFromRoot="+craft.GetComponentsInChildren<MountedMissile>(true).Length);
                steps.Add((id,name,"baseline",Now(),Visual(craft)));
                for(int s=0;s<(craft.weaponStations?.Count??0);s++)
                {
                    var station=craft.weaponStations[s];var weapon=station?.Weapons?.FirstOrDefault(w=>w!=null&&w.ammo>0);
                    if(weapon==null)continue;
                    try{weapon.Fire(craft,null,craft.rb.velocity,station,craft.GlobalPosition());}
                    catch(Exception error){log("WEAPON "+name+" station "+s+" fire failed: "+error.Message);continue;}
                    await Wait(1.5f);
                    steps.Add((id,name,"station "+s+" "+weapon.GetType().Name+" "+(station.WeaponInfo?.weaponName??weapon.name),Now(),Visual(craft)));
                    await Wait(1.5f);
                }
            }
            await Wait(1);
            await ReplayManager.i.SwitchState(ModStates.Idle);
            string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
            if(!File.Exists(file))throw new Exception("Final recording missing: "+ReplayManager.i.LastRecordingStopStatus);
            await ReplayManager.i.StartPlaying(System.IO.Path.GetFileName(file));
            var player=ReplayManager.i.Playback;player.Pause();
            int differences=0;
            foreach(var step in steps)
            {
                await player.TimelineJump(step.time,0,false);await UniTask.NextFrame();await UniTask.NextFrame();
                var actor=player.FindActor(step.id) as Aircraft;if(actor==null){log("WEAPON REPLAY "+step.craft+" missing at "+step.time);differences++;continue;}
                var replay=Visual(actor);
                var diff=step.live.Keys.Union(replay.Keys).Where(k=>!step.live.TryGetValue(k,out var l)||!replay.TryGetValue(k,out var r)||l!=r).ToArray();
                differences+=diff.Length;
                log("WEAPON REPLAY "+step.craft+" t="+step.time.ToString("F2")+" "+step.action+" "+(diff.Length==0?"MATCH":"DIFF "+diff.Length));
                foreach(var k in diff)log("   "+k+" live: "+(step.live.TryGetValue(k,out var l)?l:"absent")+" | replay: "+(replay.TryGetValue(k,out var r)?r:"absent"));
            }
            ignitions.Clear();
            await player.TimelineJump(0,0,false);player.Resume();await Wait((float)Math.Min(player.replayDuration,90)+1);player.Pause();
            log("WEAPON playback motor ignitions="+ignitions.Count);foreach(var line in ignitions.Take(40))log("   "+line);
            log("WEAPON RESULT differences="+differences);
        }
        finally{AudioListener.volume=oldVolume;}
    }
}

[HarmonyPatch]
static class ProbeMotorActivate
{
    static System.Reflection.MethodBase TargetMethod()=>AccessTools.Method(AccessTools.Inner(typeof(Missile),"Motor"),"Activate");
    static void Prefix(Missile missile)
    {
        if(!ReplayRuntime.IsPlayback||missile==null)return;
        var player=ReplayManager.i?.Playback;
        MissileWeaponProbe.ignitions.Add("t="+(player?.currentVirtualTime??-1).ToString("F2")+" "+missile.definition?.jsonKey+" age="+missile.timeSinceSpawn.ToString("F2"));
    }
}
