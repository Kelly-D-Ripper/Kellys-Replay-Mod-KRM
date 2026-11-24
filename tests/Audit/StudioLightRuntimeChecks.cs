using Mono.Cecil;
using Mono.Cecil.Cil;

public static class StudioLightRuntimeChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> native,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"studio lights "+name);}
        var compiled=AllTypes(mod.MainModule.Types).ToDictionary(type=>type.FullName);
        MethodDefinition? Body(string type,string name)=>compiled.TryGetValue(type,out var definition)?definition.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method&&method.DeclaringType.GetElementType().FullName==type&&method.Name==name)==true;
        bool Field(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field&&field.DeclaringType.GetElementType().FullName==type&&field.Name==name)==true;
        const string runtime="ReplayMod.Core.ReplayStudioLights",director="ReplayMod.Director.ReplayDirector",caps="ReplayMod.Core.ReplayStudioLightCapabilities",cookie="ReplayMod.Core.ReplayStudioLightCookie";
        var ownedTypes=new[]{runtime,caps,cookie};
        var bodies=compiled.Values.Where(type=>ownedTypes.Any(name=>type.FullName==name||type.FullName.StartsWith(name+"/",StringComparison.Ordinal))).SelectMany(type=>type.Methods.Where(method=>method.HasBody)).ToArray();
        Check("native realtime light colour temperature beam range cookie and shadow setters",native.TryGetValue("UnityEngine.Light",out var light)&&
            new[]{"type","color","intensity","range","spotAngle","innerSpotAngle","cookie","useColorTemperature","colorTemperature","shadows","shadowStrength","shadowNearPlane"}.All(property=>light.Methods.Any(method=>method.Name=="set_"+property&&!method.IsStatic&&method.Parameters.Count==1)));
        Check("live and transition guards require exact offline playback lifecycle",Calls(Body(director,"get_CanRenderStudioLights"),"ReplayMod.Core.ReplayRuntime","get_IsPlayback")&&
            Calls(Body(director,"get_CanRenderStudioLights"),"ReplayMod.Core.Player","get_IsSeeking")&&Calls(Body(director,"get_CanRenderStudioLights"),"ReplayMod.Misc.SceneHelper","get_IsTransitionPending")&&
            Calls(Body(director,"get_CanRenderStudioLights"),"ReplayMod.Core.ReplayManager","get_IsTransitioning")&&Field(Body(director,"get_CanRenderStudioLights"),"GameManager","IsHeadless")&&Field(Body(director,"get_CanRenderStudioLights"),director,"replayPath"));
        Check("studio lifecycle releases lights on open load close disable destroy",new[]{"Open","Load","Close","OnDisable","OnDestroy"}.All(name=>
            Calls(Body(director,name),director,"ClearStudioLights")||name=="Open"&&compiled.Values.Where(type=>type.FullName.StartsWith(director+"/<Open>",StringComparison.Ordinal)).SelectMany(type=>type.Methods).Any(method=>Calls(method,director,"ClearStudioLights"))));
        Check("clean capture hides editor helpers while preserving light update",Calls(Body(director,"SetClean"),director,"UpdateStudioLights")&&Calls(Body(director,"get_CanEditStudioLights"),director,"get_Clean")&&
            Calls(Body(director,"UpdateStudioLights"),director,"get_CanRenderStudioLights")&&Calls(Body(director,"UpdateStudioLights"),director,"get_CanEditStudioLights")&&Calls(Body(director,"UpdateStudioLights"),runtime,"Update"));
        Check("seek disables owned lights and all editor drag state",Calls(Body(runtime,"Update"),runtime,"Hide")&&Calls(Body(runtime,"Hide"),runtime,"CancelDrag")&&Calls(Body(runtime,"Hide"),"UnityEngine.Behaviour","set_enabled")&&Calls(Body(runtime,"Hide"),"UnityEngine.GameObject","SetActive"));
        Check("additional light construction is owned and removes physics collider",Calls(Body(runtime,"Create"),"UnityEngine.GameObject","AddComponent")&&Calls(Body(runtime,"Create"),"UnityEngine.Collider","set_enabled")&&
            Calls(Body(runtime,"Create"),"UnityEngine.Object","Destroy")&&Calls(Body(runtime,"Create"),"UnityEngine.Object","set_hideFlags")&&Calls(Body(runtime,"Create"),"UnityEngine.Renderer","set_sharedMaterial"));
        Check("native pipeline helpers fail hidden rather than Standard and remain emissive at night",Calls(Body(runtime,"Create"),"UnityEngine.Shader","Find")&&Calls(Body(runtime,"Create"),"UnityEngine.Shader","get_isSupported")&&
            Body(runtime,"Create")?.Body.Instructions.Any(instruction=>instruction.Operand is string value&&value=="Universal Render Pipeline/Unlit")==true&&Calls(Body(runtime,"Apply"),"UnityEngine.Renderer","set_enabled")&&
            Calls(Body(runtime,"Apply"),"UnityEngine.Material","EnableKeyword")&&Body(runtime,"Apply")?.Body.Instructions.Any(instruction=>instruction.Operand is string value&&value=="_EmissionColor")==true);
        Check("grab selection uses owned box bounds and screen rays without game physics",Calls(Body(runtime,"Hit"),"UnityEngine.Bounds","IntersectRay")&&Calls(Body(runtime,"PointerCaptured"),"UnityEngine.Camera","ScreenPointToRay")&&
            bodies.All(body=>body.Body.Instructions.All(instruction=>instruction.Operand is not MethodReference call||call.DeclaringType.FullName!="UnityEngine.Physics")));
        Check("drag uses plane and native UI focus with floating-origin correction",Calls(Body(runtime,"UpdatePointer"),"UnityEngine.Plane","Raycast")&&Calls(Body(runtime,"UpdatePointer"),runtime,"UiBlocked")&&
            Calls(Body(runtime,"UiBlocked"),"ReplayMod.UI.TrailerSuiteUi","get_PointerOverUi")&&Calls(Body(runtime,"UiBlocked"),"ReplayMod.UI.TrailerSuiteUi","get_HasInputFocus")&&
            Field(Body(runtime,"UpdatePointer"),runtime,"dragOriginX")&&Calls(Body(runtime,"UpdatePointer"),"UnityEngine.Plane","set_distance"));
        Check("native free-camera and existing path editor respect marker input capture",Calls(Body("ReplayMod.MainMenu","get_IsPointerOverUI"),director,"get_StudioLightPointerCaptured")&&
            Calls(Body("ReplayMod.MainMenu","get_HasInputFocus"),director,"get_StudioLightDragging")&&Calls(Body("ReplayMod.Patches.NativeFreeCameraUiFocusPatch","Prefix"),"ReplayMod.MainMenu","get_IsPointerOverUI")&&
            Calls(Body("ReplayMod.Camera.CameraManager","HandleInputs"),"ReplayMod.MainMenu","get_IsPointerOverUI"));
        Check("world drag rejects native map pause and competing camera drivers",Calls(Body(director,"get_CanEditStudioLights"),"DynamicMap","get_mapMaximized")&&
            Calls(Body(director,"get_CanEditStudioLights"),"GameplayUI","get_GameIsPaused")&&Calls(Body(director,"get_StudioLightCanDrag"),director,"get_CanEditStudioLights")&&
            Field(Body(director,"get_StudioLightCanDrag"),"CameraStateManager","freeState")&&Field(Body(director,"get_StudioLightCanDrag"),"ReplayMod.Director.DirectorProject","rig")&&
            Field(Body(director,"get_StudioLightCanDrag"),director,"CameraKeysEnabled")&&Calls(Body(director,"get_StudioLightCanDrag"),director,"get_RailActive")&&
            Calls(Body(director,"get_StudioLightCanDrag"),"ReplayMod.Camera.CameraManager","get_IsFlying")&&Calls(Body(director,"get_StudioLightPointerCaptured"),director,"get_StudioLightCanDrag"));
        Check("native performance capability gates additional shadows and active lights",Calls(Body(runtime,"Apply"),caps,"get_DynamicLightsAvailable")&&Calls(Body(runtime,"Apply"),caps,"get_ShadowsAvailable")&&
            Calls(Body(runtime,"Apply"),caps,"get_SoftShadowsAvailable")&&Calls(Body(runtime,"Apply"),"UnityEngine.Light","set_shadows"));
        Check("renderer reflection reads current pipeline without global writes",Calls(Body(caps,"Refresh"),"UnityEngine.Rendering.GraphicsSettings","get_currentRenderPipeline")&&Calls(Body(caps,"Refresh"),"System.Reflection.PropertyInfo","GetValue")&&
            bodies.All(body=>body.Body.Instructions.All(instruction=>instruction.Operand is not MethodReference call||
                !(call.DeclaringType.FullName.StartsWith("UnityEngine.Rendering.",StringComparison.Ordinal)&&call.Name.StartsWith("set_",StringComparison.Ordinal))&&call.Name!="SetValue")));
        Check("spot beam profiles are procedural owned textures gated by native support",Calls(Body(runtime,"Apply"),cookie,"CookieFor")&&Calls(Body(runtime,"Apply"),"UnityEngine.Light","set_cookie")&&
            Calls(Body(cookie,"CookieFor"),caps,"get_CookiesAvailable")&&Calls(Body(cookie,"CookieFor"),"ReplayMod.Director.StudioLightBeamProfile","Rasterize")&&
            Calls(Body(cookie,"CookieFor"),"UnityEngine.Texture2D","LoadRawTextureData")&&Calls(Body(cookie,"CookieFor"),"UnityEngine.Texture2D","Apply"));
        Check("removed bindings immediately disable objects and free private masks materials",Calls(Body(runtime,"Dispose"),cookie,"Clear")&&Calls(Body(runtime,"Dispose"),"UnityEngine.GameObject","SetActive")&&
            Calls(Body(runtime,"Dispose"),"UnityEngine.Object","Destroy")&&Calls(Body(runtime,"Clear"),runtime,"Dispose")&&Calls(Body(cookie,"Clear"),"UnityEngine.Object","Destroy"));
        Check("runtime does not scan scene units lights resources or change native actors",bodies.Length>0&&bodies.All(body=>body.Body.Instructions.All(instruction=>
            instruction.Operand is not MethodReference call||call.Name is not("FindObjectsOfType" or "FindObjectsByType" or "FindObjectOfType" or "FindFirstObjectByType" or "Load" or "LoadAll" or "SetFollowingUnit" or "Initialize")&&
            call.DeclaringType.FullName is not("Aircraft" or "Unit" or "UnitPart" or "GameManager" or "NetworkManager")&&
            !call.DeclaringType.FullName.StartsWith("System.IO.",StringComparison.Ordinal))));
        Console.WriteLine($"Studio light runtime API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {foreach(var type in types){yield return type;foreach(var nested in AllTypes(type.NestedTypes))yield return nested;}}
}
