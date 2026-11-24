using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using UnityEngine;

// Map-placed buildings keep Unit.startRotation at identity (only Spawner sets it).
// Record live buildings and compare their replayed world rotation.
public static class BuildingRotationProbe
{
    static async UniTask Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)await UniTask.NextFrame();}
    static async UniTask Until(Func<bool> ready,float timeout,string reason){float end=Time.realtimeSinceStartup+timeout;while(!ready()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(reason);await UniTask.NextFrame();}}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Building rotation test");
        await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Live scene unavailable");TimeScaleManager.Scale=1;
        await ReplayManager.i.SwitchState(ModStates.Record);
        await Until(()=>Recorder.Active!=null&&Recorder.Active.IsRecording,15,"Recorder unavailable");
        string partial=Recorder.Active.RecordingPath;
        // A spawned building whose startRotation is correct, as a control.
        var definition=Encyclopedia.Lookup.Values.OfType<BuildingDefinition>().FirstOrDefault(d=>d.unitPrefab!=null&&d.unitPrefab.GetComponent<Building>()!=null);
        Building spawned=null;
        if(definition!=null)spawned=NetworkSceneSingleton<Spawner>.i.SpawnBuilding(definition.unitPrefab,new GlobalPosition(1500,0,1500),Quaternion.Euler(0,37,0),null,null,null,false,null);
        await Wait(3);
        var live=UnityEngine.Object.FindObjectsOfType<Building>().Where(b=>b!=null&&b.persistentID.Id!=0)
            .ToDictionary(b=>b.persistentID.Id,b=>(rotation:b.transform.rotation,start:b.startRotation,name:b.definition?.jsonKey??b.name));
        var mapPlaced=live.Where(p=>Quaternion.Angle(p.Value.rotation,p.Value.start)>1).Select(p=>p.Key).ToArray();
        log("BUILDING ROTATION live buildings="+live.Count+" with startRotation different from world rotation="+mapPlaced.Length+(spawned!=null?" control="+spawned.definition.jsonKey:""));
        await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file))throw new Exception("Final recording missing: "+ReplayManager.i.LastRecordingStopStatus);
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));
        var player=ReplayManager.i.Playback;player.Pause();
        int wrong=0,checkedCount=0,missing=0;float worst=0;var examples=new List<string>();
        for(int pass=0;pass<2;pass++)
        {
            await player.TimelineJump(pass==0?1:2.5,0,false);await UniTask.NextFrame();await UniTask.NextFrame();
            foreach(var pair in live)
            {
                var actor=player.FindActor(pair.Key);if(actor==null){if(pass==0)missing++;continue;}
                float angle=Quaternion.Angle(actor.transform.rotation,pair.Value.rotation);checkedCount++;worst=Math.Max(worst,angle);
                if(angle>1){wrong++;if(examples.Count<6)examples.Add(pair.Value.name+" #"+pair.Key+" off by "+angle.ToString("F1")+"deg");}
            }
        }
        log("BUILDING ROTATION checked="+checkedCount+" missingInReplay="+missing+" wrong="+wrong+" worst="+worst.ToString("F1")+"deg "+string.Join("; ",examples));
        log(wrong==0?"BUILDING ROTATION PASS replayed buildings keep their live world rotation":"BUILDING ROTATION FAIL");
    }
}
