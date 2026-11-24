using Mono.Cecil;

public static class SeekLifetimeChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool pass){checks++;if(!pass)failures++;Console.WriteLine((pass?"PASS ":"FAIL ")+"seek lifetime "+name);}
        bool NativeContinuation(string type,string method,string part)
        {
            if(!types.TryGetValue(type,out var owner)||!owner.Fields.Any(f=>f.Name==part&&f.FieldType.Name=="AeroPart"))return false;
            var native=owner.Methods.SingleOrDefault(m=>m.Name==method&&m.ReturnType.FullName=="Cysharp.Threading.Tasks.UniTask");
            var attr=native?.CustomAttributes.SingleOrDefault(a=>a.AttributeType.FullName=="System.Runtime.CompilerServices.AsyncStateMachineAttribute");
            if(attr?.ConstructorArguments.Count!=1||attr.ConstructorArguments[0].Value is not TypeReference reference||!types.TryGetValue(reference.FullName,out var state))return false;
            var move=state.Methods.SingleOrDefault(m=>m.Name=="MoveNext"&&m.HasBody);
            return state.Fields.Any(f=>f.Name=="<>4__this"&&f.FieldType.FullName==type)&&move!=null&&move.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="Cysharp.Threading.Tasks.YieldAwaitable/Awaiter"&&m.Name=="GetResult")==1;
        }
        Check("cargo ramp state-machine seam",NativeContinuation("CargoRamp","EnableLatch","rampPart"));
        Check("canopy state-machine seam",NativeContinuation("Canopy","EjectionSequence","attachedPart"));
        Check("UniTask fixed-loop continuation API",types.TryGetValue("Cysharp.Threading.Tasks.PlayerLoopHelper",out var loop)&&loop.Methods.Any(m=>m.Name=="AddContinuation"&&m.IsStatic&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"Cysharp.Threading.Tasks.PlayerLoopTiming","System.Action"})));
        Check("UniTask continuation runner exposes its state",types.TryGetValue("Cysharp.Threading.Tasks.CompilerServices.AsyncUniTask`1",out var runner)&&runner.Fields.Any(f=>f.Name=="stateMachine"));
        var compiled=mod.MainModule.Types.SelectMany(AllTypes).ToDictionary(t=>t.FullName);
        MethodDefinition? Method(string type,string name)=>compiled.TryGetValue(type,out var t)?t.Methods.FirstOrDefault(m=>m.Name==name&&m.HasBody):null;
        bool Calls(MethodDefinition? method,string type,string name)=>method?.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name)==true;
        const string lifetime="ReplayMod.Patches.ReplayPhysicsContinuationLifetime";
        Check("queue wrapping restricted to replay and its main thread",Calls(Method(lifetime,"Wrap"),"ReplayMod.Core.ReplayRuntime","get_IsPlayback")&&Calls(Method(lifetime,"Wrap"),"System.Threading.Thread","get_ManagedThreadId"));
        Check("owned runtime services pending work",Calls(Method("ReplayMod.ReplayRuntimeHost","Update"),lifetime,"Pump"));
        Check("queue patch targets selected physics timings",Calls(Method("ReplayMod.Patches.ReplayPhysicsContinuationQueuePatch","Prefix"),lifetime,"Wrap"));
        Console.WriteLine($"Seek lifetime API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
    {yield return type;foreach(var child in type.NestedTypes.SelectMany(AllTypes))yield return child;}
}
