using System;
using System.Linq;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using UnityEngine;
using UnityEngine.Profiling;

public static class SceneReloadControlProbe
{
    static readonly List<WeakReference> oldLevels=new List<WeakReference>();
    static void Report(int n,Action<string> log)
    {
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        log("SCENE CONTROL reload="+n+" managed="+(GC.GetTotalMemory(false)/1048576)+"MiB monoUsed="+(Profiler.GetMonoUsedSizeLong()/1048576)+"MiB materials="+Resources.FindObjectsOfTypeAll<Material>().Length+" units="+Resources.FindObjectsOfTypeAll<Unit>().Length+" oldLevels="+oldLevels.Count(w=>w.IsAlive)+"/"+oldLevels.Count);
    }
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);
        await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Native reload control");
        if(Environment.GetEnvironmentVariable("REPLAY_CONTROL_PAUSED")=="1")
        {
            var holder=new GameObject("Isolated pause control");UnityEngine.Object.DontDestroyOnLoad(holder);
            holder.AddComponent<SceneControlPause>();
        }
        for(int i=0;i<4;i++)await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Native reload control");
        Report(0,log);
        int count=int.TryParse(Environment.GetEnvironmentVariable("REPLAY_MEMORY_ITERATIONS"),out int requested)?Math.Max(8,Math.Min(64,requested)):24;
        for(int i=0;i<count;i++)
        {
            oldLevels.Add(new WeakReference(NetworkSceneSingleton<LevelInfo>.i));
            await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Native reload control");
            if((i+1)%8==0)Report(i+1,log);
        }
    }
}

[DefaultExecutionOrder(32000)]
public sealed class SceneControlPause:MonoBehaviour
{
    void Update(){Time.timeScale=0;}
    void LateUpdate(){Time.timeScale=0;}
}
