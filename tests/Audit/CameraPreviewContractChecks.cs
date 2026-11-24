using Mono.Cecil;

public static class CameraPreviewContractChecks
{
    public static (int Checks,int Failures) Run(AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"camera previews "+name);}
        var types=AllTypes(mod.MainModule.Types).ToDictionary(type=>type.FullName);
        MethodDefinition? Body(string type,string name)=>types.TryGetValue(type,out var definition)?definition.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method&&method.DeclaringType.GetElementType().FullName==type&&method.Name==name)==true;
        const string preview="ReplayMod.Camera.ReplayCameraPreview",policy="ReplayMod.Camera.CameraPreviewVisibility`1",manager="ReplayMod.Camera.CameraManager",trail="ReplayMod.Camera.VisualWaypoints",segment="ReplayMod.Camera.SplineSegment",waypoint="ReplayMod.Camera.CameraWaypoint";
        var previewBodies=types.Values.Where(type=>type.FullName==preview||type.FullName.StartsWith(preview+"/",StringComparison.Ordinal)).SelectMany(type=>type.Methods.Where(method=>method.HasBody)).ToArray();
        Check("all legacy sphere and line creation registers exact ownership",
            Calls(Body(waypoint,"Create"),preview,"Attach")&&Calls(Body(segment,"CreateLine"),preview,"Attach")&&
            Calls(Body(trail,"CreateWaypoint3DMarker"),preview,"Attach")&&Calls(Body(trail,"CreateLine"),preview,"Attach"));
        Check("native default and Sprite materials are replaced by supported URP shaders",
            Calls(Body(preview,"FindSupportedShader"),"UnityEngine.Shader","Find")&&Calls(Body(preview,"FindSupportedShader"),"UnityEngine.Shader","get_isSupported")&&
            Body(preview,"FindSupportedShader")?.Body.Instructions.Count(instruction=>instruction.Operand is string name&&name.StartsWith("Universal Render Pipeline/",StringComparison.Ordinal))==3&&
            Calls(Body(preview,"Attach"),"UnityEngine.Renderer","set_sharedMaterial")&&
            new[]{waypoint,trail,segment}.All(type=>types[type].Methods.Where(method=>method.HasBody).All(method=>method.Body.Instructions.All(instruction=>instruction.Operand is not string text||text!="Sprites/Default"))));
        Check("owned material instances are disposed at object destruction",Calls(Body(preview,"OnDestroy"),"UnityEngine.Object","Destroy")&&
            Body(preview,"OnDestroy")?.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field&&field.Name=="ownedMaterial")==true);
        Check("render requests and capture requests use independent pure state",Calls(Body(preview,"SetVisible"),policy,"SetRequested")&&
            Calls(Body(preview,"SetCaptureHidden"),policy,"SetCaptureHidden")&&Calls(Body(preview,"ApplyVisibility"),policy,"IsVisible")&&
            Calls(Body(preview,"Attach"),policy,"Register"));
        Check("new missing-shader previews fail closed",Body(preview,"ApplyVisibility")?.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field&&field.Name=="ready")==true&&
            Calls(Body(preview,"ApplyVisibility"),"UnityEngine.Renderer","set_enabled")&&Calls(Body(preview,"ApplyVisibility"),"UnityEngine.GameObject","SetActive"));
        Check("owned sphere and line render toggles cannot bypass capture state",Calls(Body(waypoint,"SetRenderActive"),preview,"SetVisible")&&
            Calls(Body(segment,"SetRenderActive"),preview,"SetVisible")&&Calls(Body(trail,"SetRenderActive"),preview,"SetVisible"));
        Check("capture covers registry objects beyond old editor collections",Calls(Body(manager,"CaptureEditorVisibility"),preview,"SetCaptureHidden")&&
            Calls(Body(trail,"SetCaptureHidden"),preview,"SetCaptureHidden"));
        Check("spline and trail reset and teardown release owned old previews",Calls(Body(manager,"Restore"),preview,"DestroyOwned")&&
            Calls(Body(manager,"ClearPath"),preview,"DestroyOwned")&&Calls(Body(trail,"Restore"),preview,"DestroyOwned")&&
            Calls(Body(trail,"Reset"),preview,"DestroyOwned")&&Calls(Body(trail,"OnDestroy"),preview,"DestroyOwned"));
        Check("cleanup filters exact preview family before deferred destruction",Body(preview,"DestroyOwned")?.Body.Instructions.Any(instruction=>
            instruction.Operand is FieldReference field&&field.Name=="kind")==true&&Calls(Body(preview,"DestroyOwned"),"UnityEngine.GameObject","SetActive")&&
            Calls(Body(preview,"DestroyOwned"),policy,"Forget")&&Calls(Body(preview,"DestroyOwned"),"UnityEngine.Object","Destroy"));
        Check("flight stop and import retain requested editor visibility",Calls(Body(manager,"StopCamFlight"),manager,"ApplyEditorVisibility")&&
            Calls(Body(manager,"ImportPath"),manager,"ApplyEditorVisibility")&&Calls(Body(manager,"DrivePath"),manager,"ApplyEditorVisibility")&&
            Body(manager,"ApplyEditorVisibility")?.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field&&field.Name=="_editorVisible")==true);
        Check("preview ownership performs no global scan, native actor writes or shared-material mutation",previewBodies.Length>0&&previewBodies.All(body=>body.Body.Instructions.All(instruction=>
            instruction.Operand is not MethodReference method||method.Name is not("FindObjectsOfType" or "FindObjectsByType" or "SetPositionAndRotation" or "GetComponentsInChildren")&&
            method.DeclaringType.FullName is not("Aircraft" or "Unit" or "Pilot" or "UnityEngine.QualitySettings" or "UnityEngine.PlayerPrefs")&&
            !(method.DeclaringType.FullName=="UnityEngine.Renderer"&&method.Name=="get_material"))));
        Console.WriteLine($"Camera preview compiled-contract audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {foreach(var type in types){yield return type;foreach(var child in AllTypes(type.NestedTypes))yield return child;}}
}
