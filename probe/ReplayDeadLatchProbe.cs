using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;

// Test-only hypothesis: a destroyed ramp may safely complete its pending
// continuation without waiting for a physics tick in a paused replay.
internal static class ReplayDeadLatchProbe
{
    internal sealed class Ticket
    {
        internal UnityEngine.Object owner;
        internal Unit unit;
        internal UnityEngine.SceneManagement.Scene scene;
        internal Action action;
        internal void Run(){var callback=action;action=null;owner=null;unit=null;callback?.Invoke();}
        internal void Cancel(){forcing++;try{Run();}finally{forcing--;}}
    }
    private static readonly List<Ticket> pending=new List<Ticket>();
    private static int pumpCalls,wrapped,released,forcing;
    internal static int Wrapped=>wrapped;
    internal static string Stats=>"pump="+pumpCalls+" wrapped="+wrapped+" released="+released+" pending="+pending.Count;
    private static readonly FieldInfo rampPartField=AccessTools.Field(typeof(CargoRamp),"rampPart");
    private static readonly FieldInfo canopyPartField=AccessTools.Field(typeof(Canopy),"attachedPart");
    private static Unit UnitFor(UnityEngine.Object owner)
    {
        if(ReferenceEquals(owner,null))return null;
        if(owner is PilotDismounted pilot)return pilot;
        var part=(owner is CargoRamp?rampPartField:owner is Canopy?canopyPartField:null)?.GetValue(owner) as AeroPart;
        return ReferenceEquals(part,null)?null:part.parentUnit;
    }
    internal static bool Enabled=>Environment.GetEnvironmentVariable("REPLAY_CANCEL_DEAD_LATCH")=="1";
    internal static void StartPump(){if(Enabled&&Environment.GetEnvironmentVariable("REPLAY_PROBE_HOST_PUMP")!="1")PumpLoop().Forget();}
    private static async UniTaskVoid PumpLoop()
    {while(Enabled&&UnityEngine.Application.isPlaying){Pump();await UniTask.NextFrame();}}
    internal static void Wrap(ref Action action)
    {
        if(!Enabled||!ReplayRuntime.IsPlayback||action?.Target==null)return;
        var runner=action.Target;var type=runner.GetType();
        if(!type.FullName.Contains("CargoRamp+<EnableLatch>")&&!type.FullName.Contains("Canopy+<EjectionSequence>")&&!type.FullName.Contains("PilotDismounted+<Setup>"))return;
        var state=AccessTools.Field(type,"stateMachine")?.GetValue(runner);
        var owner=state==null?null:AccessTools.Field(state.GetType(),"<>4__this")?.GetValue(state) as UnityEngine.Object;
        if(owner==null)return;
        var unit=UnitFor(owner);if(unit==null)return;
        var ticket=new Ticket{owner=owner,unit=unit,scene=unit.gameObject.scene,action=action};pending.Add(ticket);action=ticket.Run;wrapped++;
    }
    internal static void Pump()
    {
        if(!Enabled)return;
        pumpCalls++;
        for(int i=pending.Count-1;i>=0;i--)
        {
            var ticket=pending[i];
            if(ticket.action==null){pending.RemoveAt(i);continue;}
            var scene=ticket.scene;
            if((ticket.owner==null||ticket.unit==null)&&(!scene.IsValid()||!scene.isLoaded)){pending.RemoveAt(i);released++;ticket.Cancel();}
        }
    }
    internal static void RequireAlive(CargoRamp owner)
    {if(forcing>0&&(!ReferenceEquals(owner,null))&&(owner==null||UnitFor(owner)==null))throw new OperationCanceledException("Test: replay ramp or parent destroyed before latch tick");}
    internal static void RequireCanopyAlive(Canopy owner)
    {if(forcing>0&&(!ReferenceEquals(owner,null))&&(owner==null||UnitFor(owner)==null))throw new OperationCanceledException("Test: replay canopy or parent destroyed before ejection tick");}
}

[HarmonyPatch(typeof(ReplayMod.ReplayRuntimeHost),"Update")]
internal static class ReplayDeadLatchHostProbe
{
    [HarmonyPrepare] private static bool Prepare()=>ReplayDeadLatchProbe.Enabled;
    [HarmonyPostfix] private static void Postfix()
    {if(Environment.GetEnvironmentVariable("REPLAY_PROBE_HOST_PUMP")=="1")ReplayDeadLatchProbe.Pump();}
}

[HarmonyPatch(typeof(PlayerLoopHelper),"AddContinuation",new[]{typeof(PlayerLoopTiming),typeof(Action)})]
internal static class ReplayDeadLatchQueueProbe
{
    [HarmonyPrepare] private static bool Prepare()=>ReplayDeadLatchProbe.Enabled;
    [HarmonyPrefix] private static void Prefix(PlayerLoopTiming __0,ref Action __1)
    {if(__0==PlayerLoopTiming.LastFixedUpdate||__0==PlayerLoopTiming.FixedUpdate)ReplayDeadLatchProbe.Wrap(ref __1);}
}

[HarmonyPatch]
internal static class ReplayDeadCanopyContinuationProbe
{
    [HarmonyPrepare] private static bool Prepare()=>ReplayDeadLatchProbe.Enabled;
    private static MethodBase TargetMethod()
    {
        var method=AccessTools.Method(typeof(Canopy),"EjectionSequence");
        var type=method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        return type==null?throw new MissingMethodException("Canopy ejection state machine unavailable"):AccessTools.Method(type,"MoveNext");
    }
    [HarmonyTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
    {
        var code=new List<CodeInstruction>(instructions);var owner=AccessTools.Field(__originalMethod.DeclaringType,"<>4__this");
        var result=AccessTools.Method(typeof(Cysharp.Threading.Tasks.YieldAwaitable.Awaiter),"GetResult");int count=0;
        if(owner==null||owner.FieldType!=typeof(Canopy))throw new InvalidOperationException("Canopy owner field changed");
        for(int i=0;i<code.Count;i++)if(code[i].Calls(result))
        {
            code.InsertRange(i+1,new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldfld,owner),new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ReplayDeadLatchProbe),nameof(ReplayDeadLatchProbe.RequireCanopyAlive)))});
            count++;i+=3;
        }
        if(count!=1)throw new InvalidOperationException("Canopy yield seam changed");return code;
    }
}

[HarmonyPatch]
internal static class ReplayDeadLatchContinuationProbe
{
    [HarmonyPrepare] private static bool Prepare()=>ReplayDeadLatchProbe.Enabled;
    private static MethodBase TargetMethod()
    {
        var method=AccessTools.Method(typeof(CargoRamp),"EnableLatch");
        var type=method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        return type==null?throw new MissingMethodException("Cargo ramp latch state machine unavailable"):AccessTools.Method(type,"MoveNext");
    }
    [HarmonyTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
    {
        var code=new List<CodeInstruction>(instructions);var owner=AccessTools.Field(__originalMethod.DeclaringType,"<>4__this");
        var result=AccessTools.Method(typeof(Cysharp.Threading.Tasks.YieldAwaitable.Awaiter),"GetResult");int count=0;
        if(owner==null||owner.FieldType!=typeof(CargoRamp))throw new InvalidOperationException("Cargo ramp owner field changed");
        for(int i=0;i<code.Count;i++)if(code[i].Calls(result))
        {
            code.InsertRange(i+1,new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldfld,owner),new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ReplayDeadLatchProbe),nameof(ReplayDeadLatchProbe.RequireAlive)))});
            count++;i+=3;
        }
        if(count!=1)throw new InvalidOperationException("Cargo ramp yield seam changed");return code;
    }
}
