using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class BuildingLiveProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Building collapse recording test");TimeScaleManager.Scale=1;
        await ReplayManager.i.SwitchState(ModStates.Record);var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("Building recorder unavailable");string partial=recorder.RecordingPath;
        var all=Encyclopedia.Lookup.Values.OfType<BuildingDefinition>().Where(d=>d.unitPrefab!=null&&d.unitPrefab.GetComponent<Building>()!=null).Select(d=>new{d.jsonKey,d.unitName,building=d.unitPrefab.GetComponent<Building>()}).ToArray();
        log("BUILDING INVENTORY "+string.Join("; ",all.Take(25).Select(x=>x.jsonKey+"="+x.building.collapseTime.ToString("F2")+" airbase="+(x.building.GetComponent<Airbase>()!=null))));
        var definition=Encyclopedia.Lookup.Values.OfType<BuildingDefinition>().Where(d=>d.unitPrefab!=null&&d.unitPrefab.GetComponent<Building>()!=null&&d.unitPrefab.GetComponent<Airbase>()==null)
            .Select(d=>new{definition=d,prefab=d.unitPrefab.GetComponent<Building>()}).Where(x=>x.prefab.collapseTime>.5f&&x.prefab.collapseTime<=15).OrderByDescending(x=>x.prefab.collapseTime).FirstOrDefault();
        if(definition==null)throw new Exception("No native collapsing building available");
        bool mapBuilding=Environment.GetEnvironmentVariable("REPLAY_MAP_BUILDING_LIVE")=="1";
        var existing=UnityEngine.Object.FindObjectsOfType<Building>().Where(b=>b!=null&&b.definition!=null&&b.persistentID.Id!=0&&!b.disabled&&b.definition.unitPrefab!=null&&b.definition.unitPrefab.GetComponent<Building>()!=null&&b.definition.unitPrefab.GetComponent<Building>().collapseTime>.5f&&b.definition.unitPrefab.GetComponent<Building>().collapseTime<=15).ToArray();
        log("BUILDING LIVE existing eligible="+existing.Length);
        var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();var building=mapBuilding?existing.FirstOrDefault():NetworkSceneSingleton<Spawner>.i.SpawnBuilding(definition.definition.unitPrefab,new GlobalPosition(1000,100,1000),Quaternion.identity,hq,null,null,false,null);
        if(building==null)throw new Exception(mapBuilding?"No existing map building available":"Building did not spawn");uint id=building.persistentID.Id;await Wait(2);
        float nativeDuration=building.definition.unitPrefab.GetComponent<Building>().collapseTime;string actualKey=building.definition.jsonKey;log("BUILDING LIVE key="+actualKey+" map="+mapBuilding+" duration="+nativeDuration.ToString("F2"));
        building.DisableUnit();await Wait(nativeDuration+1.5f);
        bool nativeGone=building==null;int faults=recorder.CaptureFailureCount;await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file)||!ReplayIntegrityCheck.Check(file).Contains("passed"))throw new Exception("Building recording invalid");
        BuildingStateEvent disabled=null;double intact=-1,despawn=-1;
        using(var input=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(input);for(long n=0;n<header.EventCount;n++)
            {var e=ReplayRecordTransport.ReadEvent(input,header.Format);if(e is DespawnEvent gone&&gone.unitId==id)despawn=gone.Time;if(e is BuildingStateEvent state&&state.unitId==id){if(!state.disabled)intact=state.Time;else if(disabled==null)disabled=new BuildingStateEvent{Time=state.Time,unitId=state.unitId,disabled=state.disabled,duration=state.duration,collapseStartTime=state.collapseStartTime,anchor=state.anchor,rotation=state.rotation};}ReplayEventFactory.Return(e);}
        }
        if(faults!=0||intact<0||disabled==null)throw new Exception("Building intact/destroyed states missing or capture faults="+faults);
        log("BUILDING LIVE lifecycle nativeGone="+nativeGone+" intact="+intact.ToString("F2")+" disable="+disabled.Time.ToString("F2")+" collapseStart="+disabled.collapseStartTime.ToString("F2")+" despawn="+despawn.ToString("F2"));
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var replay=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||replay==null)throw new Exception("Building replay did not open");replay.Pause();
        async UniTask<Building> At(double time){await replay.TimelineJump(time,0,false);await UniTask.NextFrame();var actor=replay.FindActor(id) as Building;if(actor==null)throw new Exception("Building missing at "+time);return actor;}
        var before=await At(intact);if(before.disabled)throw new Exception("Building already disabled before destruction");int visible=before.GetComponentsInChildren<MeshRenderer>(true).Count(r=>r.enabled);if(visible==0)throw new Exception("Intact building has no visible mesh");
        double middle=Math.Max(disabled.Time,disabled.collapseStartTime+disabled.duration*.5);var during=await At(middle);float drop=disabled.anchor.ToLocalPosition().y-during.transform.position.y;
        if(!during.disabled||drop<.1f)throw new Exception("Building did not visibly collapse: disabled="+during.disabled+" drop="+drop);
        double end=disabled.collapseStartTime+disabled.duration+.05;
        if(despawn>=0&&despawn<=end){await replay.TimelineJump(end,0,false);await UniTask.NextFrame();if(replay.FindActor(id)!=null)throw new Exception("Building remained after recorded despawn");}
        else{var after=await At(end);if(after.GetComponentsInChildren<MeshRenderer>(true).Any(r=>r.enabled))throw new Exception("Destroyed building remained visible after collapse");}
        before=await At(intact);if(before.disabled||!before.GetComponentsInChildren<MeshRenderer>(true).Any(r=>r.enabled))throw new Exception("Building did not restore after rewind");
        log("BUILDING LIVE PASS "+actualKey+" map="+mapBuilding+" native disable recorded; intact, collapse "+drop.ToString("F2")+"m, hidden and rewind restored");
    }
}
