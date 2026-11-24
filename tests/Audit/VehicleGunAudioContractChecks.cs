using Mono.Cecil;
using Mono.Cecil.Cil;

public static class VehicleGunAudioContractChecks
{
    public static (int Checks, int Failures) Run(IReadOnlyDictionary<string, TypeDefinition> native, AssemblyDefinition mod)
    {
        int checks = 0, failures = 0;
        void Check(string name, bool good)
        {
            checks++;
            if (!good) failures++;
            Console.WriteLine((good ? "PASS " : "FAIL ") + "vehicle gun audio " + name);
        }

        var compiled = AllTypes(mod.MainModule.Types).ToDictionary(type => type.FullName);
        MethodDefinition? Body(IReadOnlyDictionary<string, TypeDefinition> types, string owner, string name) =>
            types.TryGetValue(owner, out var type) ? type.Methods.FirstOrDefault(method => method.Name == name && method.HasBody) : null;
        bool Calls(MethodDefinition? method, string owner, string name) => method?.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference call && call.DeclaringType.GetElementType().FullName == owner && call.Name == name) == true;
        bool Field(MethodDefinition? method, string owner, string name) => method?.Body.Instructions.Any(instruction =>
            instruction.Operand is FieldReference field && field.DeclaringType.GetElementType().FullName == owner && field.Name == name) == true;
        bool NativeField(string owner, string name, string type) => native.TryGetValue(owner, out var definition) &&
            definition.Fields.Any(field => !field.IsStatic && field.Name == name && field.FieldType.FullName == type);
        bool NativeMethod(string owner, string name, string result, params string[] parameters) => native.TryGetValue(owner, out var type) &&
            type.Methods.Any(method => !method.IsStatic && method.Name == name && method.ReturnType.FullName == result &&
                method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameters));

        const string controller = "ReplayMod.Core.UnitController", helper = "ReplayMod.Core.ReplayGunAudioPlayback";
        var nativeShot = Body(native, "Gun", "ShotSound");
        var nativeLoop = Body(native, "Gun", "LoopSounds");
        var shot = Body(compiled, helper, "PlayShot");
        var update = Body(compiled, helper, "Update");
        var constructor = Body(compiled, helper, ".ctor");
        var pause = Body(compiled, helper, "Pause");
        var resume = Body(compiled, helper, "Resume");
        var stop = Body(compiled, helper, "Stop");
        var controllerShot = Body(compiled, controller, "PlayReplayGunShot");
        var controllerUpdate = Body(compiled, controller, "UpdateReplayGunAudio");

        Check("native shared gun sources and firing clocks remain available",
            NativeField("Gun", "sources", "UnityEngine.AudioSource[]") && NativeField("Gun", "recoilSound", "UnityEngine.AudioSource") &&
            NativeField("Gun", "fireInterval", "System.Single") && NativeField("Weapon", "lastFired", "System.Single") &&
            NativeMethod("Gun", "ShotSound", "System.Void") && NativeMethod("Gun", "LoopSounds", "System.Void"));
        Check("native shot path uses source count and freshness before starting sustained playback",
            Field(nativeShot, "Gun", "sources") && Field(nativeShot, "Gun", "fireSounds") &&
            Field(nativeShot, "Gun", "fireStart") && Field(nativeShot, "Weapon", "lastFired") && Field(nativeShot, "Gun", "fireInterval") &&
            Calls(nativeShot, "UnityEngine.AudioSource", "get_isPlaying") && Calls(nativeShot, "UnityEngine.AudioSource", "PlayOneShot") &&
            Calls(nativeShot, "UnityEngine.AudioSource", "Play") && Calls(nativeShot, "UnityEngine.Time", "get_timeSinceLevelLoad"));
        Check("native sustained maintenance ends stale bursts and retains authored pitch ramps",
            Field(nativeLoop, "Gun", "sources") && Field(nativeLoop, "Gun", "fireEnd") &&
            Field(nativeLoop, "Weapon", "lastFired") && Field(nativeLoop, "Gun", "fireInterval") &&
            Field(nativeLoop, "Gun", "modifyPitch") && Field(nativeLoop, "Gun", "pitchClimbRate") && Field(nativeLoop, "Gun", "startPitch") &&
            Calls(nativeLoop, "UnityEngine.AudioSource", "set_pitch") && Calls(nativeLoop, "UnityEngine.AudioSource", "Stop") &&
            Calls(nativeLoop, "UnityEngine.AudioSource", "PlayOneShot"));
        Check("Unity exposes non-destructive source suspension and owned cleanup",
            NativeMethod("UnityEngine.AudioSource", "Pause", "System.Void") && NativeMethod("UnityEngine.AudioSource", "UnPause", "System.Void") &&
            NativeMethod("UnityEngine.AudioSource", "Stop", "System.Void") && NativeMethod("UnityEngine.AudioSource", "get_isPlaying", "System.Boolean"));

        Check("each gun helper captures only native main and recoil sources",
            compiled.TryGetValue(helper, out var helperType) && helperType.Fields.Any(field => field.FieldType.FullName == "Gun") &&
            helperType.Fields.Any(field => field.FieldType.FullName == "UnityEngine.AudioSource[]") &&
            Field(constructor, "Gun", "sources") && Field(constructor, "Gun", "recoilSound") &&
            !Calls(constructor, "UnityEngine.Component", "GetComponentsInChildren") && !Calls(constructor, "UnityEngine.GameObject", "GetComponentsInChildren"));
        var codecMethods = compiled.TryGetValue(controller, out var controllerType)
            ? controllerType.Methods.Where(method => method.Name == "Execute" && method.Parameters.Count == 1 &&
                method.Parameters[0].ParameterType.Name is "GunBulletEvent" or "BulletEvent").ToArray()
            : Array.Empty<MethodDefinition>();
        Check("legacy and resolved-projectile codecs both reach the shared gun helper",
            codecMethods.Length == 2 && codecMethods.All(method => Calls(method, controller, "PlayReplayGunShot")) &&
            Calls(controllerShot, helper, "PlayShot"));
        Check("rendered replay clock and pause state reach shared sustained maintenance",
            Calls(controllerUpdate, helper, "Update") && Calls(controllerUpdate, controller, "UpdateReplayGunSpins") &&
            Calls(update, "ReplayMod.Core.ReplayGunBurstState", "Age") && Calls(update, "ReplayMod.Core.ReplayGunBurstState", "NativeAge") &&
            Calls(update, "Gun", "LoopSounds") && Field(update, "Gun", "sources") && Field(update, "Weapon", "lastFired"));
        Check("despawn and world release stop every owned helper before cache removal",
            Calls(Body(compiled, controller, "DespawnUnit"), controller, "ForgetReplayGuns") &&
            Calls(Body(compiled, controller, "ReleaseOwnedPresentationObjects"), controller, "ClearReplayGuns") &&
            Calls(Body(compiled, controller, "ForgetReplayGuns"), helper, "Stop") &&
            Calls(Body(compiled, controller, "ClearReplayGuns"), helper, "Stop"));
        Check("fresh native firing timestamp dominates the first native shot sound",
            FreshTimestampDominatesShot(shot));
        Check("presentation never fires native weapons or changes firing-visibility state",
            compiled.TryGetValue(helper, out var noFireType) && noFireType.Methods.Where(method => method.HasBody)
                .Append(controllerShot).Where(method => method != null).All(method => method!.Body.Instructions.All(instruction =>
                    instruction.Operand is not MethodReference call || call.Name is not
                        ("TrackFiringVisibility" or "SpawnBullet" or "Fire" or "SingleFire" or "RemoteSingleFire" or "FixedUpdate"))));
        Check("pause resumes existing sounds and stop clears source ownership",
            Calls(pause, "UnityEngine.AudioSource", "Pause") && Calls(resume, "UnityEngine.AudioSource", "UnPause") &&
            Calls(stop, "UnityEngine.AudioSource", "Stop") && Calls(update, helper, "Pause") && Calls(update, helper, "Resume") &&
            Calls(shot, helper, "Resume"));
        Check("shared playback preserves native profile selection across all vehicle classes",
            Calls(shot, "Gun", "ShotSound") && Calls(update, "Gun", "LoopSounds") &&
            compiled.TryGetValue(helper, out var sharedType) && sharedType.Methods.Where(method => method.HasBody).All(method =>
                method.Body.Instructions.All(instruction =>
                    instruction.Operand is not TypeReference type || type.FullName is not ("Aircraft" or "GroundVehicle" or "Ship"))) &&
            sharedType.Methods.Where(method => method.HasBody).SelectMany(method => method.Body.Instructions).All(instruction =>
                instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string value ||
                !new[] {"Brawler", "AttackHelo1", "30mmRotary", "750rpm"}.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase))));

        Console.WriteLine($"Vehicle gun audio API/integration audit: {checks} groups, {failures} failures");
        return (checks, failures);
    }

    private static bool FreshTimestampDominatesShot(MethodDefinition? method)
    {
        if (method?.HasBody != true) return false;
        var instructions = method.Body.Instructions;
        var sounds = instructions.Where(instruction => instruction.Operand is MethodReference call &&
            call.DeclaringType.FullName == "Gun" && call.Name == "ShotSound").ToArray();
        if (sounds.Length != 1) return false;
        var sound = sounds[0];
        // The direct native-time store must execute on every control-flow path
        // that can reach ShotSound; a store in an unrelated branch is inadequate.
        foreach (var store in instructions.Where(instruction => instruction.OpCode.Code == Code.Stfld &&
                     instruction.Operand is FieldReference field && field.Name == "lastFired" && field.DeclaringType.FullName == "Weapon"))
        {
            var value = store.Previous;
            while (value?.OpCode.Code == Code.Nop) value = value.Previous;
            if (value?.Operand is not MethodReference clock || clock.DeclaringType.FullName != "UnityEngine.Time" ||
                clock.Name != "get_timeSinceLevelLoad") continue;
            var pending = new Stack<Instruction>();
            var visited = new HashSet<Instruction>();
            pending.Push(instructions[0]);
            bool reachesSoundWithoutStore = false;
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (current == store || !visited.Add(current)) continue;
                if (current == sound) { reachesSoundWithoutStore = true; break; }
                if (current.Operand is Instruction target) pending.Push(target);
                else if (current.Operand is Instruction[] targets)
                    foreach (var branch in targets) pending.Push(branch);
                if (current.Next != null && current.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw))
                    pending.Push(current.Next);
            }
            if (!reachesSoundWithoutStore) return true;
        }
        return false;
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
    {
        foreach (var type in roots)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
        }
    }
}
