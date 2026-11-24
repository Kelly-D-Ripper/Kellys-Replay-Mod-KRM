using Mono.Cecil;

public static class CloseCameraChecks
{
    public static (int Checks, int Failures) Run(IReadOnlyDictionary<string, TypeDefinition> types, AssemblyDefinition mod, string managedDirectory)
    {
        int checks = 0, failures = 0;
        void Check(string name, bool good)
        { checks++; if (!good) failures++; Console.WriteLine((good ? "PASS " : "FAIL ") + "close camera " + name); }
        var compiled = AllTypes(mod.MainModule.Types).ToDictionary(type => type.FullName);
        MethodDefinition? Body(string type, string name) => compiled.TryGetValue(type, out var found)
            ? found.Methods.FirstOrDefault(method => method.Name == name && method.HasBody) : null;
        bool Calls(MethodDefinition? body, string type, string name) => body?.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference method && method.DeclaringType.GetElementType().FullName == type && method.Name == name) == true;
        MethodDefinition? Native(string type, string name) => types.TryGetValue(type, out var found)
            ? found.Methods.FirstOrDefault(method => method.Name == name && method.HasBody) : null;
        bool NativeNear(string type, float near) => Native(type, "EnterState")?.Body.Instructions.Any(instruction =>
            instruction.Operand is float value && value.Equals(near)) == true &&
            Calls(Native(type, "EnterState"), "UnityEngine.Camera", "set_nearClipPlane");
        const string helper = "ReplayMod.Core.ReplayCloseCameraVisibility", state = "ReplayMod.Core.ReplayCloseCameraState`1";
        var update = Body(helper, "Update"); var bodies = compiled.Values.Where(type => type.FullName == helper ||
            type.FullName.StartsWith(helper + "/", StringComparison.Ordinal)).SelectMany(type => type.Methods.Where(method => method.HasBody)).ToArray();
        Check("native external camera selects one metre near plane", NativeNear("CameraOrbitState", 1) && NativeNear("CameraTVState", 1));
        Check("native cockpit camera selects its own point-two metre near plane", NativeNear("CameraCockpitState", .2f));
        Check("native manager exposes exact scene main camera", types.TryGetValue("CameraStateManager", out var manager) &&
            manager.Fields.Any(field => field.Name == "mainCamera" && field.FieldType.FullName == "UnityEngine.Camera" && !field.IsStatic));
        using (var core = AssemblyDefinition.ReadAssembly(Path.Combine(managedDirectory, "UnityEngine.CoreModule.dll")))
        {
            var camera = core.MainModule.Types.Single(type => type.FullName == "UnityEngine.Camera");
            Check("installed Unity near-plane getter and setter match override", camera.Methods.Count(method => method.Name == "get_nearClipPlane" &&
                !method.IsStatic && method.ReturnType.FullName == "System.Single" && method.Parameters.Count == 0) == 1 &&
                camera.Methods.Count(method => method.Name == "set_nearClipPlane" && !method.IsStatic && method.ReturnType.FullName == "System.Void" &&
                    method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "System.Single" })) == 1);
        }
        Check("compiled override is gated by offline replay and transition state", Calls(update, "ReplayMod.Core.ReplayRuntime", "get_IsPlayback") &&
            Calls(update, "ReplayMod.Core.ReplayManager", "get_Playback") && Calls(update, "ReplayMod.Misc.SceneHelper", "get_IsTransitionPending") &&
            update?.Body.Instructions.Any(instruction => instruction.Operand is FieldReference field && field.DeclaringType.FullName == "GameManager" && field.Name == "IsHeadless") == true);
        Check("compiled override uses native main camera only", update?.Body.Instructions.Any(instruction => instruction.Operand is FieldReference field &&
            field.DeclaringType.FullName == "CameraStateManager" && field.Name == "mainCamera") == true && bodies.All(body => body.Body.Instructions.All(instruction =>
                instruction.Operand is not FieldReference field || field.DeclaringType.FullName != "CameraStateManager" || field.Name != "cockpitCamRender")));
        Check("compiled option-off and cleanup invoke original-state restoration", Calls(Body(helper, "SetEnabled"), helper, "Clear") &&
            Calls(Body(helper, "Clear"), helper, "Restore") && Calls(Body(helper, "Restore"), state, "Clear"));
        Check("compiled current camera applies single-owner bounded state", Calls(update, state, "Ensure") &&
            compiled.TryGetValue(state, out var ownerState) && ownerState.Fields.Count(field => field.FieldType.FullName == "T" && !field.IsStatic) == 1);
        Check("compiled visibility does not enable renderers or modify assets or global graphics", bodies.Length > 0 && bodies.All(body => body.Body.Instructions.All(instruction =>
            instruction.Operand is not MethodReference method ||
            method.DeclaringType.FullName != "UnityEngine.Renderer" && method.DeclaringType.FullName != "UnityEngine.Material" &&
            method.DeclaringType.FullName != "UnityEngine.Shader" && method.DeclaringType.FullName != "UnityEngine.QualitySettings" &&
            method.DeclaringType.FullName != "UnityEngine.PlayerPrefs" && method.DeclaringType.FullName != "UnityEngine.GameObject")));
        Check("compiled visibility performs no world scan, native actor movement or FOV changes", bodies.All(body => body.Body.Instructions.All(instruction =>
            instruction.Operand is not MethodReference method || method.Name is not ("FindObjectsOfType" or "FindObjectsByType" or "GetComponentsInChildren" or
                "SetPositionAndRotation" or "set_position" or "set_rotation" or "set_fieldOfView" or "SetFollowingUnit"))));
        Console.WriteLine($"Close camera API/integration audit: {checks} groups, {failures} failures");
        return (checks, failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    { foreach (var type in types) { yield return type; foreach (var nested in AllTypes(type.NestedTypes)) yield return nested; } }
}
