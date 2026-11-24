using System.Collections.Generic;
using System.Linq;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

// Main-thread presentation ownership. Native prefabs retain their materials,
// particles and audio, while recorded events replace damage/network simulation.
public static class ReplayVfxPlayback
{
    private static readonly HashSet<Unit> actors = new HashSet<Unit>();
    private static readonly HashSet<Unit> detonated = new HashSet<Unit>();
    private static readonly HashSet<GameObject> ownedObjects = new HashSet<GameObject>();
    private static readonly Dictionary<Unit, FuelTank[]> captureFuelTanks = new Dictionary<Unit, FuelTank[]>();
    private static readonly ReplayOwnedOverrideCache<ParticleSystem, ParticleSystemCullingMode> particleCulling =
        new ReplayOwnedOverrideCache<ParticleSystem, ParticleSystemCullingMode>(4096);
    private sealed class FadeSlot
    {
        public int index;
        public MaterialPropertyBlock original, applied;
        public bool wasEmpty;
    }
    private static readonly ReplayOwnedOverrideCache<ParticleSystemRenderer, List<FadeSlot>> cameraFade =
        new ReplayOwnedOverrideCache<ParticleSystemRenderer, List<FadeSlot>>(4096);
    private static readonly int cameraFadeId = Shader.PropertyToID("_CameraFadeParams");
    private static int particlePruneCounter;
    private static bool clearing;

    public static int OwnedObjectCount { get { ownedObjects.RemoveWhere(obj => obj == null); return ownedObjects.Count; } }
    public static int ProtectedParticleCount => particleCulling.Count;
    public static int CloseFadeOverrideCount => cameraFade.Count;
    public static void RegisterActor(Unit unit)
    {
        if (unit == null || !actors.Add(unit)) return;
        if (unit.partLookup != null)
            foreach (var part in unit.partLookup) if (part != null) ProtectParticles(part.gameObject);
        ProtectParticles(unit.gameObject);
    }
    public static void ForgetActor(Unit unit) { if (!ReferenceEquals(unit, null)) { actors.Remove(unit); detonated.Remove(unit); } }
    public static bool IsOwnedActor(Unit unit) => unit != null && actors.Contains(unit);
    public static void ForgetCapturedUnit(Unit unit) { if (!ReferenceEquals(unit, null)) captureFuelTanks.Remove(unit); }
    public static void ClearCaptureCache() => captureFuelTanks.Clear();

    public static void Track(GameObject obj)
    {
        if (obj == null || clearing) return;
        if (ownedObjects.Count >= 128) ownedObjects.RemoveWhere(item => item == null);
        if (ownedObjects.Add(obj)) ProtectParticles(obj);
    }

