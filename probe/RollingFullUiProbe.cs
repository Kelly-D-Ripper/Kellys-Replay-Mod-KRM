using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class RollingFullUiProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    static async UniTask Until(Func<bool> condition,float seconds,string text){float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new Exception(text);await UniTask.NextFrame();}}
    static void Click(string text){var button=UnityEngine.Object.FindObjectsOfType<Button>().FirstOrDefault(b=>b.interactable&&b.GetComponentInChildren<TMP_Text>()?.text.Contains(text)==true);if(button==null)throw new Exception("Button not found: "+text);button.onClick.Invoke();}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        RollingRecordingController.Instance.SetEnabled(false);await ReplayManager.i.SwitchState(ModStates.Idle);
        await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Rolling to full UI test");await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Live mission unavailable");TimeScaleManager.Scale=1;
        var rolling=RollingRecordingController.Instance;rolling.SetEnabled(true);await Until(()=>rolling.Capturing,20,"Rolling recording did not start");await Wait(1);
        var ui=TrailerSuiteUi.Instance;if(!ui.IsVisible)ui.Toggle();AccessTools.Method(typeof(TrailerSuiteUi),"OpenMenu").Invoke(ui,new object[]{"Record"});await Wait(.2f);
        Click("Start mission recording");await Until(()=>Recorder.Active!=null&&Recorder.Active.IsRecording&&!Recorder.Active.IsRolling&&!string.IsNullOrEmpty(Recorder.Active.RecordingPath),30,"UI did not switch to full recording");
        if(rolling.Enabled)throw new Exception("Rolling recording remained enabled after full-recording request");string partial=Recorder.Active.RecordingPath;
        await Wait(2);
        var labels=UnityEngine.Object.FindObjectsOfType<Button>().Where(b=>b.interactable).Select(b=>b.GetComponentInChildren<TMP_Text>()?.text).Where(t=>!string.IsNullOrEmpty(t)).ToArray();log("RECORD UI visible buttons="+string.Join(" | ",labels));Click("Stop & save mission");
        await Until(()=>ReplayManager.i.GetState()==ModStates.Idle&&!ReplayRecordingRecovery.HasRecordingReservation,30,"UI did not finish full recording");
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file)||File.Exists(partial))throw new Exception("Full recording was not published");string check=ReplayIntegrityCheck.Check(file);
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||player==null)throw new Exception("Full recording failed to open");
        player.Pause();await player.TimelineJump(Math.Min(1,player.replayDuration),0,false);
        log("ROLLING TO FULL UI PASS enabled rolling, used native studio buttons to start and stop full recording, reopened and sought it; "+check);
    }
}
