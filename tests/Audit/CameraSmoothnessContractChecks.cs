using Mono.Cecil;

public static class CameraSmoothnessContractChecks
{
    public static (int Checks,int Failures) Run(AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"camera smoothness "+name);}
        var types=AllTypes(mod.MainModule.Types).ToDictionary(type=>type.FullName);
        const string director="ReplayMod.Director.ReplayDirector",model="ReplayMod.Director.DirectorProject",ui="ReplayMod.UI.TrailerSuiteUi";
        MethodDefinition? Body(string type,string name)=>types.TryGetValue(type,out var definition)?definition.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string method)=>body?.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName==type&&call.Name==method)==true;
        bool Field(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field&&field.DeclaringType.FullName==type&&field.Name==name)==true;
        Check("project owns bounded saved strength with a usable old-project default",types[model].Fields.Any(field=>field.Name=="cameraPathSmoothness"&&field.FieldType.FullName=="System.Single"&&field.IsPublic)&&
            Field(Body(model,"Validate"),model,"cameraPathSmoothness")&&Body(model,".ctor")?.Body.Instructions.Any(instruction=>instruction.Operand is float value&&Math.Abs(value-.65f)<.000001)==true);
        bool SamplesStrength(string method)=>Calls(Body(director,method),director,"get_PathSmoothness")&&Body(director,method)?.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName=="ReplayMod.Director.CameraPathSampler"&&call.Name=="TrySample"&&call.Parameters.Count==6)==true;
        Check("render and path guides share the same strength and replay-time sampler",SamplesStrength("UpdateCamera")&&SamplesStrength("SampleCameraPath"));
        var uiMethods=types.Values.Where(type=>type.FullName==ui||type.FullName.StartsWith(ui+"/",StringComparison.Ordinal)).SelectMany(type=>type.Methods).Where(method=>method.HasBody);
        Check("compact path slider reaches the setter and is disabled with curves off",Calls(Body(ui,"CameraPathMenu"),director,"get_PathSmoothness")&&
            Body(ui,"CameraPathMenu")?.Body.Instructions.Any(instruction=>instruction.Operand is string text&&text=="Path smoothness")==true&&
            uiMethods.Any(method=>Calls(method,director,"SetPathSmoothness"))&&Field(Body(ui,"Update"),ui,"pathSmoothnessSlider")&&Calls(Body(ui,"Update"),director,"get_PathSmooth")&&Calls(Body(ui,"Update"),"UnityEngine.UI.Selectable","set_interactable"));
        Check("strength edits reset camera state and support logs report the value",Calls(Body(director,"SetPathSmoothness"),director,"CancelCameraTake")&&Calls(Body(director,"SetPathSmoothness"),director,"ResetCameraSmoothing")&&
            Calls(Body(director,"SetPathSmoothness"),model,"Finite")&&Calls(Body(director,"SetPathSmoothness"),"UnityEngine.Mathf","Clamp")&&
            types.Values.Where(type=>type.FullName.StartsWith("ReplayMod.Diagnostics.ReplayDiagnostics",StringComparison.Ordinal)).SelectMany(type=>type.Methods).Any(method=>Calls(method,director,"get_PathSmoothness")));
        Console.WriteLine($"Camera smoothness compiled-contract audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
    {foreach(var type in roots){yield return type;foreach(var nested in AllTypes(type.NestedTypes))yield return nested;}}
}
