using System;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using System.Linq;
using System.Reflection;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;
using UnityEngine.Profiling;

public static class SeekMemoryProbe
{
    static readonly System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<WeakReference>> old=new System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<WeakReference>>();
    static void Observe(string label,object value){if(value==null)return;if(!old.TryGetValue(label,out var list))old[label]=list=new System.Collections.Generic.List<WeakReference>();list.Add(new WeakReference(value));}
    static void Report(Player player,int n,Action<string> log)
    {
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        var materials=Resources.FindObjectsOfTypeAll<Material>();
        using(var p=Process.GetCurrentProcess())
        log("MEMORY seek="+n+" managed="+(GC.GetTotalMemory(false)/1048576)+"MiB monoUsed="+(Profiler.GetMonoUsedSizeLong()/1048576)+"MiB monoHeap="+(Profiler.GetMonoHeapSizeLong()/1048576)+"MiB private="+(p.PrivateMemorySize64/1048576)+"MiB units="+Resources.FindObjectsOfTypeAll<Unit>().Length+" gameObjects="+Resources.FindObjectsOfTypeAll<GameObject>().Length+" materials="+materials.Length+" textures="+Resources.FindObjectsOfTypeAll<Texture>().Length+" checkpointBytes="+player.SeekCheckpointBytes+" pools="+ReplayEventPool<SpawnEvent>.RetainedCount+","+ReplayEventPool<UpdatePositionEvent>.RetainedCount+","+ReplayEventPool<AircraftVisualStateEvent>.RetainedCount);
        log("MEMORY scene singletons total="+GameManager.SceneSingletons.Count+" destroyed="+GameManager.SceneSingletons.Count(s=>s is UnityEngine.Object obj&&obj==null));
        log("MEMORY material groups "+n+": "+string.Join("; ",materials.Where(m=>m!=null).GroupBy(m=>m.name).OrderByDescending(g=>g.Count()).Take(12).Select(g=>g.Key+"="+g.Count())));
        log("MEMORY old objects "+n+": "+string.Join("; ",old.Select(p=>p.Key+"="+p.Value.Count(w=>w.IsAlive)+"/"+p.Value.Count+" native="+p.Value.Count(w=>w.Target is UnityEngine.Object u&&u!=null))));
        if(ReplayDeadLatchProbe.Enabled)log("MEMORY LATCH "+n+" "+ReplayDeadLatchProbe.Stats);
        if(ProductionContinuationProbe.Enabled)log("MEMORY PRODUCTION CONTINUATIONS "+n+" "+ProductionContinuationProbe.Stats);
        if(Environment.GetEnvironmentVariable("REPLAY_TASK_TRACK")=="1")SeekPendingWorkProbe.Report(n,log);
        if(n==8&&Environment.GetEnvironmentVariable("REPLAY_ROOT_SEARCH")=="1")
        {
            var target=old.TryGetValue("aircraft",out var values)?values[0].Target:null;
            if(target!=null){log("MEMORY ROOT searching old aircraft");MemoryRootsProbe.Find(player,target,log);}
            target=old.TryGetValue("level",out values)?values[0].Target:null;
            if(target!=null){log("MEMORY ROOT searching old level");MemoryRootsProbe.Find(player,target,log);}
        }
    }
    public static async UniTask Run(Player player,Action<string> log)
    {
        var times=Environment.GetEnvironmentVariable("REPLAY_MEMORY_FILE")!=null
            ? new[]{player.replayDuration*.05,player.replayDuration*.35,player.replayDuration*.65,player.replayDuration*.95}
            : new[]{2d,25d,50d,75d};
        foreach(var time in times){await player.TimelineJump(time,0,false);await UniTask.NextFrame();}
        Report(player,0,log);
        int iterations=int.TryParse(Environment.GetEnvironmentVariable("REPLAY_MEMORY_ITERATIONS"),out int requested)?Math.Max(8,Math.Min(64,requested)):24;
        for(int i=0;i<iterations;i++)
        {
            var level=NetworkSceneSingleton<LevelInfo>.i;
            Observe("level",level);Observe("cloud",level?.cloudLayer);Observe("mission",MissionManager.CurrentMission);
            Observe("controller",typeof(Player).GetField("_unitCotroller",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(player));
            Observe("spawner",NetworkSceneSingleton<Spawner>.i);Observe("world",ReplayWorldPlayback.Current);
            Observe("aircraft",player.Actors().Where(p=>p.Value is Aircraft).Select(p=>p.Value).FirstOrDefault());
            await player.TimelineJump(times[(i*3+1)%times.Length],0,false);await UniTask.NextFrame();
            if(Environment.GetEnvironmentVariable("REPLAY_PRUNE_STALE")=="1")
            {
                var list=GameManager.SceneSingletons;
                for(int j=list.Count-1;j>=0;j--)if(list[j] is UnityEngine.Object obj&&obj==null){if(list[j].ClearInstance())list.RemoveAt(j);}
            }
            if(!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception(player.PlaybackFailure);
            if((i+1)%8==0)Report(player,i+1,log);
        }
        float until=Time.realtimeSinceStartup+5;while(Time.realtimeSinceStartup<until)await UniTask.NextFrame();
        Report(player,25,log);
        if(Environment.GetEnvironmentVariable("REPLAY_ADVANCE_AFTER_SEEKS")=="1")
        {
            ReplayMod.Director.ReplayDirector.i.SetPlaybackSpeed(1);player.Resume();
            float origin=Time.realtimeSinceStartup;
            foreach(float elapsed in new[]{1f,3f,6f,10f,15f})
            {
                while(Time.realtimeSinceStartup-origin<elapsed)await UniTask.NextFrame();
                Report(player,100+(int)elapsed,log);
            }
            player.Pause();
        }
        if(Environment.GetEnvironmentVariable("REPLAY_UNLOAD_UNUSED")=="1")
        {
            var watch=Stopwatch.StartNew();var operation=Resources.UnloadUnusedAssets();while(!operation.isDone)await UniTask.NextFrame();watch.Stop();
            Report(player,26,log);log("MEMORY unused-asset sweep elapsed="+watch.Elapsed.TotalSeconds.ToString("F2")+"s");
            await player.TimelineJump(50,0,false);if(!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception("Replay failed after unused-asset sweep: "+player.PlaybackFailure);
            Report(player,27,log);
        }
    }
}

[HarmonyPatch(typeof(ReplayMod.Misc.SceneHelper),"StopReplayConnectionAsync")]
public static class SeekMemoryEarlyReleaseProbe
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        if(Environment.GetEnvironmentVariable("REPLAY_EARLY_RELEASE")!="1")return;
        var player=ReplayManager.i?.Playback;
        if(player!=null&&player.IsSeeking)typeof(Player).GetField("_unitCotroller",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(player,null);
    }
}

[HarmonyPatch(typeof(UnitController),"Execute",new[]{typeof(SpawnEvent)})]
public static class SeekMemorySpawnFilterProbe
{
    [HarmonyPrefix]
    public static bool Prefix(SpawnEvent e)
    {
        string filter=Environment.GetEnvironmentVariable("REPLAY_MEMORY_SKIP_SPAWN");
        return filter!="all"&&(filter!="aircraft"||!e.isAircraft);
    }
}

[HarmonyPatch(typeof(UnitController),"ResetWorld")]
public static class SeekMemoryResetFilterProbe
{
    [HarmonyPrefix]
    public static bool Prefix(ref UniTask __result)
    {
        if(Environment.GetEnvironmentVariable("REPLAY_MEMORY_SKIP_RESET")!="1")return true;
        __result=UniTask.CompletedTask;return false;
    }
}

[HarmonyPatch(typeof(Unit),"OnDestroy")]
public static class SeekMemoryDisableCallbackProbe
{
    [HarmonyPrefix]
    public static void Prefix(Unit __instance)
    {
        if(Environment.GetEnvironmentVariable("REPLAY_MEMORY_SUPPRESS_DISABLE")!="1"||!ReplayRuntime.IsPlayback)return;
        if(ReplayManager.i!=null&&!ReplayManager.i.ShouldContinue(__instance.persistentID.Id))__instance.disabled=true;
    }
}

[HarmonyPatch(typeof(UnitController),"Execute",new[]{typeof(WorldEnvironmentStateEvent)})]
public static class SeekMemoryEnvironmentFilterProbe
{
    [HarmonyPrefix]
    public static bool Prefix()=>Environment.GetEnvironmentVariable("REPLAY_MEMORY_SKIP_ENVIRONMENT")!="1";
}

[HarmonyPatch(typeof(ReplayCloudParticleClock),"Ensure")]
public static class SeekMemoryCloudClockFilterProbe
{
    [HarmonyPrefix]
    public static bool Prefix()=>Environment.GetEnvironmentVariable("REPLAY_MEMORY_SKIP_CLOUD_CLOCK")!="1";
}
