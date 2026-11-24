using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NuclearOption.MissionEditorScripts;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class NeutralEditorProbe
{
    static async UniTask Until(Func<bool> ready,float seconds,string failure){float deadline=Time.realtimeSinceStartup+seconds;while(!ready()){if(Time.realtimeSinceStartup>deadline)throw new Exception(failure);await UniTask.NextFrame();}}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        RollingRecordingController.Instance.SetEnabled(false);await ReplayManager.i.SwitchState(ModStates.Idle);ReplayManager.i.NextRecordingIsRolling=false;await ReplayMod.Misc.SceneHelper.StopReplayConnectionAsync();
        string oldRoot=EditorMissionGroup.MissionEditorDirectory;
        string sandbox=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ghost-probe","neutral-editor-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);EditorMissionGroup.MissionEditorDirectory=sandbox;
        string stage="editor setup";
        try
        {
            var config=NewMissionConfig.DefaultMission(key);config.Name="Replay neutral probe "+Guid.NewGuid().ToString("N");
            await MissionEditor.LoadEditor(config);await Until(()=>GameManager.gameState==GameState.Editor&&SceneSingleton<MissionEditor>.i!=null,60,"Editor failed to load");
            var editor=SceneSingleton<MissionEditor>.i;var spawner=NetworkSceneSingleton<Spawner>.i;
            var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();if(hq==null)throw new Exception("No player faction in editor fixture");
            Unit Add(string unitKey,GlobalPosition position,FactionHQ faction,string name)
            {
                if(!Encyclopedia.Lookup.TryGetValue(unitKey,out var definition))throw new Exception("Missing native unit "+unitKey);
                var unit=spawner.SpawnFromUnitDefinitionInEditor(definition,position,Quaternion.identity,faction,name);
                if(unit==null)throw new Exception("Editor did not spawn "+unitKey);
                var saved=editor.RegisterNewUnit(unit,name);
                if(faction==null&&!string.IsNullOrEmpty(saved.faction))throw new Exception("Neutral unit unexpectedly got faction "+saved.faction);
                return unit;
            }
            Add("MunitionsPallet1",new GlobalPosition(1000,100,1000),null,"neutral-pallet");
            Add("6x6_1_APC",new GlobalPosition(1200,100,1000),null,"neutral-apc");
            Add("CAS1",new GlobalPosition(1500,3000,1000),null,"neutral-aircraft");
            Add("Frigate1",new GlobalPosition(20000,0,20000),null,"neutral-ship");
            var building=Encyclopedia.Lookup.Values.OfType<BuildingDefinition>().FirstOrDefault(d=>d.unitPrefab!=null&&d.unitPrefab.GetComponent<Building>()!=null&&d.unitPrefab.GetComponent<Airbase>()==null);
            if(building==null)throw new Exception("No native building definition available");
            Add(building.jsonKey,new GlobalPosition(1400,100,1000),null,"neutral-building");
            var neutralKeys=new[]{"MunitionsPallet1","6x6_1_APC","CAS1","Frigate1",building.jsonKey};log("NEUTRAL EDITOR building="+building.jsonKey);
            var player=Add("CAS1",new GlobalPosition(1800,3000,1000),hq,"player-aircraft") as Aircraft;
            if(!(player?.SavedUnit is SavedAircraft savedPlayer))throw new Exception("Player saved aircraft unavailable");savedPlayer.playerControlled=true;
            MissionSaveLoad.SaveMissionVersion(MissionManager.CurrentMission,"NeutralFixture");
            if(!MissionManager.CurrentMission.LoadKey.HasValue)throw new Exception("Saved mission key unavailable");
            var savedKey=MissionManager.CurrentMission.LoadKey.Value;log("NEUTRAL EDITOR saved mission with neutral pallet, vehicle, aircraft, ship and building");
            stage="editor exit";await MissionEditor.ExitEditor();await Until(()=>GameManager.gameState==GameState.Menu,60,"Editor did not exit");
            stage="saved mission load";if(!savedKey.TryLoad(out var loaded,out var loadError))throw new Exception("Saved mission could not load: "+loadError);
            MissionManager.SetMission(loaded,false);
            stage="starting offline host";await NetworkManagerNuclearOption.i.StartHostAsync(new HostOptions(SocketType.Offline,GameState.SinglePlayer,loaded.MapKey));
            await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Saved neutral mission did not start normally");
            await UniTask.Delay(TimeSpan.FromSeconds(2));
            stage="collecting neutral units";var neutral=UnityEngine.Object.FindObjectsOfType<Unit>().Where(u=>u!=null&&u.NetworkHQ==null&&u.definition!=null&&neutralKeys.Contains(u.definition.jsonKey)).ToArray();
            if(!neutralKeys.All(k=>neutral.Any(u=>u.definition.jsonKey==k)))throw new Exception("Native saved mission did not spawn every neutral fixture: "+string.Join(",",neutral.Select(u=>u.definition.jsonKey)));
            var ids=neutral.ToDictionary(u=>u.persistentID.Id,u=>u.definition.jsonKey);
            stage="starting recording";TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording||recorder.IsRolling||string.IsNullOrEmpty(recorder.RecordingPath))throw new Exception("Neutral full recording unavailable or rolling still active");string partial=recorder.RecordingPath;
            stage="recording and finalization";await UniTask.Delay(TimeSpan.FromSeconds(3));await ReplayManager.i.SwitchState(ModStates.Idle);
            string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
            if(!File.Exists(file)||!ReplayIntegrityCheck.Check(file).Contains("passed"))throw new Exception("Neutral mission recording invalid");
            stage="reading recording";var recorded=new HashSet<uint>();using(var input=new BinaryReader(File.OpenRead(file)))
            {var header=ReplayRecordingHeader.Read(input);for(long n=0;n<header.EventCount;n++){var e=ReplayRecordTransport.ReadEvent(input,header.Format);if(e is SpawnEvent spawn&&ids.ContainsKey(spawn.unitId)&&spawn.factionName==ReplaySpawnPolicy.NoFaction)recorded.Add(spawn.unitId);ReplayEventFactory.Return(e);}}
            if(ids.Keys.Any(id=>!recorded.Contains(id)))throw new Exception("Recorded neutral spawns missing");
            stage="returning to menu";await ReplayMod.Misc.SceneHelper.StopReplayConnectionAsync();await Until(()=>GameManager.gameState==GameState.Menu,60,"Saved mission did not return to main menu");
            stage="opening replay";await ReplayManager.i.StartPlaying(Path.GetFileName(file));var replay=ReplayManager.i.Playback;
            if(!ReplayRuntime.IsPlayback||replay==null)throw new Exception("Saved neutral mission replay did not open");
            stage="seeking";replay.Pause();foreach(double at in new[]{1d,2d,0d}){await replay.TimelineJump(at,0,false);foreach(var pair in ids)if(replay.FindActor(pair.Key)==null)throw new Exception("Neutral "+pair.Value+" missing after seek to "+at);}
            log("NEUTRAL EDITOR PASS saved mission opened normally; neutral pallet, ground vehicle, aircraft, ship and building recorded, reopened and repeatedly sought");
        }
        catch(Exception error){throw new Exception("Neutral fixture failed during "+stage,error);}
        finally{EditorMissionGroup.MissionEditorDirectory=oldRoot;}
    }
}
