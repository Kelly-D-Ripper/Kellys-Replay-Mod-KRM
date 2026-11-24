using System;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Diagnostics;
using ReplayMod.UI;
using UnityEngine;
using NuclearOption.SceneLoading;
using System.Linq;
using TMPro;
using UnityEngine.UI;

public static class ReleaseChecks
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    static void Native(string name)
    {var method=AccessTools.Method(typeof(GameplayUI),name);method.Invoke(method.IsStatic?null:UnityEngine.Object.FindObjectOfType<GameplayUI>(),null);}
    public static void Menu(Action<string> log)
    {
        ReplayDirector.i.SetClean(true);if(ReplayDirector.i.Clean)throw new Exception("Clean capture active at main menu");
        if(typeof(ReplayManager).Assembly.GetType("ReplayMod.Core.LiveFlightExperiment")!=null)throw new Exception("Ghost Flight remains compiled");
        CheckJoinWait(true);
        log("RELEASE menu guard and ghost removal PASS");
    }
    public static void CheckJoinWait(bool nativeAllowed)
    {
        var type=typeof(ReplayManager).Assembly.GetType("ReplayMod.Patches.ReplayJoinMenuWaitPatch");
        var prefix=type?.GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
        if(prefix==null||(bool)prefix.Invoke(null,new object[]{default(UniTask)})!=nativeAllowed)throw new Exception("Join-menu wait scope is incorrect");
    }
    public static async UniTask Playback(Player player,Action<string> log)
    {
        CheckJoinWait(false);
        await DelayScopeProbe.Run(true,log);
        foreach(double time in new[]{2d,60d,15d,80d,3d,40d,0d,5d})
        {
            await player.TimelineJump(time,0,false);
            if(player.IsSeeking||!player.IsPaused||Math.Abs(player.currentVirtualTime-time)>.001||!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception("Seek failed at "+time);
        }
        var camera=SceneSingleton<CameraStateManager>.i;
        ReplayDirector.i.SetPlaybackSpeed(.1f);player.Resume();double from=player.currentVirtualTime;await Wait(1);
        double advance=player.currentVirtualTime-from;
        if(player.IsSeeking||camera!=SceneSingleton<CameraStateManager>.i||advance<.02||advance>.3)throw new Exception("Slow motion clock/reload problem: "+advance);
        Native("PauseGame");double paused=player.currentVirtualTime;await Wait(.3f);
        if(!player.IsPaused||Math.Abs(player.currentVirtualTime-paused)>.001)throw new Exception("Native pause did not pause replay");
        Native("ResumeGame");await Wait(.2f);if(player.IsPaused)throw new Exception("Native resume did not resume replay");
        player.Pause();ReplayDirector.i.SetPlaybackSpeed(1);
        ReplayDirector.i.SetClean(true);await Wait(.1f);if(!ReplayDirector.i.Clean)throw new Exception("Clean capture not enabled in replay");
        if(!SceneSingleton<CameraStateManager>.i.mainCamera.enabled)throw new Exception("Clean capture disabled main camera");
        ReplayDirector.i.SetClean(false);
        var ui=TrailerSuiteUi.Instance;if(!ui.IsVisible)ui.Toggle();
        var open=AccessTools.Method(typeof(TrailerSuiteUi),"OpenMenu");var rebuild=AccessTools.Method(typeof(TrailerSuiteUi),"RebuildMenu");
        foreach(string menu in new[]{"Library","Record","Camera","Events","Scene","Lights","Help","Settings"})
        {open.Invoke(ui,new object[]{menu});await UniTask.NextFrame();}
        open.Invoke(ui,new object[]{"Camera"});
        for(int tab=0;tab<5;tab++){AccessTools.Field(typeof(TrailerSuiteUi),"cameraTab").SetValue(ui,tab);rebuild.Invoke(ui,null);await UniTask.NextFrame();}
        var director=ReplayDirector.i;var saved=director.Project.cameras.ToArray();director.Project.cameras.Clear();
        for(int i=0;i<26;i++)director.Project.cameras.Add(new CameraKey{time=i*.1});
        AccessTools.Field(typeof(TrailerSuiteUi),"cameraTab").SetValue(ui,1);
        AccessTools.Field(typeof(TrailerSuiteUi),"showPathOptions").SetValue(ui,true);AccessTools.Field(typeof(TrailerSuiteUi),"showPathTimes").SetValue(ui,true);
        AccessTools.Method(typeof(TrailerSuiteUi),"ChangeCameraPointPage").Invoke(ui,new object[]{false,1});await UniTask.NextFrame();
        if(!UnityEngine.Object.FindObjectsOfType<TMP_Text>().Any(t=>t.text.StartsWith("Point 26")))throw new Exception("Point 26 not reachable");
        ScreenCapture.CaptureScreenshot(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"release-path-page.png"));await Wait(.5f);
        director.RemovePathPoint(director.Project.cameras[25]);rebuild.Invoke(ui,null);await UniTask.NextFrame();
        if(!UnityEngine.Object.FindObjectsOfType<TMP_Text>().Any(t=>t.text.StartsWith("Point 1 ")))throw new Exception("Deleted last page did not clamp");
        director.Project.cameras.Clear();director.Project.cameras.AddRange(saved);
        director.EnterFreeCamera();camera=SceneSingleton<CameraStateManager>.i;
        var original=camera.transform.position;camera.transform.position=new Vector3(original.x,Datum.LocalSeaY+2,original.z);camera.transform.rotation=Quaternion.identity;
        director.SetRailDirection(RailDirection.Down);director.SetRailSpeed(100);director.Project.allowCameraThroughWorld=false;director.StartRail();await Wait(.2f);
        if(director.RailActive)throw new Exception("Rail did not stop at sea with collision on");
        camera.transform.position=new Vector3(original.x,Datum.LocalSeaY+2,original.z);
        director.Project.allowCameraThroughWorld=true;director.StartRail();await Wait(.2f);
        if(!director.RailActive||camera.transform.position.y>=Datum.LocalSeaY)throw new Exception("Rail collision bypass failed");
        director.StopRail();director.Project.allowCameraThroughWorld=false;camera.transform.position=original;
        string support=await ReplayDiagnostics.ExportAsync();if(!File.Exists(support)||new FileInfo(support).Length==0)throw new Exception("Support bundle missing");
        log("RELEASE repeated seeks, slow motion without reload, native pause/resume, capture guard, menu construction, point pagination, rail collision and support export PASS");
    }
    public static async UniTask RecordPaused(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);Menu(log);await DelayScopeProbe.Run(false,log);
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays");
        string first=Path.Combine(root,"bulk-probe-a-"+Guid.NewGuid().ToString("N")+".replay");
        string second=Path.Combine(root,"bulk-probe-b-"+Guid.NewGuid().ToString("N")+".replay");
        File.WriteAllText(first,"one");File.WriteAllText(first+".director.json","project");File.WriteAllText(second,"two");
        var ui=TrailerSuiteUi.Instance;if(!ui.IsVisible)ui.Toggle();AccessTools.Method(typeof(TrailerSuiteUi),"OpenMenu").Invoke(ui,new object[]{"Library"});
        Click("Select recordings to delete");Click(Path.GetFileNameWithoutExtension(first));Click(Path.GetFileNameWithoutExtension(second));Click("Delete selected recordings");Click("Confirm removal of 2");
        if(File.Exists(first)||File.Exists(second)||File.Exists(first+".director.json"))throw new Exception("Bulk delete did not move selected recordings");
        var trash=Path.Combine(root,"Deleted recordings");if(!Directory.GetFiles(trash,Path.GetFileName(first),SearchOption.AllDirectories).Any()||!Directory.GetFiles(trash,Path.GetFileName(first)+".director.json",SearchOption.AllDirectories).Any())throw new Exception("Bulk deletion did not preserve recoverable files");
        log("RELEASE bulk selection, confirmation and recoverable project sidecar PASS");
        string invalid=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays","release-invalid.replay");File.WriteAllBytes(invalid,new byte[]{1,2,3});
        try{await ReplayManager.i.StartPlaying(Path.GetFileName(invalid));}catch(InvalidDataException){}
        if(ReplayRuntime.IsPlayback)throw new Exception("Malformed replay accepted");
        await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Release recording check");
        float until=Time.realtimeSinceStartup+60;while(!RollingRecordingController.IsUsableGameplay()){if(Time.realtimeSinceStartup>until)throw new Exception("Live scene unavailable");await UniTask.NextFrame();}
        await ReplayManager.i.SwitchState(ModStates.Record);await Wait(1);
        if(Recorder.Active?.IsRecording!=true)throw new Exception("Recording did not start");
        AccessTools.Field(typeof(Recorder),"<CaptureFailureCount>k__BackingField").SetValue(Recorder.Active,1);
        Native("PauseGame");await Wait(.2f);float scale=TimeScaleManager.Scale;
        await ReplayManager.i.SwitchState(ModStates.Idle);await Wait(.2f);
        if(!GameplayUI.GameIsPaused||Math.Abs(TimeScaleManager.Scale-scale)>.001)throw new Exception("Recording stop resumed paused mission");
        if(ReplayManager.i.LastRecordingStopStatus.IndexOf("details may be missing",StringComparison.OrdinalIgnoreCase)<0)throw new Exception("Capture problem was hidden from the recording notice");
        log("RELEASE simulated capture issue displayed in recording notice PASS");
        Native("ResumeGame");
        // The synthetic empty host has no player-controlled resume clock.
        TimeScaleManager.Scale=1;
        RollingRecordingController.Instance.SetEnabled(true);
        float rollingDeadline=Time.realtimeSinceStartup+15;
        while(!RollingRecordingController.Instance.Capturing){if(Time.realtimeSinceStartup>rollingDeadline)throw new Exception("Recent recording did not start");await UniTask.NextFrame();}
        await Wait(1);
        string clip=Path.Combine(root,"rolling-probe-"+Guid.NewGuid().ToString("N")+".replay");
        await Recorder.Active.SaveRolling(clip);
        if(!ReplayIntegrityCheck.Check(clip).Contains("passed"))throw new Exception("Recent clip did not validate");
        RollingRecordingController.Instance.SetEnabled(false);
        await ReplayManager.i.SwitchState(ModStates.Idle);log("RELEASE recent clip verified and published PASS");
        var beforeFiles=Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays"),"*.replay");
        await ReplayManager.i.SwitchState(ModStates.Record);await Wait(1);
        var active=Recorder.Active;if(active==null||!active.IsRecording)throw new Exception("Session-end recording failed to start");
        AccessTools.Field(typeof(Recorder),"_recordingMission").SetValue(active,new object());
        float stopDeadline=Time.realtimeSinceStartup+20;
        while(ReplayManager.i.GetState()!=ModStates.Idle||ReplayRecordingRecovery.HasRecordingReservation){if(Time.realtimeSinceStartup>stopDeadline)throw new Exception("Session end did not finalize recording");await UniTask.NextFrame();}
        var created=Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays"),"*.replay").Except(beforeFiles).Single();
        string checkedFile=ReplayIntegrityCheck.Check(created);if(!checkedFile.Contains("passed"))throw new Exception("Finalized recording integrity failed");
        await ReplayManager.i.StartPlaying(Path.GetFileName(created));
        if(!ReplayRuntime.IsPlayback)throw new Exception("Finalized recording did not open");
        await ReplayManager.i.SwitchState(ModStates.Idle);
        log("RELEASE session identity loss automatically finalizes, integrity checks and opens recording PASS");
        await ReplayManager.i.StartPlaying("test.replay");
        if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("Valid replay failed after malformed input/record stop");
        await ReplayManager.i.SwitchState(ModStates.Idle);
        log("RELEASE malformed rejection, paused live recording stop and valid replay reopen PASS");
    }
    private static void Click(string text)
    {
        var button=UnityEngine.Object.FindObjectsOfType<Button>().FirstOrDefault(b=>b.interactable&&b.GetComponentInChildren<TMP_Text>()?.text.Contains(text)==true);
        if(button==null)throw new Exception("Missing UI button: "+text);button.onClick.Invoke();
    }
    public static async UniTask DayNight(MapKey key,Action<string> log)
    {
        var baseline=ReplayWorldPlayback.CaptureEnvironment(NetworkSceneSingleton<LevelInfo>.i,0);
        string name="release-day-"+Guid.NewGuid().ToString("N")+".replay";
        using(var writer=new BinaryWriter(File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",name))))
        {
            writer.Write(12549);writer.Write((byte)5);writer.Write((byte)0);writer.Write((byte)0);writer.Write((byte)key.Type);writer.Write(key.Path);
            writer.Write(DateTime.UtcNow.Ticks);writer.Write(0);writer.Write(600d);writer.Write(301L);writer.Write((ushort)0);
            for(int i=0;i<301;i++)
            {
                var state=new ReplayMod.Events.ConcreteEvents.WorldEnvironmentStateEvent();baseline.CopyTo(state);state.Time=i*2;
                state.timeOfDay=6+(float)(state.Time/50);state.timeFactor=72;writer.Write((byte)state.EventType);state.Write(writer);
            }
        }
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(name);
        var player=ReplayManager.i.Playback;player.Pause();await player.TimelineJump(300,0,false);
        var level=NetworkSceneSingleton<LevelInfo>.i;if(Math.Abs(level.timeOfDay-12)>.1)throw new Exception("Midday mismatch");
        ReplayDirector.i.SetPlaybackSpeed(480);player.Resume();float end=Time.realtimeSinceStartup+20;
        while(!player.IsPaused){if(Time.realtimeSinceStartup>end)throw new Exception("Timelapse timed out");await UniTask.NextFrame();}
        if(Math.Abs(player.currentVirtualTime-600)>.01||Math.Abs(level.timeOfDay-18)>.1||!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception("Timelapse endpoint mismatch");
        ReplayDirector.i.SetPlaybackSpeed(1);log("RELEASE 480x synthetic day cycle seek noon to evening endpoint PASS");
    }
}
