using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

// Native Setup waits for a physics tick. A paused replay can replace the scene
// before that tick occurs, leaving a continuation with a destroyed pilot.
internal static class ReplayPilotSetupLifetime
{
    private static readonly ConditionalWeakTable<PilotDismounted,object> replaySetups=new ConditionalWeakTable<PilotDismounted,object>();
    internal static void Observe(PilotDismounted pilot)
    {
        if(ReplayRuntime.IsPlayback&&!ReferenceEquals(pilot,null))replaySetups.GetValue(pilot,_=>new object());
    }
    internal static void RequireAlive(PilotDismounted pilot)
    {
        // Keep the start-time ownership even after playback closes. Live-game
        // tasks and all still-living pilots continue through the native body.
        if(!ReferenceEquals(pilot,null)&&replaySetups.TryGetValue(pilot,out _)&&pilot==null)
            throw new OperationCanceledException("Replay pilot was destroyed before its setup physics tick.");
    }
}

[HarmonyPatch(typeof(PilotDismounted),"Setup")]
internal static class ReplayPilotSetupStartPatch
{
    [HarmonyPrefix] private static void Prefix(PilotDismounted __instance)=>ReplayPilotSetupLifetime.Observe(__instance);
}

[HarmonyPatch]
internal static class ReplayPilotSetupContinuationPatch
{
    private static MethodBase TargetMethod()
    {
        var setup=AccessTools.Method(typeof(PilotDismounted),"Setup");
        var state=setup?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        return state==null?throw new MissingMethodException("Pilot setup state machine is unavailable"):AccessTools.Method(state,"MoveNext");
    }
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
    {
        var code=new List<CodeInstruction>(instructions);
        var owner=AccessTools.Field(__originalMethod.DeclaringType,"<>4__this");
        var resumed=AccessTools.Method(typeof(Cysharp.Threading.Tasks.YieldAwaitable.Awaiter),"GetResult");
        int matches=0;
        if(owner==null||owner.FieldType!=typeof(PilotDismounted))throw new InvalidOperationException("Pilot setup owner field changed");
        for(int index=0;index<code.Count;index++)
        {
            if(!code[index].Calls(resumed))continue;
            code.InsertRange(index+1,new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldfld,owner),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ReplayPilotSetupLifetime),nameof(ReplayPilotSetupLifetime.RequireAlive)))});
            matches++;index+=3;
        }
        if(matches!=1)throw new InvalidOperationException("Pilot setup yield seam changed; expected one continuation");
        return code;
    }
}
