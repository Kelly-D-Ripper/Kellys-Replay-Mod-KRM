using Mono.Cecil;
using Mono.Cecil.Cil;

public static class NeutralCargoSpawnChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"neutral cargo "+name);}
        MethodDefinition? Native(string type,string name)=>types.TryGetValue(type,out var definition)?definition.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody):null;
        var controller=mod.MainModule.Types.Single(type=>type.FullName=="ReplayMod.Core.UnitController");
        MethodDefinition? Body(string name)=>controller.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody);
        bool Calls(MethodDefinition? method,string type,string name)=>method?.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName==type&&call.Name==name)==true;
        var nativeSpawn=Native("Spawner","SpawnContainer");
        Check("native container spawner accepts original nullable HQ",nativeSpawn?.ReturnType.FullName=="Container"&&
            nativeSpawn.Parameters.Select(parameter=>parameter.ParameterType.FullName).SequenceEqual(new[]{"UnityEngine.GameObject","GlobalPosition","UnityEngine.Quaternion","FactionHQ","System.String"})&&
            Calls(nativeSpawn,"Unit","set_NetworkHQ")&&nativeSpawn.Body.Instructions.All(instruction=>instruction.Operand is not MethodReference call||call.DeclaringType.FullName!="FactionHQ"));
        var classifier=Body("HasContainerSpawnPath");
        Check("neutral exemption matches container route and verifies prefab root",classifier!=null&&
            new[]{"VehicleDefinition","MissileDefinition","AircraftDefinition","ShipDefinition","BuildingDefinition","SceneryDefinition"}.All(type=>classifier.Body.Instructions.Any(instruction=>instruction.OpCode==OpCodes.Isinst&&instruction.Operand is TypeReference reference&&reference.FullName==type))&&
            classifier.Body.Instructions.Any(instruction=>instruction.Operand is string text&&text=="PILOT")&&
            classifier.Body.Instructions.Any(instruction=>instruction.Operand is GenericInstanceMethod call&&call.Name=="GetComponent"&&call.GenericArguments.Count==1&&call.GenericArguments[0].FullName=="Container"));
        var execute=controller.Methods.Single(method=>method.Name=="Execute"&&method.Parameters.Count==1&&method.Parameters[0].ParameterType.FullName=="ReplayMod.Events.ConcreteEvents.SpawnEvent");
        Check("replay validation classifies before normal faction guard and retains failures",Calls(execute,controller.FullName,"HasContainerSpawnPath")&&Calls(execute,"ReplayMod.Core.ReplaySpawnPolicy","CanSpawnWithoutFaction")&&
            Calls(execute,"FactionRegistry","HqFromName")&&execute.Body.Instructions.Any(instruction=>instruction.OpCode==OpCodes.Throw)&&
            !execute.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName=="FactionHQ"&&call.Name.Contains("Create",StringComparison.Ordinal)));
        var fallback=Body("SpawnPilotOrContainer");
        Check("cargo and pilot retain separate native spawners without ownership rewrites",Calls(fallback,"Spawner","SpawnContainer")&&Calls(fallback,"Spawner","SpawnPilot")&&
            fallback?.Body.Instructions.Any(instruction=>instruction.Operand is string text&&text=="PILOT")==true&&
            fallback.Body.Instructions.All(instruction=>instruction.Operand is not MethodReference call||call.Name!="set_NetworkHQ")&&
            fallback.Body.Instructions.Count(instruction=>instruction.OpCode==OpCodes.Ldarg_2)==2);
        Console.WriteLine($"Neutral cargo spawn API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
}
