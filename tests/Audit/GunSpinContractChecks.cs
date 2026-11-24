using Mono.Cecil;
using Mono.Cecil.Cil;

public static class GunSpinContractChecks
{
    public static (int Checks, int Failures) Run(IReadOnlyDictionary<string, TypeDefinition> types, AssemblyDefinition mod)
    {
        int checks = 0, failures = 0;
        void Check(string name, bool good) { checks++; if (!good) failures++; Console.WriteLine((good ? "PASS " : "FAIL ") + "rotary gun " + name); }
        var compiled = AllTypes(mod.MainModule.Types).ToDictionary(type => type.FullName);
        MethodDefinition? Body(string owner, string name) => compiled.TryGetValue(owner, out var type) ? type.Methods.FirstOrDefault(method => method.Name == name && method.HasBody) : null;
        bool Calls(MethodDefinition? method, string owner, string name) => method?.Body.Instructions.Any(instruction => instruction.Operand is MethodReference call && call.DeclaringType.GetElementType().FullName == owner && call.Name == name) == true;
        bool Field(MethodDefinition? method, string owner, string name) => method?.Body.Instructions.Any(instruction => instruction.Operand is FieldReference field && field.DeclaringType.GetElementType().FullName == owner && field.Name == name) == true;
        bool NativeField(string name, string fieldType) => types.TryGetValue("Gun", out var type) && type.Fields.Any(field => field.Name == name && field.FieldType.FullName == fieldType && !field.IsStatic);
        const string controller = "ReplayMod.Core.UnitController", spin = "ReplayMod.Core.ReplayGunSpin", reducer = "ReplayMod.Core.ReplayGunSpinState", seek = "ReplayMod.Core.ReplaySeekState", rolling = "ReplayMod.Core.RollingReplayBuffer";
        const string anchor = "ReplayMod.Events.ConcreteEvents.GunSpinStateEvent";

        Check("native shared gun component exposes its authored spin transform and angular speed", NativeField("spinTransform", "UnityEngine.Transform") && NativeField("spinRate", "System.Single") && NativeField("currentSpinRate", "System.Single"));
        var nativeSpin = types.Values.FirstOrDefault(type => type.DeclaringType?.FullName == "Gun" && type.Name.StartsWith("<SpinBarrels>", StringComparison.Ordinal))?.Methods.FirstOrDefault(method => method.Name == "MoveNext" && method.HasBody);
        Check("native barrel coroutine uses local forward rotation and one-per-second speed decay", Field(nativeSpin, "Gun", "spinTransform") && Field(nativeSpin, "Gun", "spinRate") && Field(nativeSpin, "Gun", "currentSpinRate") && Calls(nativeSpin, "UnityEngine.Transform", "Rotate") && Calls(nativeSpin, "UnityEngine.Vector3", "get_forward") && Calls(nativeSpin, "UnityEngine.Time", "get_deltaTime") && nativeSpin?.Body.Instructions.Any(instruction => instruction.OpCode.Code == Code.Sub) == true);

        var binding = Body(controller, "ReplaySpin"); var display = Body(controller, "UpdateReplayGunSpins");
        Check("playback only binds an owned gun with a native spin transform on any unit type", Calls(binding, controller, "OwnsReplayUnit") && Field(binding, "Gun", "spinTransform") && Field(binding, "Gun", "spinRate") && Calls(binding, "UnityEngine.Transform", "get_localRotation") && binding?.Body.Instructions.All(instruction => instruction.OpCode.Code != Code.Isinst || instruction.Operand is not TypeReference reference || reference.FullName != "Aircraft") == true);
        Check("old and new shot playback feed the deterministic barrel envelope", Calls(Body(controller, "PlayReplayGunShot"), controller, "ReplaySpin") && Calls(Body(controller, "PlayReplayGunShot"), spin, "RecordShot") && compiled[controller].Methods.Where(method => method.Name == "Execute" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.Name is "GunBulletEvent" or "BulletEvent").All(method => Calls(method, controller, "PlayReplayGunShot")));
        Check("barrels render from replay phase without frame clocks async native fire or physics", Calls(Body(controller, "UpdateReplayGunAudio"), controller, "UpdateReplayGunSpins") && Calls(display, spin, "PhaseAt") && Calls(display, "UnityEngine.Transform", "set_localRotation") && Calls(display, "UnityEngine.Quaternion", "AngleAxis") && Calls(display, "UnityEngine.Vector3", "get_forward") && new[] {display, Body(controller, "PlayReplayGunShot")}.All(method => method != null && method.Body.Instructions.All(instruction => instruction.Operand is not MethodReference call || (call.DeclaringType.FullName != "UnityEngine.Rigidbody" && call.Name is not ("SpinBarrels" or "FixedUpdate" or "SpawnBullet")))) && compiled[spin].Methods.Where(method => method.HasBody).All(method => method.Body.Instructions.All(instruction => instruction.Operand is not MethodReference call || call.DeclaringType.FullName != "UnityEngine.Time")));

        var restore = compiled[controller].Methods.FirstOrDefault(method => method.Name == "Execute" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == anchor && method.HasBody);
        Check("seek anchors restore phase without historical gun sounds particles bullets or damage", Calls(restore, controller, "AcceptSample") && Calls(restore, controller, "TryGetUnit") && Calls(restore, controller, "ResolveReplayGun") && Calls(restore, spin, "Restore") && restore?.Body.Instructions.All(instruction => instruction.Operand is not MethodReference call || call.Name is not ("PlayReplayGunShot" or "ShotSound" or "Play" or "SpawnBullet" or "Fire" or "ApplyDamage" or "Create")) == true);
        Check("checkpoint state derives both projectile codecs and follows actor lifetime clone and memory bounds", Calls(Body(seek, "Add"), reducer, "Add") && Calls(Body(seek, "Forget"), reducer, "Forget") && Calls(Body(seek, "Clone"), reducer, "CloneTo") && Calls(Body(seek, "CopyTo"), reducer, "CopyTo") && Calls(Body(seek, "Clear"), reducer, "Clear") && Calls(Body(seek, "get_EstimatedBytes"), reducer, "get_EstimatedBytes") && compiled[reducer].Methods.Count(method => method.Name == "Add") == 3 && compiled[reducer].Fields.Any(field => field.Name == "MaximumGuns" && field.HasConstant && (int)field.Constant == 8192));
        Check("rolling cuts sample phase and residual speed before assigning the new replay clock", Calls(Body(rolling, "Snapshot"), anchor, "RebaseAt") && Calls(Body(anchor, "RebaseAt"), spin, "PhaseAt") && Calls(Body(anchor, "RebaseAt"), spin, "SpeedAt") && Calls(Body(controller, "ForgetReplayGuns"), "System.Collections.Generic.Dictionary`2", "Remove") && Calls(Body(controller, "ClearReplayGuns"), "System.Collections.Generic.Dictionary`2", "Clear"));
        Console.WriteLine($"Rotary gun API/integration audit: {checks} groups, {failures} failures"); return (checks, failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
    { foreach (var type in roots) { yield return type; foreach (var child in AllTypes(type.NestedTypes)) yield return child; } }
}
