using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Cysharp.Threading.Tasks;
using ReplayMod.Patches;

// Test-only bounded timing samples around the same native fan callback.
[HarmonyPatch(typeof(DuctedFan),"Update")]
public static class FanTimingProbe
{
    private static readonly List<long> samples=new List<long>(4096);
    private static bool active;
    public static void Start(){samples.Clear();active=true;}
    [HarmonyPrefix,HarmonyPriority(Priority.First+100)]
    public static void Prefix(out long __state){__state=active?Stopwatch.GetTimestamp():0;}
    [HarmonyPostfix,HarmonyPriority(Priority.Last-100)]
    public static void Postfix(long __state){if(__state!=0&&active&&samples.Count<32000)samples.Add(Stopwatch.GetTimestamp()-__state);}
    public static void Finish(string label,Action<string> log)
    {
        active=false;if(samples.Count<100)throw new Exception("Too few ducted fan timing samples: "+samples.Count);
        var sorted=samples.OrderBy(x=>x).ToArray();double factor=1000000d/Stopwatch.Frequency;
        log("FAN TIMING "+label+" samples="+samples.Count+" mean_us="+(samples.Average()*factor).ToString("F3")+" p95_us="+(sorted[(int)(sorted.Length*.95)]*factor).ToString("F3")+" max_us="+(sorted[sorted.Length-1]*factor).ToString("F3"));
    }
    public static async UniTask Compare(Action<string> log)
    {
        var original=AccessTools.Method(typeof(DuctedFan),"Update");var patch=AccessTools.Method(typeof(ReplayDuctedFanStatePatch),"Prefix");
        if(Harmony.GetPatchInfo(original)?.Prefixes.Any(p=>p.PatchMethod==patch)!=true)throw new Exception("Production fan patch not present");
        async UniTask Sample(string label)
        {Start();float until=UnityEngine.Time.realtimeSinceStartup+2;while(UnityEngine.Time.realtimeSinceStartup<until)await UniTask.NextFrame();Finish(label,log);}
        await Sample("patched");
        var harmony=new Harmony("local.replay.fan.timing.restore");harmony.Unpatch(original,patch);
        try
        {
            if(Harmony.GetPatchInfo(original)?.Prefixes.Any(p=>p.PatchMethod==patch)==true)throw new Exception("Unable to remove fan patch for isolated comparison");
            await Sample("unpatched");
        }
        finally{harmony.Patch(original,prefix:new HarmonyMethod(patch){priority=Priority.First});}
        if(Harmony.GetPatchInfo(original)?.Prefixes.Any(p=>p.PatchMethod==patch)!=true)throw new Exception("Fan patch did not restore after timing comparison");
    }
}
