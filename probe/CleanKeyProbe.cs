using System;
using System.Reflection;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;

[HarmonyPatch(typeof(KeyboardShortcutExtensions),"GetDown")]
public static class CleanKeyInjection
{
    public static bool handled;public static int targetFrame=-1;public static KeyCode injected;
    [HarmonyPrefix]
    public static bool Prefix(KeyboardShortcut key,ref bool __result)
    {
        if(Time.frameCount==targetFrame&&key.MainKey==injected){handled=true;__result=true;return false;}
        return true;
    }
}
public static class CleanKeyProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    static async UniTask Press(KeyCode key){CleanKeyInjection.injected=key;CleanKeyInjection.handled=false;CleanKeyInjection.targetFrame=Time.frameCount+1;await Wait(.2f);CleanKeyInjection.targetFrame=-1;if(!CleanKeyInjection.handled)throw new Exception("Simulated shortcut was not polled");}
    public static async UniTask Menu(Action<string> log)
    {
        var current=ConfigManager.ToggleCleanCapture.Value.MainKey;await Press(current);if(ReplayDirector.i.Clean)throw new Exception("Shortcut hid main menu");log("CLEAN KEY main-menu simulated shortcut left clean mode off");
    }
    public static async UniTask Replay(Action<string> log)
    {
        var original=ConfigManager.ToggleCleanCapture.Value;
        try
        {
            await Wait(.5f);string reason;if(!ReplayDirector.i.AssignCleanCaptureHotkey(KeyCode.F10,out reason))throw new Exception("Could not assign test key: "+reason);
            await Press(KeyCode.F10);if(!ReplayDirector.i.Clean)throw new Exception("Assigned key did not hide UI; IsBusy="+ReplayDirector.i.IsBusy+" mission="+MissionManager.IsRunning);
            await Press(KeyCode.F10);if(ReplayDirector.i.Clean)throw new Exception("Assigned key did not restore UI");
            log("CLEAN KEY PASS simulated F10 assignment hid and restored replay UI; original binding restored");
        }
        finally{ReplayDirector.i.SetClean(false);ConfigManager.ToggleCleanCapture.Value=original;CleanKeyInjection.targetFrame=-1;}
    }
}
