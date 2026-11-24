using Mono.Cecil;
using Mono.Cecil.Cil;

public static class AirframeOpeningStateContractChecks
{
    public static (int Checks,int Failures) Run(AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"airframe opening "+name);}
        var types=All(mod.MainModule.Types).ToDictionary(type=>type.FullName);
        const string recorder="ReplayMod.Core.Recorder",stateType=recorder+"/RecordedAircraftParts";
        MethodDefinition? Body(string owner,string name)=>types.TryGetValue(owner,out var type)?type.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody):null;
        bool Calls(MethodDefinition? body,string owner,string name)=>body?.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method&&method.DeclaringType.GetElementType().FullName==owner&&method.Name==name)==true;
        bool Reads(MethodDefinition? body,string owner,string name)=>body?.Body.Instructions.Any(instruction=>instruction.OpCode.Code is Code.Ldfld or Code.Ldsfld&&instruction.Operand is FieldReference field&&field.DeclaringType.GetElementType().FullName==owner&&field.Name==name)==true;
        int LastCall(MethodDefinition? body,string owner,string name)=>body?.Body.Instructions.ToList().FindLastIndex(instruction=>instruction.Operand is MethodReference method&&method.DeclaringType.GetElementType().FullName==owner&&method.Name==name)??-1;
        var initial=Body(recorder,"RecordInitialAircraftPartMotion");
        var registration=Body(recorder,"RegisterUnitWhenReady");
        var capture=Body(recorder,"RecordAircraftPartMotion");

        Check("registration publishes the opening airframe after its initial HP events",
            LastCall(registration,recorder,"HandleDamage")>=0&&
            LastCall(registration,recorder,"RecordInitialAircraftPartMotion")>LastCall(registration,recorder,"HandleDamage"));
        Check("opening capture rejects untracked actors and bypasses ordinary position timing",
            initial?.Body.Instructions.Any(instruction=>instruction.OpCode.Code==Code.Isinst&&instruction.Operand is TypeReference type&&type.FullName=="Aircraft")==true&&
            Calls(initial,recorder,"IsTrackedLifecycleUnit")&&Calls(initial,recorder,"TryEnsureRecordedAircraftParts")&&Calls(initial,recorder,"RecordAircraftPartMotion")&&
            !Calls(initial,recorder,"RecordObservedAircraftMotion")&&!Reads(initial,recorder,"_lastPositionUpdateTime"));
        Check("opening eligibility observes existing disabled ejected damaged and detached state",
            initial!=null&&(Reads(initial,"Unit","disabled")||Calls(initial,"Unit","get_disabled"))&&Calls(initial,"Aircraft","HasEjected")&&
            Reads(initial,"UnitPart","hitPoints")&&Reads(initial,"UnitPart","fragmented")&&Calls(initial,"UnitPart","IsDetached")&&
            Reads(initial,"UnitPart/AttachInfo","detachedFromParentPart")&&Reads(initial,stateType,"published")&&
            !initial.Body.Instructions.Any(instruction=>instruction.OpCode.Code is Code.Newobj or Code.Newarr));
        Check("opening pose reuses finite native flags and the current recording clock",
            Reads(capture,recorder,"_virtualTime")&&Calls(capture,"ReplayMod.Data.AircraftPartMotionSnapshot","Valid")&&
            Calls(capture,"UnityEngine.GameObject","get_activeInHierarchy")&&Calls(capture,"UnitPart","IsDetached")&&Reads(capture,"UnitPart","fragmented")&&
            Calls(capture,"ReplayMod.Data.AircraftPartMotionSnapshot","CopyTo")&&Calls(capture,recorder,"QueueEvent"));
        Check("opening state capture never reapplies native damage or breakup",
            initial!=null&&capture!=null&&new[]{initial,capture}.All(method=>method.Body.Instructions.All(instruction=>
                instruction.Operand is not MethodReference call||call.Name is not("ApplyDamage" or "InvokeDamage" or "SpawnFragments" or "Detach" or "TakeDamage")))&&
            new[]{initial,capture}.All(method=>method.Body.Instructions.All(instruction=>instruction.OpCode.Code!=Code.Stfld||instruction.Operand is not FieldReference field||
                field.DeclaringType.FullName is not("UnitPart" or "AeroPart" or "Aircraft" or "Unit"))));
        Console.WriteLine($"Airframe opening state API/integration audit: {checks} groups, {failures} failures");
        return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> roots)
    {foreach(var type in roots){yield return type;foreach(var nested in All(type.NestedTypes))yield return nested;}}
}
