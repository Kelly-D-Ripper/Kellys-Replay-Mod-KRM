using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NuclearOption.MissionEditorScripts;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Patches;
using UnityEngine;

public static class NukeEditorProbe
{
    static async UniTask Until(Func<bool> ready,float seconds,string failure){float deadline=Time.realtimeSinceStartup+seconds;while(!ready()){if(Time.realtimeSinceStartup>deadline)throw new Exception(failure);await UniTask.NextFrame();}}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        RollingRecordingController.Instance.SetEnabled(false);await ReplayManager.i.SwitchState(ModStates.Idle);ReplayManager.i.NextRecordingIsRolling=false;await ReplayMod.Misc.SceneHelper.StopReplayConnectionAsync();
        string oldRoot=EditorMissionGroup.MissionEditorDirectory;string sandbox=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ghost-probe","nuke-editor-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(sandbox);EditorMissionGroup.MissionEditorDirectory=sandbox;
        int detonation=0;Action<Missile,GlobalPosition,Vector3,bool,bool,bool> observe=(missile,position,normal,armed,armor,terrain)=>{if(missile!=null&&missile.definition?.jsonKey=="nuclearBomb1_strategic"){detonation++;log("NUKE EDITOR native detonation armed="+armed+" terrain="+terrain);}};
        try
        {
            var config=NewMissionConfig.DefaultMission(key);config.Name="Replay nuke probe "+Guid.NewGuid().ToString("N");
            await MissionEditor.LoadEditor(config);await Until(()=>GameManager.gameState==GameState.Editor&&SceneSingleton<MissionEditor>.i!=null,60,"Editor failed to load");
            var editor=SceneSingleton<MissionEditor>.i;var spawner=NetworkSceneSingleton<Spawner>.i;var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();if(hq==null)throw new Exception("No faction for nuclear mission");
            var top=new GlobalPosition(1000,10000,1000).ToLocalPosition();var hits=Physics.RaycastAll(top,Vector3.down,20000).Where(h=>h.collider!=null).OrderBy(h=>h.distance).ToArray();if(hits.Length==0)throw new Exception("No terrain raycast hit for nuclear mission");
            var site=(hits[0].point+Vector3.up*3).ToGlobalPosition();log("NUKE EDITOR raycast site="+site+" collider="+hits[0].collider.name);
            var nuke=spawner.SpawnFromUnitDefinitionInEditor(Encyclopedia.Lookup["nuclearBomb1_strategic"],site,Quaternion.LookRotation(Vector3.down,Vector3.forward),hq,"instant-nuke");
            if(nuke==null||!(editor.RegisterNewUnit(nuke,"instant-nuke") is SavedMissile savedNuke))throw new Exception("Could not place native 250kt bomb");savedNuke.startingSpeed=200;
            var aircraft=spawner.SpawnFromUnitDefinitionInEditor(Encyclopedia.Lookup["CAS1"],new GlobalPosition(16000,3000,16000),Quaternion.identity,hq,"player-aircraft");
            if(aircraft==null||!(editor.RegisterNewUnit(aircraft,"player-aircraft") is SavedAircraft savedPlayer))throw new Exception("Could not place player aircraft");savedPlayer.playerControlled=true;savedPlayer.startingSpeed=150;
            MissionSaveLoad.SaveMissionVersion(MissionManager.CurrentMission,"NukeFixture");var savedKey=MissionManager.CurrentMission.LoadKey.Value;
            log("NUKE EDITOR saved mission with airborne player and 250kt bomb near terrain");
            await MissionEditor.ExitEditor();await Until(()=>GameManager.gameState==GameState.Menu,60,"Editor did not exit");
            if(!savedKey.TryLoad(out var loaded,out var error))throw new Exception("Saved nuke mission could not load: "+error);
            MissionManager.SetMission(loaded,false);ReplayMissileDetonationPatch.OnDetonation+=observe;
            await NetworkManagerNuclearOption.i.StartHostAsync(new HostOptions(SocketType.Offline,GameState.SinglePlayer,loaded.MapKey));
            await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Saved nuke mission did not start");
            await Until(()=>detonation>0,20,"Placed nuke did not detonate in the native mission");ReplayMissileDetonationPatch.OnDetonation-=observe;
            TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording||recorder.IsRolling||string.IsNullOrEmpty(recorder.RecordingPath))throw new Exception("Nuclear full recording unavailable or rolling still active");string partial=recorder.RecordingPath;
            await UniTask.Delay(TimeSpan.FromSeconds(20));int faults=recorder.CaptureFailureCount;await ReplayManager.i.SwitchState(ModStates.Idle);
            string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
            if(!File.Exists(file)||!ReplayIntegrityCheck.Check(file).Contains("passed"))throw new Exception("Nuclear mission recording invalid");
            await ReplayMod.Misc.SceneHelper.StopReplayConnectionAsync();await Until(()=>GameManager.gameState==GameState.Menu,60,"Nuclear mission did not return to main menu");
            await ReplayManager.i.StartPlaying(Path.GetFileName(file));var replay=ReplayManager.i.Playback;
            if(!ReplayRuntime.IsPlayback||replay==null||replay.replayDuration<15)throw new Exception("Saved nuclear mission replay did not open");
            replay.Pause();foreach(double at in new[]{2d,15d,5d,0d}){await replay.TimelineJump(at,0,false);if(replay.IsSeeking||!string.IsNullOrEmpty(replay.PlaybackFailure)||Math.Abs(replay.currentVirtualTime-at)>.001)throw new Exception("Nuclear mission seek failed at "+at);}
            if(faults!=0)throw new Exception("Nuclear recording had "+faults+" capture warnings");
            log("NUKE EDITOR PASS saved mission, native 250kt detonation, 20-second recording, main-menu reopen and four seeks");
        }
        finally{ReplayMissileDetonationPatch.OnDetonation-=observe;EditorMissionGroup.MissionEditorDirectory=oldRoot;}
    }
}
