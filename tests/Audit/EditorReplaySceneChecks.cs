using Mono.Cecil;

public static class EditorReplaySceneChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"editor replay scene "+name);}
        MethodDefinition? Native(string type,string name)=>types.TryGetValue(type,out var definition)?definition.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody):null;
        var scene=mod.MainModule.Types.Single(type=>type.FullName=="ReplayMod.Misc.SceneHelper");
        var methods=All(scene).SelectMany(type=>type.Methods).Where(method=>method.HasBody).ToArray();
        MethodDefinition? Body(string name)=>scene.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody);
        bool Calls(MethodDefinition? method,string type,string name)=>method?.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName==type&&call.Name==name)==true;
        bool CallsAny(string type,string name)=>methods.Any(method=>Calls(method,type,name));
        var mission=Native("NuclearOption.SavedMission.Mission",".ctor");
        Check("native fresh mission constructor builds mission-start state",types.TryGetValue("NuclearOption.SavedMission.Mission",out var nativeMission)&&
            nativeMission.Methods.Any(method=>method.IsConstructor&&method.Parameters.Select(parameter=>parameter.ParameterType.FullName).SequenceEqual(new[]{"System.String","System.Boolean"})&&
                Calls(method,"NuclearOption.SavedMission.Mission","SetupNewMission")&&Calls(method,"NuclearOption.SavedMission.Mission","AfterLoad")));
        var fresh=Body("CreateEmptyMission");
        Check("replay shell does not import free-flight template scripts or airbases",Calls(fresh,"NuclearOption.SavedMission.Mission",".ctor")&&
            !Calls(fresh,"NuclearOption.SavedMission.MissionSaveLoad","LoadDefault")&&
            fresh?.Body.Instructions.All(instruction=>instruction.Operand is not FieldReference field||field.Name is not("airbases" or "objectives" or "outcomes" or "LoadKey" or "NewMissionConfig"))==true);
        Check("fresh shell supplies native factions and prevents new AI and early wreck expiry",Calls(fresh,"NuclearOption.SavedMission.Mission","EnsureFactionExists")&&
            new[]{"defaultFactions","preventJoin","preventDonation","AIAircraftLimit","wrecksMaxNumber","wrecksDecayTime","allowEventContent"}.All(name=>
                fresh?.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field&&field.Name==name)==true));
        var validate=Body("ValidateReplayMap");
        Check("explicit recorded terrain is checked by native loader before transition barrier",Calls(validate,"ReplayMod.Core.ReplayMapIdentity","IsExplicitTerrain")&&
            Calls(validate,"NuclearOption.SceneLoading.MapLoader","CanLoad")&&methods.Any(method=>Calls(method,scene.FullName,"ValidateReplayMap")&&
                Calls(method,"ReplayMod.Core.ReplayOperationBarrier","RunAsync")&&
                method.Body.Instructions.ToList().FindIndex(instruction=>instruction.Operand is MethodReference call&&call.Name=="ValidateReplayMap")<
                method.Body.Instructions.ToList().FindIndex(instruction=>instruction.Operand is MethodReference call&&call.Name=="RunAsync")));
        Check("recording resolver reads loaded terrain and independently checks editor fallback",Calls(Body("GetRecordingMapKey"),"NuclearOption.SceneLoading.MapLoader","get_CurrentMap")&&
            Calls(Body("GetRecordingMapKey"),"MissionManager","get_CurrentMission")&&
            Calls(Body("GetRecordingMapKey"),"ReplayMod.Core.ReplayMapIdentity","TrySelectRecordingMap")&&CallsAny("NuclearOption.SceneLoading.MapLoader","CanLoad"));
        var recorder=mod.MainModule.Types.Single(type=>type.FullName=="ReplayMod.Core.Recorder");
        Check("recording header captures explicit resolved terrain",All(recorder).SelectMany(type=>type.Methods).Any(method=>Calls(method,scene.FullName,"GetRecordingMapKey")));
        Check("startup reports native disconnect reason and clears stale previous reason",CallsAny("GameManager","ClearDisconnectReason")&&
            Calls(Body("MapLoadFailure"),"ReplayMod.Core.ReplayMapIdentity","LoadFailure")&&
            Body("MapLoadFailure")?.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field&&field.DeclaringType.FullName=="DisconnectInfo"&&field.Name=="Message")==true);
        Check("native mission load failures stop host and explain main-menu symptom",Calls(Native("MissionManager","StartMission"),"NuclearOption.Networking.NetworkManagerNuclearOption","Stop")&&
            Native("MissionManager","StartMission")?.Body.ExceptionHandlers.Any(handler=>handler.CatchType?.Name=="MissionLoadException")==true);
        Console.WriteLine($"Editor replay scene API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> All(TypeDefinition type)
    {yield return type;foreach(var nested in type.NestedTypes)foreach(var child in All(nested))yield return child;}
}
