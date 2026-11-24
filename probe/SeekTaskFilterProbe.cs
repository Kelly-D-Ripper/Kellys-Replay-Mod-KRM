using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;

[HarmonyPatch]
public static class SeekTaskFilterProbe
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach(var item in new[]{(typeof(TargetDetector),"RepeatSearch"),(typeof(LaserDesignator),"LaseTargets"),(typeof(Airbase),"WaitRepair"),(typeof(NuclearOption.Networking.Player),"WaitShowJoinMenu"),(typeof(RearmVehicleAI),"LeaveDepot")})
        {var method=AccessTools.Method(item.Item1,item.Item2);if(method!=null)yield return method;}
    }
    [HarmonyPrefix]
    static bool Prefix(MethodBase __originalMethod,ref UniTask __result)
    {
        string filter=Environment.GetEnvironmentVariable("REPLAY_SUPPRESS_TASKS");
        if(!ReplayRuntime.IsPlayback||string.IsNullOrEmpty(filter)||Array.IndexOf(filter.Split(','),__originalMethod.Name)<0)return true;
        __result=UniTask.CompletedTask;return false;
    }
}
