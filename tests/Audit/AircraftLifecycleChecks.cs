using Mono.Cecil;

public static class AircraftLifecycleChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod,string managedDirectory)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"aircraft lifecycle "+name);}
        bool Fields(string type,params (string Name,string Type)[] fields)=>types.TryGetValue(type,out var definition)&&fields.All(expected=>definition.Fields.Any(f=>f.Name==expected.Name&&!f.IsStatic&&f.FieldType.FullName==expected.Type));
        bool Method(string type,string name,string result,params string[] parameters)=>types.TryGetValue(type,out var definition)&&definition.Methods.Count(m=>m.Name==name&&!m.IsStatic&&m.ReturnType.FullName==result&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(parameters))==1;
        MethodDefinition? NativeBody(string type,string name)=>types.TryGetValue(type,out var definition)?definition.Methods.FirstOrDefault(m=>m.Name==name&&m.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string method)=>body?.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==method)==true;
        Check("stable unit-part identities and physical body references",Fields("UnitPart",("id","System.Byte"),("parentUnit","Unit"),("rb","UnityEngine.Rigidbody"),("attachInfo","UnitPart/AttachInfo"),("fragmented","System.Boolean")));
        Check("native attachment and detachment states",Fields("UnitPart/AttachInfo",("parentPart","UnitPart"),("detachedFromParentPart","System.Boolean"))&&Method("UnitPart","IsDetached","System.Boolean"));
        Check("native direct detach and observed fragmentation contracts",Method("UnitPart","Detach","System.Void","UnityEngine.Vector3","UnityEngine.Vector3")&&Method("UnitPart","SpawnFragments","System.Void")&&Method("AeroPart","SpawnFragments","System.Void"));
        Check("native received aircraft detach reaches base observation",Calls(NativeBody("AeroPart","Detach"),"UnitPart","Detach")&&types.TryGetValue("Unit",out var unit)&&unit.Methods.Any(m=>m.Name.StartsWith("UserCode_RpcBreakPart_",StringComparison.Ordinal)&&m.HasBody&&Calls(m,"IDamageable","Detach")&&!Calls(m,"Unit","DetachPart")));
        Check("native disable does not constitute object destruction",Calls(NativeBody("Unit","OnDestroy"),"UnitRegistry","UnregisterUnit")&&!Calls(NativeBody("Unit","UnitDisabled"),"UnitRegistry","UnregisterUnit"));
        Check("native damage callback still captures received HP",Calls(NativeBody("UnitPart","ApplyDamage"),"UnitPart","InvokeDamage"));
        Check("native aircraft ejection and part container contract",Fields("Aircraft",("cockpit","UnitPart"))&&Fields("Unit",("partLookup","System.Collections.Generic.List`1<UnitPart>"))&&Method("Aircraft","HasEjected","System.Boolean"));
        Check("native capsule duplicate-pilot guard contract",Fields("EscapeCapsule",("part","AeroPart"))&&Method("EscapeCapsule","SpawnDisembarkingPilot","System.Void","System.Int32"));
        Check("native peer pose interpolation runs during LateUpdate",Calls(NativeBody("NuclearOption.NetworkTransforms.SendTransformBatcher","LateUpdate"),"NuclearOption.NetworkTransforms.SendTransformBatcher","ClientUpdate")&&
            Calls(NativeBody("NuclearOption.NetworkTransforms.SendTransformBatcher","ClientUpdate"),"NuclearOption.NetworkTransforms.SendTransformBatcher","VisualUpdate")&&
            Calls(NativeBody("NuclearOption.NetworkTransforms.AircraftNetworkTransform","VisualUpdate"),"NuclearOption.NetworkTransforms.AircraftNetworkTransform","ApplySnapshot"));
        var compiled=AllTypes(mod.MainModule.Types).ToDictionary(t=>t.FullName);
        MethodDefinition? Body(string type,string method)=>compiled.TryGetValue(type,out var definition)?definition.Methods.FirstOrDefault(m=>m.Name==method&&m.HasBody):null;
        var pilotSetup=NativeBody("PilotDismounted","Setup");
        var setupState=pilotSetup?.CustomAttributes.FirstOrDefault(attribute=>attribute.AttributeType.FullName=="System.Runtime.CompilerServices.AsyncStateMachineAttribute")?.ConstructorArguments.FirstOrDefault().Value as TypeReference;
        var continuation=setupState!=null?NativeBody(setupState.FullName,"MoveNext"):null;
        Check("native pilot setup yields once before its unchecked object access",pilotSetup?.ReturnType.FullName=="Cysharp.Threading.Tasks.UniTask"&&continuation!=null&&
            continuation.Body.Instructions.Count(instruction=>instruction.Operand is MethodReference method&&method.DeclaringType.FullName=="Cysharp.Threading.Tasks.YieldAwaitable/Awaiter"&&method.Name=="GetResult")==1&&
            Calls(continuation,"UnityEngine.Component","get_gameObject")&&types[setupState!.FullName].Fields.Any(field=>field.Name=="<>4__this"&&field.FieldType.FullName=="PilotDismounted"));
        const string setupLifetime="ReplayMod.Patches.ReplayPilotSetupLifetime";
        var alive=Body(setupLifetime,"RequireAlive");
        Check("compiled pilot setup lifetime is weak and captured only at replay startup",Calls(Body("ReplayMod.Patches.ReplayPilotSetupStartPatch","Prefix"),setupLifetime,"Observe")&&
            Calls(Body(setupLifetime,"Observe"),"ReplayMod.Core.ReplayRuntime","get_IsPlayback")&&compiled.TryGetValue(setupLifetime,out var setupOwnership)&&setupOwnership.Fields.Any(field=>field.FieldType.GetElementType().FullName=="System.Runtime.CompilerServices.ConditionalWeakTable`2"));
        Check("destroyed replay pilot continuation cancels without swallowing other failures",alive!=null&&alive.Body.ExceptionHandlers.Count==0&&Calls(alive,"UnityEngine.Object","op_Equality")&&
            alive.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method&&method.DeclaringType.GetElementType().FullName=="System.Runtime.CompilerServices.ConditionalWeakTable`2"&&method.Name=="TryGetValue")&&
            alive.Body.Instructions.Any(instruction=>instruction.OpCode.Code==Mono.Cecil.Cil.Code.Newobj&&instruction.Operand is MethodReference method&&method.DeclaringType.FullName=="System.OperationCanceledException")&&
            !Calls(alive,"Unit","SetLocalSim")&&!Calls(alive,"Unit","SetRB"));
        const string recorder="ReplayMod.Core.Recorder",controller="ReplayMod.Core.UnitController",player="ReplayMod.Core.Player";
        Check("compiled source records received detach with lifetime deduplication",Calls(Body("ReplayMod.Patches.ReplayReceivedPartDetachPatch","Prefix"),recorder,"RecordReceivedPartDetach")&&
            Calls(Body(recorder,"HandlePartDetach"),recorder,"NoteObservedPartDetach")&&Calls(Body(recorder,"NoteObservedPartDetach"),"ReplayMod.Data.ReplayPartDetachTracker","Observe"));
        Check("compiled aircraft world poses sampled after native presentation",Calls(Body(recorder,"LateUpdate"),recorder,"RecordObservedAircraftMotion")&&
            Calls(Body(recorder,"RecordObservedAircraftMotion"),recorder,"QueueObservedAircraftPose")&&Calls(Body(recorder,"RecordObservedAircraftMotion"),recorder,"RecordAircraftPartMotion")&&
            compiled.TryGetValue(recorder,out var recorderType)&&recorderType.CustomAttributes.Any(a=>a.AttributeType.FullName=="UnityEngine.DefaultExecutionOrder"&&(int)a.ConstructorArguments[0].Value>=10000));
        var removal=Body(recorder,"HandleUnitUnregister");
        var removalCalls=removal?.Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>(MethodReference)i.Operand).ToArray()??Array.Empty<MethodReference>();
        int final=Array.FindIndex(removalCalls,m=>m.Name=="RecordFinalAircraftMotion"),forget=Array.FindIndex(removalCalls,m=>m.Name=="ForgetRecordingAircraftLifecycle");
        Check("compiled final observed motion precedes actor lifetime release",final>=0&&forget>final&&Calls(removal,recorder,"QueueEvent"));
        Check("compiled playback uses direct observed detach instead of network command",Calls(Body(controller,"Execute"),controller,"ApplyRecordedPartDetach")||
            compiled[controller].Methods.Where(m=>m.Name=="Execute"&&m.HasBody).Any(m=>Calls(m,controller,"ApplyRecordedPartDetach")));
        var detach=Body(controller,"DetachRecordedPart");
        Check("compiled direct detach invokes receiver without transmitting commands",Calls(Body(controller,"ApplyRecordedPartDetach"),controller,"DetachRecordedPart")&&Calls(Body(controller,"RefreshRecordedAircraftParts"),controller,"DetachRecordedPart")&&Calls(detach,"UnitPart","Detach")&&!Calls(detach,"Unit","DetachPart"));
        Check("compiled replay detachment accounts for simple physics and abandoned joint bodies",detach?.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="simplePhysics"&&f.DeclaringType.Name=="AeroPart")==true&&Calls(detach,"UnityEngine.Object","DestroyImmediate")&&Calls(detach,"AeroPart","CreateRB"));
        var lateCalls=Body(player,"RefreshAircraftVisualsLate")?.Body.Instructions.Where(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==controller).Select(i=>((MethodReference)i.Operand).Name).ToArray()??Array.Empty<string>();
        Check("compiled final world parts wrap geometry before flame refresh after peer interpolation",lateCalls.SequenceEqual(new[]{"RefreshRecordedUnitPoses","RefreshRecordedAircraftParts","RefreshAircraftVisualsLate","RefreshRecordedAircraftParts","RefreshFuelFlames"}));
        Check("compiled breakup geometry has an explicit recorded-fragment scope",Calls(Body(controller,"RefreshRecordedAircraftParts"),"ReplayMod.Core.ReplayPartFragmentScope","Enter")&&
            Calls(Body("ReplayMod.Patches.ReplayRecordedPartFragmentsPatch","Prefix"),"ReplayMod.Core.ReplayPartFragmentScope","get_IsActive")&&Calls(Body("ReplayMod.Patches.ReplayRecordedAeroFragmentsPatch","Prefix"),"ReplayMod.Core.ReplayPartFragmentScope","get_IsActive"));
        Check("compiled aircraft lifetime clears detached owned presentation",Calls(Body(controller,"DespawnUnit"),controller,"ForgetAircraftPartMotion")&&Calls(Body(controller,"ReleaseOwnedPresentationObjects"),controller,"ClearAircraftPartMotion"));
        Check("compiled final pose pass uses cached actors without scene discovery",new[]{"RefreshRecordedUnitPoses","RefreshRecordedAircraftParts"}.All(name=>Body(controller,name)?.Body.Instructions.All(i=>i.Operand is not MethodReference m||m.Name is not("FindObjectsByType" or "FindObjectsOfType" or "GetComponentsInChildren"))==true));
        Console.WriteLine($"Aircraft lifecycle API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {foreach(var type in types){yield return type;foreach(var child in AllTypes(type.NestedTypes))yield return child;}}
}
