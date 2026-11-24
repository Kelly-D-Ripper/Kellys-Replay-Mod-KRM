using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using UnityEngine;

// Investigates replayed "launch animations" on the wrong weapon. Part 1 compares
// the production aircraft visual channel table at the playback spawn frame with
// the table after native loadout initialization. Part 2 records native bay door
// openings one hardpoint group at a time and checks which doors open in replay.
public static class MissileLaunchProbe
{
    internal static int playbackOpenDoor, playbackSpring, playbackMountFire;
    static async UniTask Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)await UniTask.NextFrame();}
    static async UniTask Until(Func<bool> ready,float timeout,string reason){float end=Time.realtimeSinceStartup+timeout;while(!ready()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(reason);await UniTask.NextFrame();}}
    static readonly Type BindingType=typeof(UnitController).Assembly.GetType("ReplayMod.Core.ReplayAircraftVisuals");
    static (string key,Transform target)[] Channels(Aircraft aircraft)
    {
        var binding=Activator.CreateInstance(BindingType,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new object[]{aircraft},null);
        var channels=(Array)BindingType.GetField("channels").GetValue(binding);
        var result=new List<(string,Transform)>();
        foreach(var channel in channels)
        {
            var type=channel.GetType();
            result.Add(((string)type.GetField("key").GetValue(channel),(Transform)type.GetField("target").GetValue(channel)));
        }
        return result.ToArray();
    }
    static string DoorId(Transform t)
    {
        if(t==null)return "null";
        var part=t.GetComponentInParent<UnitPart>();var path=new List<string>();var current=t;
        while(current!=null&&(part==null||current!=part.transform)&&path.Count<64)
        {
            int index=current.parent!=null?current.GetSiblingIndex():0;path.Add(current.name+"#"+index);current=current.parent;
        }
        path.Reverse();var weapon=t.GetComponentInParent<Weapon>();
        return (part!=null?"part"+part.id:"root")+"/"+string.Join("/",path)+(weapon!=null?" [in weapon "+weapon.GetType().Name+"]":"");
    }
    static NuclearOption.SavedMission.Loadout BayLoadout(AircraftDefinition definition)
    {
        var sets=definition.unitPrefab.GetComponent<Aircraft>().weaponManager.hardpointSets;
        var loadout=new NuclearOption.SavedMission.Loadout();
        foreach(var set in sets)
        {
            int Score(WeaponMount w)=>w==null||w.prefab==null?-1:w.prefab.GetComponentsInChildren<BayDoor>(true).Length*4+
                (w.prefab.GetComponentInChildren<MountedMissile>(true)!=null?2:0)+(w.prefab.GetComponentInChildren<Gun>(true)!=null?1:0);
            loadout.weapons.Add(set.weaponOptions==null?null:set.weaponOptions.OrderByDescending(Score).FirstOrDefault(w=>Score(w)>0));
        }
        return loadout;
    }
    static Aircraft Spawn(AircraftDefinition definition,NuclearOption.SavedMission.Loadout loadout,FactionHQ hq,float x)
        => NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,definition.unitPrefab,loadout,1f,default(LiveryKey),new GlobalPosition(x,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
    static Dictionary<string,Quaternion> Doors(Aircraft aircraft)
        => aircraft.GetComponentsInChildren<BayDoor>(true).Concat(aircraft.partLookup.Where(p=>p!=null).SelectMany(p=>p.GetComponentsInChildren<BayDoor>(true)))
            .Distinct().GroupBy(d=>DoorId(d.transform)).ToDictionary(g=>g.Key,g=>g.First().transform.localRotation);
    static HashSet<string> Open(Dictionary<string,Quaternion> closed,Dictionary<string,Quaternion> now)
        => new HashSet<string>(now.Where(p=>closed.TryGetValue(p.Key,out var c)&&Quaternion.Angle(c,p.Value)>5f).Select(p=>p.Key));

    public static async UniTask Run(MapKey key,Action<string> log)
    {
        float oldVolume=AudioListener.volume;AudioListener.volume=0;
        try
        {
            await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Missile launch presentation test");
            await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Live scene unavailable");TimeScaleManager.Scale=1;
            var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
            AircraftDefinition worst=null;int worstScore=-1;
            foreach(var definition in Encyclopedia.Lookup.Values.OfType<AircraftDefinition>().Distinct().OrderBy(d=>d.jsonKey))
            {
                Aircraft craft=null;
                try
                {
                    if(definition.unitPrefab==null||definition.unitPrefab.GetComponent<Aircraft>()?.weaponManager?.hardpointSets==null)continue;
                    var loadout=BayLoadout(definition);
                    craft=Spawn(definition,loadout,hq,1000);
                    // Playback creates its binding from the first visual pass after Execute(SpawnEvent).
                    var atSpawn=craft.partLookup!=null&&craft.partLookup.Count>0?Channels(craft):null;
                    int mountsAtSpawn=craft.GetComponentsInChildren<MountedMissile>(true).Length;
                    await UniTask.NextFrame();var nextFrame=Channels(craft);
                    await Wait(2);var settled=Channels(craft);int mountsSettled=craft.GetComponentsInChildren<MountedMissile>(true).Length;
                    var doors=settled.Where(c=>c.key.StartsWith("bay:")).ToArray();
                    string Map((string key,Transform target)[] table)=>table==null?"no-binding":string.Join(" | ",table.Where(c=>c.key.StartsWith("bay:")).Select(c=>c.key+"="+DoorId(c.target)));
                    bool keysDiffer=atSpawn==null||!atSpawn.Select(c=>c.key).SequenceEqual(settled.Select(c=>c.key));
                    int wrongDoors=atSpawn==null?doors.Length:atSpawn.Count(c=>c.key.StartsWith("bay:")&&settled.Any(s=>s.key==c.key&&s.target!=c.target));
                    int missing=atSpawn==null?doors.Length:doors.Count(s=>!atSpawn.Any(c=>c.key==s.key));
                    string mountedDoors=string.Join(";",craft.GetComponentsInChildren<MountedMissile>(true).Where(m=>m.bayDoors!=null&&m.bayDoors.Length>0)
                        .Select(m=>m.name+"->"+string.Join(",",m.bayDoors.Select(d=>d==null?"null":DoorId(d.transform)))));
                    log($"INVENTORY {definition.jsonKey} doors={doors.Length} mounts spawn/settled={mountsAtSpawn}/{mountsSettled} channels spawn/next/settled={(atSpawn?.Length??-1)}/{nextFrame.Length}/{settled.Length} keysDiffer={keysDiffer} wrongDoorBindings={wrongDoors} missingDoorKeys={missing}");
                    if(doors.Length>0)
                    {
                        log("INVENTORY "+definition.jsonKey+" SPAWN  "+Map(atSpawn));
                        log("INVENTORY "+definition.jsonKey+" SETTLED "+Map(settled));
                        if(mountedDoors.Length>0)log("INVENTORY "+definition.jsonKey+" MOUNT-DOORS "+mountedDoors);
                        foreach(var hp in craft.weaponManager.hardpointSets.SelectMany(s=>s.hardpoints??new List<Hardpoint>()).Where(h=>h?.bayDoors!=null&&h.bayDoors.Length>0))
                            log("INVENTORY "+definition.jsonKey+" HARDPOINT "+hp.HardpointIndex+" mount="+(hp.mount?.jsonKey??"none")+" doors="+string.Join(",",hp.bayDoors.Select(d=>d==null?"null":DoorId(d.transform))));
                    }
                    int score=(wrongDoors+missing)*100+doors.Length;
                    if(doors.Length>0&&score>worstScore){worstScore=score;worst=definition;}
                }
                catch(Exception error){log("INVENTORY "+definition.jsonKey+" skipped: "+error.GetType().Name+": "+error.Message);}
                finally{if(craft!=null)UnityEngine.Object.Destroy(craft.gameObject);await UniTask.NextFrame();}
            }
            if(worst==null){log("MISSILE LAUNCH no aircraft with bay doors; end-to-end skipped");return;}
            await EndToEnd(worst,hq,log);
        }
        finally{AudioListener.volume=oldVolume;}
    }

    static async UniTask EndToEnd(AircraftDefinition definition,FactionHQ hq,Action<string> log)
    {
        log("E2E aircraft="+definition.jsonKey);
        await ReplayManager.i.SwitchState(ModStates.Record);
        await Until(()=>Recorder.Active!=null&&Recorder.Active.IsRecording,15,"Recorder unavailable");
        var recorder=Recorder.Active;string partial=recorder.RecordingPath;
        var clock=AccessTools.Field(typeof(Recorder),"_virtualTime");double Now()=>(double)clock.GetValue(recorder);
        var craft=Spawn(definition,BayLoadout(definition),hq,1000);uint id=craft.persistentID.Id;
        await Wait(4);
        var closed=Doors(craft);double closedAt=Now();
        var groups=craft.weaponManager.hardpointSets.SelectMany(s=>s.hardpoints??new List<Hardpoint>()).Where(h=>h?.bayDoors!=null&&h.bayDoors.Length>0)
            .GroupBy(h=>string.Join(",",h.bayDoors.Select(d=>d==null?"":DoorId(d.transform)).OrderBy(s=>s))).Select(g=>g.First()).ToArray();
        var expectations=new List<(double time,string trigger,HashSet<string> open)>();
        foreach(var hardpoint in groups)
        {
            hardpoint.SpringOpenBayDoors();
            await Wait(.8f);var open=Open(closed,Doors(craft));double at=Now();
            expectations.Add((at,"hardpoint "+hardpoint.HardpointIndex+" ("+(hardpoint.mount?.jsonKey??"none")+")",open));
            log("E2E LIVE t="+at.ToString("F2")+" opened hardpoint "+hardpoint.HardpointIndex+" open=["+string.Join(", ",open)+"]");
            await Wait(Mathf.Max(3f,hardpoint.doorOpenDuration+2.5f));
        }
        // Also fire one real mounted missile natively, if present, to compare its own doors.
        var station=craft.weaponStations?.FirstOrDefault(s=>s?.Weapons!=null&&s.Weapons.Any(w=>w is MountedMissile m&&m.bayDoors!=null&&m.bayDoors.Length>0&&m.ammo>0));
        if(station!=null)
        {
            var weapon=(MountedMissile)station.Weapons.First(w=>w is MountedMissile m&&m.bayDoors!=null&&m.bayDoors.Length>0&&m.ammo>0);
            weapon.Fire(craft,null,craft.rb.velocity,station,craft.GlobalPosition());
            await Wait(.8f);var open=Open(closed,Doors(craft));double at=Now();
            expectations.Add((at,"MountedMissile.Fire "+weapon.name,open));
            log("E2E LIVE t="+at.ToString("F2")+" fired "+weapon.name+" open=["+string.Join(", ",open)+"]");
            await Wait(4);
        }
        await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file))throw new Exception("Final recording missing: "+ReplayManager.i.LastRecordingStopStatus);
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));
        var player=ReplayManager.i.Playback;player.Pause();
        playbackOpenDoor=playbackSpring=playbackMountFire=0;
        int mismatches=0;
        for(int pass=0;pass<2;pass++)
        {
            await player.TimelineJump(closedAt,0,false);await UniTask.NextFrame();await UniTask.NextFrame();
            var actor=player.FindActor(id) as Aircraft;if(actor==null)throw new Exception("Replay aircraft missing");
            var replayClosed=Doors(actor);
            foreach(var expected in expectations)
            {
                await player.TimelineJump(expected.time,0,false);await UniTask.NextFrame();await UniTask.NextFrame();
                actor=player.FindActor(id) as Aircraft;
                var open=Open(replayClosed,Doors(actor));
                bool same=open.SetEquals(expected.open);if(!same)mismatches++;
                log("E2E REPLAY pass="+pass+" t="+expected.time.ToString("F2")+" "+expected.trigger+" "+(same?"MATCH":"MISMATCH")+
                    " live=["+string.Join(", ",expected.open)+"] replay=["+string.Join(", ",open)+"]");
            }
        }
        // Let playback run across the whole recording once to catch native launch calls.
        await player.TimelineJump(0,0,false);player.Resume();await Wait((float)Math.Min(player.replayDuration,40)+1);player.Pause();
        log("E2E native calls during playback: BayDoor.OpenDoor="+playbackOpenDoor+" SpringOpenBayDoors="+playbackSpring+" MountedMissile.Fire="+playbackMountFire);
        log("E2E RESULT mismatches="+mismatches);
    }
}

[HarmonyPatch(typeof(BayDoor),nameof(BayDoor.OpenDoor))]
static class ProbeBayDoorOpen{static void Prefix(BayDoor __instance){if(ReplayRuntime.IsPlayback){MissileLaunchProbe.playbackOpenDoor++;Debug.Log("GHOST_PROBE playback BayDoor.OpenDoor "+__instance.name+"\n"+Environment.StackTrace);}}}
[HarmonyPatch(typeof(Hardpoint),nameof(Hardpoint.SpringOpenBayDoors))]
static class ProbeSpringOpen{static void Prefix(){if(ReplayRuntime.IsPlayback){MissileLaunchProbe.playbackSpring++;Debug.Log("GHOST_PROBE playback SpringOpenBayDoors\n"+Environment.StackTrace);}}}
[HarmonyPatch(typeof(MountedMissile),nameof(MountedMissile.Fire))]
static class ProbeMountFire{static void Prefix(MountedMissile __instance){if(ReplayRuntime.IsPlayback){MissileLaunchProbe.playbackMountFire++;Debug.Log("GHOST_PROBE playback MountedMissile.Fire "+__instance.name+"\n"+Environment.StackTrace);}}}
