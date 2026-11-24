using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.UI;
using UnityEngine;

public static class CleanUiFrameProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    static async UniTask Capture(string name,Action<string> log)
    {
        string tag=Environment.GetEnvironmentVariable("REPLAY_CLEAN_TAG")??"v18";
        if(tag.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'&&c!='_'))throw new Exception("Invalid capture tag");
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ghost-probe","clean-"+tag+"-"+name+".png");
        if(File.Exists(path))throw new Exception("Capture already exists: "+path);
        ScreenCapture.CaptureScreenshot(path);await Wait(.6f);if(!File.Exists(path)||new FileInfo(path).Length<100)throw new Exception("Capture missing: "+name);log("CLEAN FRAME "+name+" "+path);
    }
    public static async UniTask MainMenu(Action<string> log)
    {
        await Wait(.5f);await Capture("menu-before",log);ReplayDirector.i.SetClean(true);if(ReplayDirector.i.Clean)throw new Exception("Clean capture enabled in main menu");await Capture("menu-after",log);
        log("CLEAN FRAME main-menu guard kept clean mode off");
    }
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        var player=ReplayManager.i.Playback;player.Pause();await player.TimelineJump(5,0,false);await Wait(.5f);
        var studio=TrailerSuiteUi.Instance;if(!studio.IsVisible)studio.Toggle();await Wait(.2f);await Capture("replay-before",log);
        ReplayDirector.i.SetClean(true);await Wait(.4f);if(!ReplayDirector.i.Clean||studio.IsVisible)throw new Exception("Replay UI did not hide");await Capture("replay-hidden",log);
        ReplayDirector.i.SetClean(false);await Wait(.4f);if(ReplayDirector.i.Clean||!studio.IsVisible)throw new Exception("Replay UI did not restore");await Capture("replay-restored",log);
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Clean UI frame test");TimeScaleManager.Scale=1;await Wait(1);
        if(!RollingRecordingController.IsUsableGameplay())throw new Exception("Live gameplay unavailable");
        if(!studio.IsVisible)studio.Toggle();await Wait(.2f);await Capture("live-before",log);
        ReplayDirector.i.SetClean(true);await Wait(.4f);if(!ReplayDirector.i.Clean||studio.IsVisible)throw new Exception("Live UI did not hide");await Capture("live-hidden",log);
        ReplayDirector.i.SetClean(false);await Wait(.3f);if(ReplayDirector.i.Clean||!studio.IsVisible)throw new Exception("Live UI did not restore");await Capture("live-restored",log);
        var pause=AccessTools.Method(typeof(GameplayUI),"PauseGame");pause.Invoke(pause.IsStatic?null:UnityEngine.Object.FindObjectOfType<GameplayUI>(),null);await Wait(.3f);if(!GameplayUI.GameIsPaused)throw new Exception("Pause menu unavailable");await Capture("paused-before",log);
        ReplayDirector.i.SetClean(true);await Wait(.4f);if(!ReplayDirector.i.Clean)throw new Exception("Paused clean mode unavailable");await Capture("paused-hidden",log);
        ReplayDirector.i.SetClean(false);await Wait(.3f);if(!GameplayUI.GameIsPaused)throw new Exception("Restoring UI resumed mission");await Capture("paused-restored",log);
        log("CLEAN FRAME PASS captured menu, replay, live and paused hide/restore states for independent image inspection");
    }
}
