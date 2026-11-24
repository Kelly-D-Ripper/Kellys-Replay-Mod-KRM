using System;
using System.IO;
using Cysharp.Threading.Tasks;
using NuclearOption.MissionEditorScripts;
using NuclearOption.SavedMission;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using UnityEngine;

public static class EditorLiveProbe
{
    static async UniTask Until(Func<bool> condition,float seconds,string message)
    {float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new Exception(message);await UniTask.NextFrame();}}
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.StopReplayConnectionAsync();
        string oldRoot=EditorMissionGroup.MissionEditorDirectory;
        string sandbox=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ghost-probe","editor-sandbox-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);EditorMissionGroup.MissionEditorDirectory=sandbox;
        try
        {
            var config=NewMissionConfig.DefaultMission(key);config.Name="Replay editor probe "+Guid.NewGuid().ToString("N");
            log("EDITOR loading isolated native editor");await MissionEditor.LoadEditor(config);
            await Until(()=>GameManager.gameState==GameState.Editor&&SceneSingleton<MissionEditor>.i!=null,60,"Editor failed to open");
            log("EDITOR native play from editor");await MissionEditor.PlayFromEditor();
            await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Editor mission did not start");
            TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
            await Until(()=>Recorder.Active!=null&&Recorder.Active.IsRecording,15,"Editor recording did not start");
            string partial=Recorder.Active.RecordingPath;await Wait(3);
            log("EDITOR returning to editor while recording");await MissionEditor.ReturnToEditor();
            await Until(()=>GameManager.gameState==GameState.Editor&&SceneSingleton<MissionEditor>.i!=null,60,"Return to editor failed");
            await Until(()=>ReplayManager.i.GetState()==ModStates.Idle&&!ReplayRecordingRecovery.HasRecordingReservation,60,"Editor transition did not finish recording");
            string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
            if(!File.Exists(file))throw new Exception("Editor recording not finalized: "+ReplayManager.i.LastRecordingStopStatus);
            log("EDITOR "+ReplayIntegrityCheck.Check(file));
            MissionSaveLoad.SaveMissionVersion(MissionManager.CurrentMission,"ReplayProbeSave");
            var saved=Directory.GetFiles(sandbox,"*",SearchOption.AllDirectories);
            if(saved.Length==0)throw new Exception("Editor save did not reach isolated folder");
            log("EDITOR saved mission version inside isolated folder; exiting editor");
            await MissionEditor.ExitEditor();await Until(()=>GameManager.gameState==GameState.Menu,60,"Editor did not exit");
            await ReplayManager.i.StartPlaying(Path.GetFileName(file));
            if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("Editor recording failed to reopen");
            log("EDITOR PASS native editor Play → record → Return → save version → Exit → open recording");
        }
        finally{EditorMissionGroup.MissionEditorDirectory=oldRoot;}
    }
}
