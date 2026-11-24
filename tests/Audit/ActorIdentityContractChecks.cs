using Mono.Cecil;
using Mono.Cecil.Cil;

public static class ActorIdentityContractChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"actor identity "+name);}
        bool NativeField(string owner,string name,string value,bool isPrivate)=>types.TryGetValue(owner,out var type)&&
            type.Fields.Count(field=>field.Name==name&&!field.IsStatic&&field.FieldType.FullName==value&&(!isPrivate||field.IsPrivate))==1;
        bool NativeMethod(string owner,string name,string result,params string[] parameters)=>types.TryGetValue(owner,out var type)&&
            type.Methods.Count(method=>method.Name==name&&!method.IsStatic&&method.ReturnType.FullName==result&&
                method.Parameters.Select(parameter=>parameter.ParameterType.FullName).SequenceEqual(parameters))==1;
        const string networkPlayer="NuclearOption.Networking.Player",nativeName="NuclearOption.Networking.PlayerName";
        Check("native current aircraft and player references",NativeMethod("Aircraft","get_Player",networkPlayer)&&NativeMethod(networkPlayer,"get_Aircraft","Aircraft"));
        Check("native existing private name cache",NativeField(networkPlayer,"_playerNameCache",nativeName,true));
        Check("native filtered Other-context display text",NativeMethod(nativeName,"GetDisplayName","System.String","NuclearOption.Networking.PlayerNameContext")&&
            types.TryGetValue("NuclearOption.Networking.PlayerNameContext",out var context)&&context.Fields.Any(field=>field.Name=="Other"&&field.HasConstant&&(int)field.Constant==1));
        Check("native unit callsign field",NativeField("Unit","UniqueName","System.String",false));
        var compiled=AllTypes(mod.MainModule.Types).ToDictionary(type=>type.FullName);
        MethodDefinition? Body(string owner,string name)=>compiled.TryGetValue(owner,out var type)?type.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody):null;
        bool Calls(MethodDefinition? body,string owner,string name)=>body?.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method&&
            method.DeclaringType.GetElementType().FullName==owner&&method.Name==name)==true;
        bool Field(MethodDefinition? body,string owner,string name)=>body?.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field&&
            field.DeclaringType.GetElementType().FullName==owner&&field.Name==name)==true;
        bool ExactUnitReference(MethodDefinition? body)
        {
            if(body==null)return false;
            if(Calls(body,"System.Object","ReferenceEquals"))return true;
            // Roslyn lowers ReferenceEquals(current, unit) to a reference ceq in
            // Release. Require the actual Unit local and method argument, not
            // just an arbitrary equality instruction elsewhere in the method.
            bool UnitLocal(Instruction instruction)
            {
                int index=instruction.OpCode.Code switch{Code.Ldloc_0=>0,Code.Ldloc_1=>1,Code.Ldloc_2=>2,Code.Ldloc_3=>3,_=>-1};
                var local=index>=0&&index<body.Body.Variables.Count?body.Body.Variables[index]:
                    instruction.OpCode.Code is Code.Ldloc or Code.Ldloc_S?instruction.Operand as VariableDefinition:null;
                return local?.VariableType.FullName=="Unit";
            }
            return body.Body.Instructions.Any(instruction=>instruction.OpCode.Code==Code.Ceq&&instruction.Previous?.OpCode.Code==Code.Ldarg_1&&
                instruction.Previous.Previous!=null&&UnitLocal(instruction.Previous.Previous)&&instruction.Next?.OpCode.Code==Code.Ret);
        }
        const string recorder="ReplayMod.Core.Recorder",controller="ReplayMod.Core.UnitController",player="ReplayMod.Core.Player",
            data="ReplayMod.Data.ActorIdentitySnapshot",cache="ReplayMod.Data.ActorIdentityCache",value="ReplayMod.Events.ConcreteEvents.ActorIdentityEvent",
            text="ReplayMod.Data.ActorIdentityText",director="ReplayMod.Director.ReplayDirector",policy="ReplayMod.Director.ActorCaptionPolicy",ui="ReplayMod.UI.TrailerSuiteUi";
        var capture=Body(recorder,"RecordActorIdentity");
        Check("compiled capture reads cached filtered name without Steam/auth fetch",Field(capture,networkPlayer,"_playerNameCache")&&
            Calls(capture,nativeName,"GetDisplayName")&&Calls(capture,networkPlayer,"get_Aircraft")&&
            !Calls(capture,networkPlayer,"GetDisplayName")&&!Calls(capture,networkPlayer,"GetPlayerName")&&
            capture?.Body.Instructions.All(instruction=>instruction.Operand is not MethodReference call||
                !call.DeclaringType.FullName.Contains("Steam",StringComparison.Ordinal)&&call.Name!="GetAuthData")==true);
        Check("compiled spawn capture and cached changed-only half-second polling",Calls(Body(recorder,"RegisterUnitWhenReady"),recorder,"RecordActorIdentity")&&
            Calls(capture,data,"Same")&&Calls(capture,recorder,"IsTracked")&&Calls(capture,recorder,"get_SessionValid")&&
            Calls(Body(recorder,"LateUpdate"),recorder,"CaptureActorIdentitiesLate")&&
            Body(recorder,"CaptureActorIdentitiesLate")?.Body.Instructions.Any(instruction=>instruction.Operand is double interval&&interval==.5)==true&&
            !Calls(Body(recorder,"CaptureActorIdentitiesLate"),"UnityEngine.Object","FindObjectsOfType"));
        Check("compiled name payload remains bounded plain text without account fields",compiled.TryGetValue(value,out var eventType)&&
            eventType.Fields.Where(field=>!field.IsStatic).All(field=>!field.Name.Contains("Steam",StringComparison.OrdinalIgnoreCase)&&!field.Name.Contains("account",StringComparison.OrdinalIgnoreCase))&&
            Calls(Body(value,"HasValidPayload"),text,"Valid")&&Calls(Body(value,"Read"),value,"Validate")&&Calls(Body(value,"Write"),value,"Validate"));
        Check("compiled actor lifetime controls current identity and native-to-recorded mapping",compiled.TryGetValue(controller,out var controllerType)&&
            controllerType.Methods.Any(method=>method.Name=="Execute"&&method.Parameters.Count==1&&method.Parameters[0].ParameterType.FullName==value&&Calls(method,cache,"Apply"))&&
            Calls(Body(controller,"GetActorIdentity"),controller,"FindActor")&&ExactUnitReference(Body(controller,"TryRecordedId"))&&
            Calls(Body(controller,"TryRecordedId"),controller,"TryGetUnit")&&Field(Body(controller,"TryRecordedId"),controller,"_reverseUnitMap")&&
            Calls(Body(controller,"DespawnUnit"),cache,"Forget")&&Calls(Body(controller,"ReleaseOwnedPresentationObjects"),cache,"Clear"));
        Check("compiled playback exposes recorded identity without native name lookup",Calls(Body(player,"GetActorIdentity"),controller,"GetActorIdentity")&&
            Calls(Body(player,"TryRecordedId"),controller,"TryRecordedId"));
        Check("compiled caption follows driven or native aircraft and rejects Free selection",Calls(Body(director,"TryGetCaptionActor"),policy,"Ready")&&
            Calls(Body(director,"TryGetCaptionActor"),policy,"TryAttachedTarget")&&Calls(Body(director,"TryGetCaptionActor"),player,"TryRecordedId")&&
            Field(Body(director,"TryGetCaptionActor"),"CameraStateManager","followingUnit")&&
            Body(director,"TryGetCaptionActor")?.Body.Instructions.Any(instruction=>instruction.OpCode.Code==Code.Isinst&&
                instruction.Operand is TypeReference type&&type.FullName=="CameraFreeState")==true);
        Check("compiled caption uses owned plain label and no menu rebuild or scene scan",Calls(Body(ui,"RefreshIdentityCaption"),director,"TryGetCaptionActor")&&
            Calls(Body(ui,"RefreshIdentityCaption"),player,"GetActorIdentity")&&Calls(Body(ui,"RefreshIdentityCaption"),text,"Caption")&&
            Calls(Body(ui,"BuildIdentityCaption"),"TMPro.TMP_Text","set_richText")&&
            Body(ui,"RefreshIdentityCaption")?.Body.Instructions.All(instruction=>instruction.Operand is not MethodReference call||
                call.Name is not("RebuildMenu" or "FindObjectsOfType" or "FindObjectsByType"))==true);
        Check("compiled caption render subscription releases with UI destruction",Calls(Body(ui,"BuildIdentityCaption"),"UnityEngine.Canvas","add_willRenderCanvases")&&
            Calls(Body(ui,"OnDestroy"),"UnityEngine.Canvas","remove_willRenderCanvases"));
        Check("compiled target picker includes recorded names and project default survives older JSON",Calls(Body(ui,"ActorName"),player,"GetActorIdentity")&&
            Calls(Body(director,"Load"),"ReplayMod.Director.ProjectJsonCodec","Read")&&Calls(Body("ReplayMod.Director.ProjectJsonCodec","Read"),"Newtonsoft.Json.JsonConvert","PopulateObject")&&
            compiled.TryGetValue("ReplayMod.Director.DirectorProject",out var project)&&project.Fields.Any(field=>field.Name=="showPlayerNames"&&field.FieldType.FullName=="System.Boolean"));
        Console.WriteLine($"Actor identity API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
    {foreach(var type in roots){yield return type;foreach(var nested in AllTypes(type.NestedTypes))yield return nested;}}
}
