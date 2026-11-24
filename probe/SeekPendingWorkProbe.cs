using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

public static class SeekPendingWorkProbe
{
    private static readonly List<(string name,WeakReference weak)> work=new List<(string,WeakReference)>();
    public static IEnumerable<object> LiveWork()=>work.Select(w=>w.weak.Target).Where(x=>x!=null);
    public static void Track(object item,string prefix)
    {
        if(Environment.GetEnvironmentVariable("REPLAY_TASK_TRACK")!="1"||!ReplayRuntime.IsPlayback||item==null||work.Count>=60000)return;
        var name=item is Delegate callback?(callback.Target?.GetType().FullName??callback.Method.DeclaringType?.FullName??"static")+"::"+callback.Method.Name:item.GetType().FullName;
        work.Add((prefix+name,new WeakReference(item)));
    }
    public static void Report(int n,Action<string> log)
    {
        var live=work.Where(w=>w.weak.IsAlive).GroupBy(w=>w.name).OrderByDescending(g=>g.Count()).Take(25).Select(g=>g.Key+"="+g.Count());
        log("PENDING WORK "+n+" observed="+work.Count+" live: "+string.Join("; ",live));
        var details=new Dictionary<string,int>();var seen=new HashSet<object>(new RefComparer());
        foreach(var entry in work)
        {
            if(!entry.name.EndsWith("+DelayPromise")&&!entry.name.EndsWith("+DelayIgnoreTimeScalePromise")&&!entry.name.EndsWith("+NextFramePromise")&&!entry.name.Contains("AsyncOperationHandleConfiguredSource`1[[LiveryData"))continue;
            var promise=entry.weak.Target;if(promise==null||!seen.Add(promise))continue;
            var core=promise.GetType().GetField("core",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(promise);
            var continuation=core?.GetType().GetField("continuationState",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(core);
            string name=(entry.name.Contains("LiveryData")?"LIVERY ":entry.name.EndsWith("+NextFramePromise")?"NEXT ":entry.name.EndsWith("+DelayIgnoreTimeScalePromise")?"UNSCALED ":"SCALED ")+(continuation is Delegate d?(d.Target?.GetType().FullName??d.Method.DeclaringType?.FullName??"static")+"::"+d.Method.Name:(continuation?.GetType().FullName??"none"));
            if(continuation is Delegate next&&next.Target!=null&&name.Contains("ContinueWith"))
            {
                var fields=next.Target.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                var state=fields.FirstOrDefault(f=>f.Name.IndexOf("stateMachine",StringComparison.OrdinalIgnoreCase)>=0)?.GetValue(next.Target);
                var action=state?.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Select(f=>f.GetValue(state)).OfType<Delegate>().FirstOrDefault();
                if(action!=null)name+=" -> "+action.Method.DeclaringType?.FullName+"::"+action.Method.Name;
            }
            details[name]=details.TryGetValue(name,out var count)?count+1:1;
        }
        log("PENDING SCALED DELAYS "+n+": "+string.Join("; ",details.OrderByDescending(p=>p.Value).Take(20).Select(p=>p.Key+"="+p.Value)));
    }
    sealed class RefComparer:IEqualityComparer<object>{public new bool Equals(object a,object b)=>ReferenceEquals(a,b);public int GetHashCode(object item)=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item);}
}

[HarmonyPatch(typeof(MonoBehaviour),"StartCoroutine",new[]{typeof(IEnumerator)})]
public static class SeekPendingCoroutinePatch
{
    [HarmonyPrefix] public static void Prefix(MonoBehaviour __instance,IEnumerator routine)=>SeekPendingWorkProbe.Track(routine,"coroutine:");
}

[HarmonyPatch(typeof(PlayerLoopHelper),"AddAction")]
public static class SeekPendingUniTaskPatch
{
    [HarmonyPrefix] public static void Prefix(IPlayerLoopItem action)=>SeekPendingWorkProbe.Track(action,"unitask:");
}

[HarmonyPatch(typeof(PlayerLoopHelper),"AddContinuation",new[]{typeof(PlayerLoopTiming),typeof(Action)})]
public static class SeekPendingFixedContinuationPatch
{
    [HarmonyPrefix] public static void Prefix(PlayerLoopTiming __0,Action __1)
    {
        if(__0.ToString().Contains("FixedUpdate"))SeekPendingWorkProbe.Track(__1,"continuation:"+__0+":");
    }
}
