using System;
using System.IO;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using UnityEngine;
using HarmonyLib;

public static class QuitRecordingProbe
{
    public static async UniTask Record(MapKey key,Action<string> log)
    {
        var marker=Environment.GetEnvironmentVariable("REPLAY_QUIT_MARKER");
        if(string.IsNullOrEmpty(marker))throw new Exception("Missing isolated quit marker");
        await ReplayManager.i.SwitchState(ModStates.Idle);
        await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Quit recording test");
        TimeScaleManager.Scale=1;
        await ReplayManager.i.SwitchState(ModStates.Record);
        var recorder=Recorder.Active;
        if(recorder==null||!recorder.IsRecording)throw new Exception("Quit test recording did not start");
        float until=Time.realtimeSinceStartup+6;
        while(Time.realtimeSinceStartup<until)await UniTask.NextFrame();
        File.WriteAllText(marker,recorder.RecordingPath);
        log("QUIT TEST active recording; requesting normal application quit");
    }
    public static async UniTask Verify(Action<string> log)
    {
        var marker=Environment.GetEnvironmentVariable("REPLAY_QUIT_MARKER");
        string partial=File.ReadAllText(marker);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file)||File.Exists(partial))throw new Exception("Quit did not publish its completed recording");
        string result=ReplayIntegrityCheck.Check(file);
        await ReplayManager.i.SwitchState(ModStates.Idle);
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));
        if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("Quit recording could not be reopened");
        log("QUIT TEST PASS slow completed write published, "+result+" and reopened");
    }
}

[HarmonyPatch(typeof(ReplayIntegrityCheck),nameof(ReplayIntegrityCheck.Check))]
public static class SlowQuitProbe
{
    [HarmonyPrefix]public static void Prefix(string path)
    {
        var setting=Environment.GetEnvironmentVariable("REPLAY_SLOW_QUIT");
        if(!string.IsNullOrEmpty(setting)&&path.EndsWith(".partial",StringComparison.OrdinalIgnoreCase))
            System.Threading.Thread.Sleep(int.TryParse(setting,out int ms)?ms:5000);
    }
}
