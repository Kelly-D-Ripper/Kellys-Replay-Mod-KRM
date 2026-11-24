using Mono.Cecil;

public static class ReplayOpeningFailureContractChecks
{
    public static (int Checks,int Failures) Run(AssemblyDefinition mod)
    {
        var types=All(mod.MainModule.Types).ToDictionary(t=>t.FullName);int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"opening restore "+name);}
        MethodDefinition? Body(string type,string name)=>types.TryGetValue(type,out var t)?t.Methods.FirstOrDefault(m=>m.Name==name&&m.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name)==true;
        const string player="ReplayMod.Core.Player";
        var opening=types.Values.Where(t=>t.FullName.StartsWith(player+"/",StringComparison.Ordinal)&&t.Name.Contains("<StartPlaying>")).SelectMany(t=>t.Methods).FirstOrDefault(m=>m.Name=="MoveNext"&&m.HasBody);
        Check("startup errors retain the failed opening stage and original exception",opening!=null&&new[]{"reading the recording","checking the recording","loading the recorded terrain","preparing the replay world","restoring the opening frame"}.All(text=>opening.Body.Instructions.Any(i=>i.Operand is string value&&value==text))&&Calls(opening,"System.IO.IOException",".ctor")&&opening.Body.ExceptionHandlers.Any());
        Check("event failure context survives playback restore and the Library error notice",Calls(Body(player,"TryCommitFrame"),player,"ExecuteReplayEvent")&&Calls(Body(player,"DrainAvailable"),player,"ExecuteReplayEvent")&&types.Values.Where(t=>t.FullName.StartsWith(player+"/",StringComparison.Ordinal)&&t.Name.Contains("<ExecuteRestore>")).SelectMany(t=>t.Methods).Any(m=>Calls(m,player,"ExecuteReplayEvent"))&&Calls(Body(player,"ExecuteReplayEvent"),"ReplayMod.Events.IReplayEvent","get_EventType")&&Calls(Body(player,"ExecuteReplayEvent"),"System.IO.InvalidDataException",".ctor")&&types.Values.Where(t=>t.FullName.StartsWith("ReplayMod.UI.TrailerSuiteUi",StringComparison.Ordinal)).SelectMany(t=>t.Methods).Any(m=>Calls(m,"ReplayMod.Core.ReplayManager","StartPlaying")&&m.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.DeclaringType.FullName=="ReplayMod.Director.ReplayDirector"&&f.Name=="Status")));
        var copy=Body("ReplayMod.Core.ReplaySeekState","CloneEvent");
        Check("checkpoint copies keep final health but remove historical damage impulses",copy!=null&&new[]{"pierce","blast","fire","impact"}.All(name=>copy.Body.Instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.FullName=="ReplayMod.Events.ConcreteEvents.PartDamageEvent"&&f.Name==name)));
        Console.WriteLine($"Replay opening failure API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> roots){foreach(var t in roots){yield return t;foreach(var child in All(t.NestedTypes))yield return child;}}
}
