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

[HarmonyPatch]
public static class SeekTargetDelayProbe
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach(var target in new[]{(typeof(TargetDetector),"RepeatSearch"),(typeof(LaserDesignator),"LaseTargets"),(typeof(Airbase),"WaitRepair")})
        {
            var original=AccessTools.Method(target.Item1,target.Item2);
            var attribute=original?.GetCustomAttribute<AsyncStateMachineAttribute>();
            var move=attribute==null?null:AccessTools.Method(attribute.StateMachineType,"MoveNext");
            if(move!=null)yield return move;
        }
    }
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
    {
        var source=instructions.ToList();
        if(Environment.GetEnvironmentVariable("REPLAY_DELAY_TOKEN")!="1")return source;
        var owner=AccessTools.Field(__originalMethod.DeclaringType,"<>4__this");
        var delay=AccessTools.Method(typeof(UniTask),"Delay",new[]{typeof(int),typeof(bool),typeof(PlayerLoopTiming),typeof(CancellationToken),typeof(bool)});
        var helper=AccessTools.Method(typeof(SeekTargetDelayProbe),nameof(Delay));
        int expected=__originalMethod.DeclaringType.DeclaringType==typeof(TargetDetector)?2:1;
        if(owner==null||delay==null||helper==null||source.Count(i=>i.Calls(delay))!=expected)throw new InvalidOperationException("Unexpected replay delay IL: "+__originalMethod.DeclaringType);
        var result=new List<CodeInstruction>();
        foreach(var instruction in source)
        {
            if(instruction.Calls(delay))
            {
                var load=new CodeInstruction(OpCodes.Ldarg_0);load.labels.AddRange(instruction.labels);instruction.labels.Clear();
                result.Add(load);result.Add(new CodeInstruction(OpCodes.Ldfld,owner));instruction.operand=helper;
            }
            result.Add(instruction);
        }
        return result;
    }
    public static UniTask Delay(int milliseconds,bool ignoreTimeScale,PlayerLoopTiming timing,CancellationToken cancellation,bool immediately,UnityEngine.MonoBehaviour owner)
    {
        if(ReplayRuntime.IsPlayback&&owner!=null)return UniTask.Delay(milliseconds,ignoreTimeScale,timing,owner.destroyCancellationToken,true);
        return UniTask.Delay(milliseconds,ignoreTimeScale,timing,cancellation,immediately);
    }
}
