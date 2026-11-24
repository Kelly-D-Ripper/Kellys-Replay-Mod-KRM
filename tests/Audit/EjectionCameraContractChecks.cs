using Mono.Cecil;

public static class EjectionCameraContractChecks
{
    public static (int Checks,int Failures) Run(AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"ejection/FOV "+name);}
        var types=All(mod.MainModule.Types).ToDictionary(type=>type.FullName);
        MethodDefinition? Body(string type,string name)=>types.TryGetValue(type,out var t)?t.Methods.FirstOrDefault(m=>m.Name==name&&m.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name)==true;
        bool Uses(MethodDefinition? body,string type)=>body?.Body.Instructions.Any(i=>i.Operand is TypeReference t&&t.FullName==type)==true;
        const string core="ReplayMod.Core.",director="ReplayMod.Director.ReplayDirector",eventType="ReplayMod.Events.ConcreteEvents.PilotEjectionStateEvent",ui="ReplayMod.UI.TrailerSuiteUi";
        Check("new format advertises unfamiliar presentation events",types[core+"ReplayRecordingHeader"].Fields.Any(f=>f.Name=="CurrentFormat"&&Convert.ToInt32(f.Constant)>=9)&&types["ReplayMod.Events.EventType"].Fields.Any(f=>f.Name=="PilotEjectionState"&&Convert.ToInt32(f.Constant)==34));
        Check("late native capture publishes recorded eyes and aircraft lifetime",Calls(Body(core+"Recorder","LateUpdate"),core+"Recorder","CaptureEjectionEyesLate")&&Calls(Body(core+"Recorder","RegisterUnitWhenReady"),core+"Recorder","NoteEjectionCaptureSpawn")&&Uses(Body(core+"Recorder","NoteEjectionCaptureSpawn"),"PilotDismounted")&&Calls(Body(core+"Recorder","CaptureEjectionEyesLate"),eventType,"HasValidPayload"));
        Check("eye samples have an interpolation owner and lifecycle fences",Uses(Body(core+"BoundedEventBuffer","IsInterpolation"),eventType)&&Uses(Body(core+"BoundedEventBuffer","SampleActor"),eventType)&&Uses(Body(core+"ReplaySeekState","Add"),eventType));
        Check("rolling cut rebases new nested clocks",Calls(Body(core+"RollingReplayBuffer","Snapshot"),eventType,"Rebase")&&Calls(Body(core+"RollingReplayBuffer","Snapshot"),core+"RollingReplayBuffer","MarkOrphanedEyeAtCut")&&Calls(Body(core+"RollingReplayBuffer","Snapshot"),"ReplayMod.Events.ConcreteEvents.BuildingStateEvent","Rebase"));
        Check("cockpit camera reads exact replay eye track before normal rig",Calls(Body(director,"UpdateCamera"),director,"TryApplyEjectionCamera")&&Calls(Body(director,"TryApplyEjectionCamera"),core+"Player","TryEjectionEye")&&Calls(Body(director,"TryApplyEjectionCamera"),core+"Player","TryActorBirth"));
        Check("followed body suppression is restored on camera release",Calls(Body(director,"TryApplyEjectionCamera"),"ReplayMod.Core.ReplayInteriorPresentation","Clear")&&Calls(Body(director,"TryApplyEjectionCamera"),director,"EjectionInteriorPresent")&&Calls(Body(director,"RestoreEjectionCamera"),director,"EjectionInteriorPresent")&&!Calls(Body(director,"EjectionInteriorPresent"),"UnitPart","IsDetached")&&Body(director,"EjectionInteriorPresent")?.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="detachedFromParentPart")==true&&Calls(Body(director,"TryApplyEjectionCamera"),"UnityEngine.Renderer","set_forceRenderingOff")&&Calls(Body(director,"RestoreEjectionCamera"),"UnityEngine.Renderer","set_forceRenderingOff")&&Calls(Body(director,"ReleaseCamera"),director,"RestoreEjectionCamera"));
        Check("manual zoom no longer writes path-disabled flag",Body(director,"SetCameraFov")?.Body.Instructions.All(i=>i.OpCode.Code!=Mono.Cecil.Cil.Code.Stfld||i.Operand is not FieldReference f||f.Name!="CameraKeysEnabled")==true&&Body(director,"SetCameraFov")?.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="animatePathFov")==true);
        Check("path menu exposes lens and saved zoom animation directly",Calls(Body(ui,"CameraPathMenu"),director,"get_CurrentCameraFov")&&Calls(Body(ui,"CameraPathMenu"),director,"get_PathSmoothness")&&types.Values.Where(t=>t.FullName.StartsWith(ui,StringComparison.Ordinal)).SelectMany(t=>t.Methods).Any(m=>Calls(m,director,"SetPathAnimatesFov"))&&types["ReplayMod.Director.DirectorProject"].Fields.Any(f=>f.Name=="firstPersonEjection"));
        Console.WriteLine($"Ejection camera/path FOV API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> roots){foreach(var type in roots){yield return type;foreach(var nested in All(type.NestedTypes))yield return nested;}}
}
