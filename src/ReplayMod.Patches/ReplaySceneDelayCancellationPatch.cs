using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Patches;

// These native loops use scaled delays without their owner's destruction token.
// A paused replay can reload many worlds before those delays finish, retaining
// destroyed actors and airbases. Keep the native work, but end its wait when
// its own component is destroyed during replay.
[HarmonyPatch]
internal static class ReplaySceneDelayCancellationPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach(var target in new[]{(typeof(TargetDetector),"RepeatSearch"),(typeof(LaserDesignator),"LaseTargets"),(typeof(Airbase),"WaitRepair")})
        {
            var original=AccessTools.Method(target.Item1,target.Item2);
            var attribute=original?.GetCustomAttribute<AsyncStateMachineAttribute>();
            var move=attribute==null?null:AccessTools.Method(attribute.StateMachineType,"MoveNext");
            if(move!=null)yield return move;
            else Plugin.logger.LogWarning("Replay lifetime cleanup unavailable for "+target.Item1.Name+"."+target.Item2+"; native behavior retained.");
        }
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
    {
        var source=instructions.ToList();
        var owner=AccessTools.Field(__originalMethod.DeclaringType,"<>4__this");
        var delay=AccessTools.Method(typeof(UniTask),"Delay",new[]{typeof(int),typeof(bool),typeof(PlayerLoopTiming),typeof(CancellationToken),typeof(bool)});
        var replacement=AccessTools.Method(typeof(ReplaySceneDelayCancellationPatch),nameof(Delay));
        int expected=__originalMethod.DeclaringType.DeclaringType==typeof(TargetDetector)?2:1;
        var calls=delay==null?Array.Empty<CodeInstruction>():source.Where(i=>i.Calls(delay)).ToArray();
        if(owner==null||!typeof(MonoBehaviour).IsAssignableFrom(owner.FieldType)||replacement==null||calls.Length!=expected||calls.Any(i=>i.blocks.Count!=0))
        {
            Plugin.logger.LogWarning("Replay lifetime cleanup could not match "+__originalMethod.DeclaringType.FullName+"; native behavior retained.");
            return source;
        }
        var result=new List<CodeInstruction>(source.Count+calls.Length*2);
        foreach(var instruction in source)
        {
            if(instruction.Calls(delay))
            {
                var load=new CodeInstruction(OpCodes.Ldarg_0);load.labels.AddRange(instruction.labels);instruction.labels.Clear();
                result.Add(load);result.Add(new CodeInstruction(OpCodes.Ldfld,owner));instruction.operand=replacement;
            }
            result.Add(instruction);
        }
        return result;
    }

    public static UniTask Delay(int milliseconds,bool ignoreTimeScale,PlayerLoopTiming timing,CancellationToken cancellation,bool immediately,MonoBehaviour owner)
    {
        if(ReplayRuntime.IsPlayback&&owner!=null&&!cancellation.CanBeCanceled&&!immediately)
            return UniTask.Delay(milliseconds,ignoreTimeScale,timing,owner.destroyCancellationToken,true);
        return UniTask.Delay(milliseconds,ignoreTimeScale,timing,cancellation,immediately);
    }
}