    private static void ProtectParticles(GameObject root)
    {
        if (root == null || clearing || !ReplayRuntime.IsPlayback) return;
        if ((++particlePruneCounter & 127) == 0)
        { particleCulling.Prune(system => system != null); cameraFade.Prune(renderer => renderer != null); }
        foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (system == null || particleCulling.Contains(system)) continue;
            // Native smoke uses Custom space and Automatic culling. Offscreen
            // pausing can leave its moving bounds stale and its particle count
            // empty when DamageParticles decides whether the effect has ended.
            particleCulling.TryApply(system, target => target.main.cullingMode, (target, original) =>
            { var main = target.main; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; });
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer == null || cameraFade.Contains(renderer)) continue;
            var materials = renderer.sharedMaterials;
            var slots = new List<FadeSlot>();
            // A native particle renderer has one main and at most one trail
            // material. Bound unusual modded renderers without changing assets.
            for (int index = 0; index < materials.Length && index < 16; index++)
            {
                var material = materials[index];
                if (material == null || !material.HasProperty(cameraFadeId)) continue;
                var fade = material.GetVector(cameraFadeId);
                if (!ReplayVfxPolicy.HasNearCameraFade(fade.x, fade.y)) continue;
                var original = new MaterialPropertyBlock(); var applied = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original, index);
                bool empty = original.isEmpty;
                // Indexed blocks override the renderer-wide block. Preserve
                // whichever supplied the original colours and other properties.
                if (empty) renderer.GetPropertyBlock(applied);
                else renderer.GetPropertyBlock(applied, index);
                applied.SetVector(cameraFadeId, new Vector4(0, 1000, 0, 0));
                slots.Add(new FadeSlot {index=index, original=original, applied=applied, wasEmpty=empty});
            }
            if (slots.Count > 0)
                cameraFade.TryApply(renderer, target => slots, (target, saved) =>
                { foreach (var slot in saved) target.SetPropertyBlock(slot.applied, slot.index); });
        }
    }

    public static void ClearOwnedObjects()
    {
        clearing = true;
        try
        {
            // Clear ownership before deferred Unity destruction can call native
            // particle/culling handlers or initialize another detached effect.
            var release = ownedObjects.ToArray();
            ownedObjects.Clear(); actors.Clear(); detonated.Clear();
            ReplayDamageParticleMotion.Clear();
            int restoreFailures = cameraFade.Restore(renderer => renderer != null, (renderer, slots) =>
            { foreach (var slot in slots) renderer.SetPropertyBlock(slot.wasEmpty ? null : slot.original, slot.index); });
            restoreFailures += particleCulling.Restore(system => system != null, (system, original) =>
            { var main = system.main; main.cullingMode = original; });
            particlePruneCounter = 0;
            if (restoreFailures > 0) Plugin.logger.LogWarning("Replay effect setting restore failures: " + restoreFailures);
            foreach (var obj in release) if (obj != null) Object.Destroy(obj);
        }
        finally { clearing = false; }
    }

    // Call after SpawnEvent is queued, and periodically for already tracked units
    // to catch status changes that bypass an RPC and mid-session recording starts.
    // The caller owns/queues every returned pooled event and assigns its timestamp.
    public static IEnumerable<FuelTankStatusEvent> CaptureFuelStates(Unit unit)
    {
        if (unit == null) yield break;
        if (!captureFuelTanks.TryGetValue(unit, out var tanks))
        {
            var found = new HashSet<FuelTank>(); var selected = new List<FuelTank>();
            void Include(GameObject root)
            {
                foreach (var tank in root.GetComponentsInChildren<FuelTank>(true))
                    if (tank != null && found.Add(tank)) selected.Add(tank);
            }
            // Native complex physics can detach wing parts from the aircraft
            // hierarchy. Their tanks still belong to this unit via partLookup.
            // Cache components even before FuelTank.Awake fills its part field.
            if (unit.partLookup != null)
                foreach (var part in unit.partLookup) if (part != null) Include(part.gameObject);
            Include(unit.gameObject);
            captureFuelTanks[unit] = tanks = selected.ToArray();
        }
        foreach (var tank in tanks)
        {
            if (tank == null || tank.part == null || tank.part.parentUnit != unit) continue;
            var sample = ReplayEventFactory.GetEvent<FuelTankStatusEvent>();
            sample.unitId = unit.persistentID.Id; sample.partId = tank.part.id;
            sample.ruptured = tank.ruptured; sample.onFire = tank.onFire;
            sample.flameActive = tank.fireEffectSpawn != null; sample.suppressBurst = true;
            yield return sample;
        }
    }

    public static UnitStatusEvent CaptureUnitStatus(Unit unit)
    {
        if (unit == null) return null;
        var sample = ReplayEventFactory.GetEvent<UnitStatusEvent>();
        sample.unitId = unit.persistentID.Id; sample.disabled = unit.disabled; sample.state = (byte)unit.unitState;
        return sample;
    }

    public static void Apply(UnitController controller, UnitStatusEvent sample)
    {
        var actor = controller.FindActor(sample.unitId);
        if (actor == null || controller.IsTerminalMissile(sample.unitId, actor)) return;
        // Networkdisabled invokes native UnitDisabled and starts delayed wreckage
        // removal. Replay lifecycle events own this unit's lifetime instead.
        actor.disabled = sample.disabled;
        actor.unitState = (Unit.UnitState)sample.state;
    }

    public static void Apply(UnitController controller, FuelTankStatusEvent sample)
    {
        controller.PrepareVfxActor(sample.unitId, sample.Time);
        var actor = controller.FindActor(sample.unitId);
        if (actor?.partLookup == null) return;
        var part = actor.partLookup.FirstOrDefault(candidate => candidate != null && candidate.id == sample.partId);
        if (part == null || !part.TryGetComponent<FuelTank>(out var tank)) return;

        // UpdateStatus also starts the server fire-damage coroutine. Invoke only
        // its presentation branches; the recording already owns subsequent HP.
        bool wasRuptured = tank.ruptured, wasOnFire = tank.onFire;
        tank.enabled = false;
        tank.ruptured = sample.ruptured; tank.onFire = sample.onFire;
        if (!wasRuptured && sample.ruptured)
        {
            if (sample.suppressBurst) tank.leakRate = tank.maxLeakRate;
            else tank.RuptureEffects();
        }
        // Format8 captures the actual continuous effect instance separately,
        // including its detached/fading lifetime. Keep only one-shot native
        // rupture/ignition bursts here; never create a competing looped flame.
        if(ReplayRuntime.FormatMajor>=8)
        {
            if(!wasOnFire&&sample.onFire&&!sample.suppressBurst&&tank.leakRate==tank.maxLeakRate)tank.Fireball();
            if(sample.onFire&&tank.leakSystem!=null){tank.leakSystem.Stop();Object.Destroy(tank.leakSystem.gameObject,5);tank.leakSystem=null;}
            if(tank.fireballSpawn!=null)Track(tank.fireballSpawn);
            return;
        }
        if (ReplayVfxPolicy.ShouldCreateFlame(wasOnFire, sample.flameActive, tank.fireEffectSpawn != null))
        {
            if (!sample.suppressBurst) tank.IgnitionEffects();
            else if (tank.fireEffect != null && tank.transform.position.y > Datum.LocalSeaY)
            {
                tank.fireEffectSpawn = Object.Instantiate(tank.fireEffect, tank.transform);
                tank.fireEffectSpawn.transform.localPosition = Vector3.zero;
                tank.fireParticles = tank.fireEffectSpawn.GetComponent<DamageParticles>();
                if (tank.fireParticles != null) tank.part.AddHostedParticles(tank.fireParticles);
            }
        }
        if (!sample.flameActive && tank.fireEffectSpawn != null)
        {
            Object.Destroy(tank.fireEffectSpawn); tank.fireEffectSpawn = null; tank.fireParticles = null;
        }
        if (tank.fireEffectSpawn != null) Track(tank.fireEffectSpawn);
        if (tank.fireballSpawn != null) Track(tank.fireballSpawn);
        if (tank.leakSystem != null) Track(tank.leakSystem.gameObject);
    }

    public static void Apply(UnitController controller, MissileDetonationEvent sample)
    {
        controller.PrepareVfxActor(sample.unitId, sample.Time);
        if (!(controller.FindActor(sample.unitId) is Missile missile) || detonated.Contains(missile) ||
            !controller.CanApplyMissileImpact(sample.unitId, missile, sample.Time)) return;
        var point = sample.position.ToLocalPosition();
        var direction = sample.normal;
        if (!ReplayVfxPolicy.IsFinite(point.x) || !ReplayVfxPolicy.IsFinite(point.y) || !ReplayVfxPolicy.IsFinite(point.z)) return;
        if (!ReplayVfxPolicy.IsFinite(direction.x) || !ReplayVfxPolicy.IsFinite(direction.y) || !ReplayVfxPolicy.IsFinite(direction.z) || direction.sqrMagnitude < 0.000001f)
            direction = Vector3.up;
        detonated.Add(missile);
        // Hide the missile mesh while its existing particle/trail renderers fade.
        foreach (var renderer in missile.GetComponentsInChildren<MeshRenderer>()) renderer.enabled = false;
        if (missile.motors != null) foreach (var motor in missile.motors) motor.Burnout(true);
        if (missile.flightSound != null)
        {
            missile.flightSound.Stop(); missile.flightSound.clip = missile.nearbyDetonationClip;
            missile.flightSound.pitch = 1; missile.flightSound.volume = 1; missile.flightSound.dopplerLevel = 1;
            missile.flightSound.loop = false; missile.flightSound.Play();
        }
        if (missile.effectsTransform != null)
        {
            // Native client impacts can correct a predicted flight position by
            // many metres. Preserve the old world-space trail before applying
            // that correction; moving its root first draws a false bridge.
            missile.effectsTransform.SetParent(Datum.origin); Track(missile.effectsTransform.gameObject);
            Object.Destroy(missile.effectsTransform.gameObject, 20);
        }

        controller.RecordMissileImpact(sample.unitId, missile, sample.position, sample.Time);

        var warhead = missile.warhead;
        if (warhead == null) return;
        Quaternion rotation = Quaternion.LookRotation(direction);
        if (!sample.armed)
        {
            Spawn(warhead.fizzleEffect, point, rotation, missile, 30); return;
        }
        float radius = Mathf.Pow(Mathf.Max(missile.blastYield, 0), 0.3333f) * 2;
        bool underwater = point.y < Datum.LocalSeaY + 0.1f;
        GameObject main = null;
        if (underwater) main = Spawn(warhead.underwaterEffect, new Vector3(point.x, Datum.LocalSeaY, point.z), Quaternion.identity, missile, 30);
        else
        {
            if (sample.hitTerrain) main = Spawn(warhead.terrainEffect, point, rotation, missile, 30);
            if (sample.hitArmor) main = Spawn(warhead.armorEffect, point, rotation, missile, 30) ?? main;
            bool ground = sample.hitTerrain || (Physics.Linecast(point, point - Vector3.up * radius, out var hit, PhysicsLayers.StaticsMask) && hit.point.y > Datum.LocalSeaY);
            if (!ground && point.y < Datum.LocalSeaY + radius && point.y > Datum.LocalSeaY + 1)
                Spawn(warhead.waterSurfaceEffect, new Vector3(point.x, Datum.LocalSeaY, point.z), Quaternion.identity, missile, 30);
        }
        if (main == null) Spawn(warhead.airEffect, point, rotation, missile, 30);
    }

    private static GameObject Spawn(GameObject prefab, Vector3 point, Quaternion rotation, Missile missile, float lifetime)
    {
        if (prefab == null) return null;
        var obj = Object.Instantiate(prefab, Datum.origin);
        obj.transform.SetPositionAndRotation(point, rotation); Track(obj);
        // Native shockwave visuals still expand; its physical influence list is
        // cleared by the replay patch before Update can apply any force/damage.
        if (missile.blastYield > 200)
        {
            var shockwave = obj.GetComponentInChildren<Shockwave>();
            if (shockwave != null) shockwave.SetOwner(PersistentID.None, missile.blastYield * 0.000001f);
            // Nuclear cloud prefabs have their own longer visual lifetime.
        }
        else Object.Destroy(obj, lifetime);
        return obj;
    }
}
