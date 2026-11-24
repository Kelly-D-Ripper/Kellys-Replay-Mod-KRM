using System;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using ReplayMod.Core;

[HarmonyPatch]
internal static class ProductionContinuationProbe
{
    internal struct State { internal Action Original; internal bool Playback; }
    private static int liveChecks,liveWrapped,replayWrapped;
    internal static bool Enabled=>Environment.GetEnvironmentVariable("REPLAY_MONITOR_CONTINUATIONS")=="1";
    internal static int LiveWrapped=>liveWrapped;
    internal static int ReplayWrapped=>replayWrapped;
    internal static string Stats=>"liveChecks="+liveChecks+" liveWrapped="+liveWrapped+" replayWrapped="+replayWrapped;
    [HarmonyPrepare] private static bool Prepare()=>Enabled;
    private static MethodBase TargetMethod()=>AccessTools.Method(AccessTools.TypeByName("ReplayMod.Patches.ReplayPhysicsContinuationLifetime"),"Wrap");
    [HarmonyPrefix] private static void Prefix(Action __0,out State __state)=>__state=new State{Original=__0,Playback=ReplayRuntime.IsPlayback};
    [HarmonyPostfix] private static void Postfix(Action __0,State __state)
    {
        if(!__state.Playback){liveChecks++;if(!ReferenceEquals(__state.Original,__0))liveWrapped++;}
        else if(!ReferenceEquals(__state.Original,__0))replayWrapped++;
    }
    internal static void CheckOneShot(Action<string> log)
    {
        var lifetime=AccessTools.TypeByName("ReplayMod.Patches.ReplayPhysicsContinuationLifetime");
        var ticketType=lifetime?.GetNestedType("Ticket",BindingFlags.NonPublic)??throw new Exception("Continuation ticket unavailable");
        var invoke=AccessTools.Method(ticketType,"Invoke");var action=AccessTools.Field(ticketType,"Continuation");
        var ownerField=AccessTools.Field(ticketType,"Owner");var unitField=AccessTools.Field(ticketType,"Unit");var sceneField=AccessTools.Field(ticketType,"Scene");
        var forcing=AccessTools.Field(lifetime,"forcing");
        var live=UnityEngine.Object.FindObjectsOfType<Unit>().FirstOrDefault(u=>u!=null)??throw new Exception("No live fixture unit");
        void Check(bool stale,bool fail)
        {
            var ticket=Activator.CreateInstance(ticketType,true);int called=0,observed=-1;
            if(!stale){ownerField.SetValue(ticket,live);unitField.SetValue(ticket,live);sceneField.SetValue(ticket,live.gameObject.scene);}
            Action callback=()=>{called++;observed=(int)forcing.GetValue(null);invoke.Invoke(ticket,null);if(fail)throw new InvalidOperationException("expected test exception");};
            action.SetValue(ticket,callback);
            try{invoke.Invoke(ticket,null);if(fail)throw new Exception("Expected callback exception was swallowed");}
            catch(TargetInvocationException error) when(fail&&error.InnerException is InvalidOperationException){}
            invoke.Invoke(ticket,null);
            if(called!=1||observed!=(stale?1:0)||(int)forcing.GetValue(null)!=0||action.GetValue(ticket)!=null)throw new Exception("Continuation one-shot/guard test failed");
        }
        Check(false,false);Check(true,false);Check(true,true);
        log("PRODUCTION CONTINUATION PASS live and stale callbacks execute at most once, including re-entry and thrown callback cleanup");
    }
}
