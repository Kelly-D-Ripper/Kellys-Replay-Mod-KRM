using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReplayMod.Patches;

// Some native replay actors wait for a physics tick during setup or destruction.
// While paused, that tick never arrives, even after a seek unloads their scene.
// Retire only the continuations from the unloaded scene; living actors and live
// gameplay keep the native fixed-update schedule.
internal static class ReplayPhysicsContinuationLifetime
{
    private sealed class Binding
    {
        internal FieldInfo State,Owner;
    }
    private sealed class Ticket
    {
        internal UnityEngine.Object Owner;
        internal Unit Unit;
        internal Scene Scene;
        internal Action Continuation;
        internal bool IsStale => (Owner==null||Unit==null)&&(!Scene.IsValid()||!Scene.isLoaded);
        internal void Invoke()
        {
            var continuation=Continuation;if(continuation==null)return;
            bool cancelled=IsStale;
            Continuation=null;Owner=null;Unit=null;
            if(cancelled)forcing++;
            try{continuation();}finally{if(cancelled)forcing--;}
        }
    }
    private static readonly Dictionary<Type,Binding> bindings=new Dictionary<Type,Binding>();
    private static readonly List<Ticket> pending=new List<Ticket>();
    private static readonly FieldInfo rampPart=AccessTools.Field(typeof(CargoRamp),"rampPart");
    private static readonly FieldInfo canopyPart=AccessTools.Field(typeof(Canopy),"attachedPart");
    private static int mainThread,forcing;
    private static bool warned;

    internal static void Initialize()=>mainThread=Thread.CurrentThread.ManagedThreadId;
    private static Unit UnitFor(UnityEngine.Object owner)
    {
        if(ReferenceEquals(owner,null))return null;
        if(owner is PilotDismounted pilot)return pilot;
        var field=owner is CargoRamp?rampPart:owner is Canopy?canopyPart:null;
        var part=field?.GetValue(owner) as AeroPart;
        return ReferenceEquals(part,null)?null:part.parentUnit;
    }
    private static Binding Bind(Type runner)
    {
        if(bindings.TryGetValue(runner,out var known))return known;
        Binding result=null;
        if(runner.IsGenericType)
        {
            var arguments=runner.GetGenericArguments();
            if(arguments.Length==1)
            {
                var state=arguments[0];var ownerType=state.DeclaringType;
                string method=ownerType==typeof(CargoRamp)?"EnableLatch":ownerType==typeof(Canopy)?"EjectionSequence":ownerType==typeof(PilotDismounted)?"Setup":null;
                var native=method==null?null:AccessTools.Method(ownerType,method);
                if(native?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType==state)
                {
                    var stateField=AccessTools.Field(runner,"stateMachine");var ownerField=AccessTools.Field(state,"<>4__this");
                    if(stateField!=null&&ownerField!=null&&ownerField.FieldType==ownerType)result=new Binding{State=stateField,Owner=ownerField};
                }
            }
        }
        bindings[runner]=result;return result;
    }
    internal static void Wrap(ref Action continuation)
    {
        if(Thread.CurrentThread.ManagedThreadId!=mainThread||!ReplayRuntime.IsPlayback||continuation?.Target==null)return;
        try
        {
            var runner=continuation.Target;var binding=Bind(runner.GetType());if(binding==null)return;
            var state=binding.State.GetValue(runner);var owner=state==null?null:binding.Owner.GetValue(state) as UnityEngine.Object;
            if(owner==null)return;
            var unit=UnitFor(owner);if(unit==null)return;
            var ticket=new Ticket{Owner=owner,Unit=unit,Scene=unit.gameObject.scene,Continuation=continuation};
            pending.Add(ticket);continuation=ticket.Invoke;
        }
        catch(Exception error)
        {
            if(!warned){warned=true;Plugin.SafeLogWarning("Replay physics cleanup unavailable: "+error.Message);}
        }
    }
    internal static void Pump()
    {
        for(int i=pending.Count-1;i>=0;i--)
        {
            var ticket=pending[i];
            if(ticket.Continuation==null){pending.RemoveAt(i);continue;}
            if(!ticket.IsStale)continue;
            pending.RemoveAt(i);
            try{ticket.Invoke();}catch(Exception error){Plugin.SafeLogWarning("Replay physics cleanup failed: "+error.Message);}
        }
    }
    internal static void RequireRampAlive(CargoRamp owner)
    {
        if(forcing>0&&!ReferenceEquals(owner,null)&&(owner==null||UnitFor(owner)==null))
            throw new OperationCanceledException("Replay cargo ramp scene was unloaded before its physics tick.");
    }
    internal static void RequireCanopyAlive(Canopy owner)
    {
        if(forcing>0&&!ReferenceEquals(owner,null)&&(owner==null||UnitFor(owner)==null))
            throw new OperationCanceledException("Replay canopy scene was unloaded before its physics tick.");
    }
}

[HarmonyPatch(typeof(PlayerLoopHelper),"AddContinuation",new[]{typeof(PlayerLoopTiming),typeof(Action)})]
internal static class ReplayPhysicsContinuationQueuePatch
{
    [HarmonyPrefix] private static void Prefix(PlayerLoopTiming __0,ref Action __1)
    {
        if(__0==PlayerLoopTiming.FixedUpdate||__0==PlayerLoopTiming.LastFixedUpdate)
            ReplayPhysicsContinuationLifetime.Wrap(ref __1);
    }
}

internal static class ReplayPhysicsContinuationPatch
{
    internal static MethodBase Target(Type owner,string method)
    {
        var native=AccessTools.Method(owner,method);
        var state=native?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        return state==null?throw new MissingMethodException(owner.Name+" "+method+" state machine is unavailable"):AccessTools.Method(state,"MoveNext");
    }
    internal static IEnumerable<CodeInstruction> Guard(IEnumerable<CodeInstruction> instructions,MethodBase original,Type ownerType,string guardName)
    {
        var code=new List<CodeInstruction>(instructions);var owner=AccessTools.Field(original.DeclaringType,"<>4__this");
        var getResult=AccessTools.Method(typeof(Cysharp.Threading.Tasks.YieldAwaitable.Awaiter),"GetResult");int matches=0;
        if(owner==null||owner.FieldType!=ownerType)throw new InvalidOperationException(ownerType.Name+" continuation owner changed");
        for(int i=0;i<code.Count;i++)if(code[i].Calls(getResult))
        {
            code.InsertRange(i+1,new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldfld,owner),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ReplayPhysicsContinuationLifetime),guardName))});
            matches++;i+=3;
        }
        if(matches!=1)throw new InvalidOperationException(ownerType.Name+" physics continuation changed; expected one yield");
        return code;
    }
}

[HarmonyPatch]
internal static class ReplayCargoRampContinuationPatch
{
    private static MethodBase TargetMethod()=>ReplayPhysicsContinuationPatch.Target(typeof(CargoRamp),"EnableLatch");
    [HarmonyTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code,MethodBase __originalMethod)
        =>ReplayPhysicsContinuationPatch.Guard(code,__originalMethod,typeof(CargoRamp),nameof(ReplayPhysicsContinuationLifetime.RequireRampAlive));
}

[HarmonyPatch]
internal static class ReplayCanopyContinuationPatch
{
    private static MethodBase TargetMethod()=>ReplayPhysicsContinuationPatch.Target(typeof(Canopy),"EjectionSequence");
    [HarmonyTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code,MethodBase __originalMethod)
        =>ReplayPhysicsContinuationPatch.Guard(code,__originalMethod,typeof(Canopy),nameof(ReplayPhysicsContinuationLifetime.RequireCanopyAlive));
}
