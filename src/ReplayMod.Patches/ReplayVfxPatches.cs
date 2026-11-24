using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(Unit), nameof(Unit.UnitDisabled))]
public static class ReplayUnitStatusPatch
{
    public static Action<Unit> OnStatus;
    [HarmonyPostfix]
    public static void Postfix(Unit __instance)
    { if (!ReplayRuntime.IsPlayback) OnStatus?.Invoke(__instance); }
}

[HarmonyPatch(typeof(FuelTank), nameof(FuelTank.UpdateStatus))]
public static class ReplayFuelTankStatusPatch
{
    public static Action<FuelTank, bool, bool> OnStatus;
    [HarmonyPostfix]
    public static void Postfix(FuelTank __instance)
    {
        if (!ReplayRuntime.IsPlayback) OnStatus?.Invoke(__instance, __instance.ruptured, __instance.onFire);
    }
}

// Hook the presentation implementation, reached for local and remote missiles.
// The old Detonate prefix records only the local authority's invocation.
[HarmonyPatch]
public static class ReplayMissileDetonationPatch
{
    public static Action<Missile, GlobalPosition, Vector3, bool, bool, bool> OnDetonation;
    public static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(Missile)))
            if (method.Name.StartsWith("UserCode_RpcDetonate_", StringComparison.Ordinal)) yield return method;
    }
    [HarmonyPostfix]
    public static void Postfix(Missile __instance, bool armed, bool hitArmor, bool hitTerrain, Vector3 normal)
    {
        if (!ReplayRuntime.IsPlayback && __instance != null)
            OnDetonation?.Invoke(__instance, __instance.transform.position.ToGlobalPosition(), normal, armed, hitArmor, hitTerrain);
    }
}

[HarmonyPatch(typeof(DamageParticles), "Awake")]
public static class ReplayDamageParticlesAwakePatch
{
    [HarmonyPostfix]
    public static void Postfix(DamageParticles __instance)
    {
        if (!ReplayRuntime.IsPlayback||ReplayFuelFlameVisual.IsControlled(__instance)) return;
        __instance.fireDamage = 0;
        var part = __instance.GetComponentInParent<UnitPart>();
        if (part == null || !ReplayVfxPlayback.IsOwnedActor(part.parentUnit)) return;
        ReplayVfxPlayback.Track(__instance.gameObject);
        // Native Awake treats a kinematic replay body as an orphan, schedules
        // culling after 1.5 seconds, then stops its looped flame/smoke systems.
        ReplayDamageParticleMotion.Register(__instance,part);
    }
}

[HarmonyPatch(typeof(FuelTank), "FixedUpdate")]
public static class ReplayFuelTankSimulationPatch
{
    [HarmonyPrefix]
    public static bool Prefix() => !ReplayRuntime.IsPlayback;
}

[HarmonyPatch(typeof(FuelTank), "CheckDamageStatus")]
public static class ReplayFuelTankIgnitionPatch
{
    [HarmonyPrefix]
    public static bool Prefix() => !ReplayRuntime.IsPlayback;
}

[HarmonyPatch(typeof(DamageParticles), nameof(DamageParticles.ParentObjectCulled))]
public static class ReplayDamageParticlesCullPatch
{
    [HarmonyPrefix]
    public static bool Prefix(DamageParticles __instance)
    {
        return !ReplayDamageParticleMotion.Keep(__instance);
    }
}

[HarmonyPatch(typeof(Shockwave), "Update")]
public static class ReplayShockwaveVisualPatch
{
    [HarmonyPrefix]
    public static void Prefix(Shockwave __instance)
    {
        if (ReplayRuntime.IsPlayback) __instance.influencedObjects.Clear();
    }
}

// Native Detach only emits disintegration effects within 5 km of the camera.
// Telephoto replay shots still need the recorded part's visual event at range.
[HarmonyPatch(typeof(AeroPart), nameof(AeroPart.Detach))]
public static class ReplayDistantPartFragmentsPatch
{
    [HarmonyPrefix]
    public static void Prefix(AeroPart __instance, out bool __state)
    { __state = ReplayRuntime.IsPlayback && ReplayVfxPlayback.IsOwnedActor(__instance.parentUnit); }

    [HarmonyPostfix]
    public static void Postfix(AeroPart __instance, bool __state)
    {
        if (__instance != null && ReplayVfxPolicy.ShouldRestoreDistantFragments(__state, __instance.fragmented))
            __instance.SpawnFragments();
    }
}
