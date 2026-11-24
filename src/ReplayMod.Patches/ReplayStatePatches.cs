using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(UnitPart), nameof(UnitPart.InvokeDamage))]
public static class PartDamagePatch
{
    public static Action<UnitPart, float, float, float, float> OnDamage;
    [HarmonyPostfix]
    public static void Postfix(UnitPart __instance, float netPierceDamage, float netBlastDamage, float netFireDamage, float netImpactDamage)
    { OnDamage?.Invoke(__instance, netPierceDamage, netBlastDamage, netFireDamage, netImpactDamage); }
}

// Replayed HP is applied through ApplyDamage; physical hits must not invent damage.
[HarmonyPatch(typeof(UnitPart), nameof(UnitPart.TakeDamage))]
public static class ReplayPhysicalDamagePatch
{
    [HarmonyPrefix] public static bool Prefix() => !ReplayRuntime.IsPlayback;
}

[HarmonyPatch(typeof(DamageEffects), nameof(DamageEffects.BlastFrag))]
public static class ReplayBlastDamagePatch
{
    [HarmonyPrefix] public static bool Prefix() => !ReplayRuntime.IsPlayback;
}

[HarmonyPatch(typeof(Aircraft), "CheckPhysicsLod")]
public static class ReplayPhysicsLodPatch
{
    [HarmonyPrefix] public static bool Prefix() => !ReplayRuntime.IsPlayback;
}

[HarmonyPatch(typeof(LandingGear), "FixedUpdate")]
public static class ReplayGearContactPatch
{
    [HarmonyPrefix] public static bool Prefix() => !ReplayRuntime.IsPlayback;
}

[HarmonyPatch(typeof(Missile), "ServerFixedUpdate")]
public static class ReplayMissileGuidancePatch
{
    [HarmonyPrefix] public static bool Prefix() => !ReplayRuntime.IsPlayback;
}

// Replay missiles spawn on an offline single-player host, so native LocalStart
// would initialise the seeker. Every seeker then runs SlowChecks on its own
// slow-update timer (not ServerFixedUpdate) and can self-destruct a replay
// missile, which never has a target. That timer's phase changes on each
// respawn, so the false impact moved on every rewind. Recorded impacts are
// presented by ReplayVfxPlayback without native Detonate.
[HarmonyPatch(typeof(Missile), "LocalStart")]
public static class ReplayMissileSeekerStartPatch
{
    [HarmonyPrefix] public static bool Prefix() => !ReplayRuntime.IsPlayback;
}

[HarmonyPatch(typeof(Missile), nameof(Missile.Detonate))]
public static class ReplayMissileNativeDetonationPatch
{
    // Main thread only. Legacy recordings mark an impact only on despawn and
    // rely on native presentation; that recorded request is the one exception.
    private static bool recordedDespawn;
    internal static void DetonateRecordedDespawn(Missile missile)
    {
        recordedDespawn = true;
        try { missile.Detonate(missile.rb.velocity, hitArmor: false, hitTerrain: false); }
        finally { recordedDespawn = false; }
    }
    [HarmonyPrefix] public static bool Prefix() => !ReplayRuntime.IsPlayback || recordedDespawn;
}

[HarmonyPatch(typeof(Missile), nameof(Missile.TakeDamage))]
public static class ReplayMissileDamagePatch
{
    [HarmonyPrefix] public static bool Prefix() => !ReplayRuntime.IsPlayback;
}

[HarmonyPatch(typeof(Missile), "MotorThrust")]
public static class ReplayMissileMotorPatch
{
    [HarmonyPrefix] public static bool Prefix() => !ReplayRuntime.IsPlayback || ReplayRuntime.FormatMajor < 2;
}

[HarmonyPatch]
public static class EjectionVisualPatch
{
    public static Action<Aircraft, bool> OnEjection;
    public static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(Aircraft)))
            if (method.Name.StartsWith("UserCode_RpcJettisonCanopy_", StringComparison.Ordinal) ||
                method.Name.StartsWith("UserCode_RpcEscapeCapsule_", StringComparison.Ordinal)) yield return method;
    }
    [HarmonyPostfix]
    public static void Postfix(Aircraft __instance, MethodBase __originalMethod)
    { OnEjection?.Invoke(__instance, __originalMethod.Name.Contains("EscapeCapsule")); }
}
