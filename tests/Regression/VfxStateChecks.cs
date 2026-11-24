using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class VfxStateChecks
{
    public static void Run(Action<string, Action> test)
    {
        test("VFX actual fuel ignition and resolved missile impact roundtrip", () =>
        {
            var status = RoundTrip(new UnitStatusEvent { Time = 51, unitId = 617, disabled = true, state = 4 });
            Require(status.Time == 51 && status.unitId == 617 && status.disabled && status.state == 4, "critical-part disable and lifecycle status preserved");
            ReplayEventFactory.Return(status);
            var fuel = RoundTrip(new FuelTankStatusEvent { Time = 51.25, unitId = 617, partId = 255, ruptured = true, onFire = true, flameActive = true, suppressBurst = true });
            Require(fuel.Time == 51.25 && fuel.unitId == 617 && fuel.partId == 255 && fuel.ruptured && fuel.onFire && fuel.flameActive && fuel.suppressBurst, "fuel identity and authoritative outcome");
            ReplayEventFactory.Return(fuel);
            foreach (bool armed in new[] { false, true })
                foreach (bool armor in new[] { false, true })
                    foreach (bool terrain in new[] { false, true })
                    {
                        var blast = RoundTrip(new MissileDetonationEvent { Time = 17, unitId = 12,
                            position = new GlobalPosition(-1024, 420, 321), normal = new Vector3(-.5f, .25f, 1),
                            armed = armed, hitArmor = armor, hitTerrain = terrain });
                        Require(blast.Time == 17 && blast.unitId == 12 && blast.position.x == -1024 && blast.position.y == 420 && blast.normal.x == -.5f && blast.normal.y == .25f && blast.normal.z == 1 && blast.armed == armed && blast.hitArmor == armor && blast.hitTerrain == terrain, "air/armor/terrain/fizzle classifications survive codec");
                        ReplayEventFactory.Return(blast);
                    }
        });
        test("VFX truncated events fail and returned events reset fully", () =>
        {
            foreach (var original in new IReplayEvent[] {
                new UnitStatusEvent { Time = 3, unitId = 17, disabled = true, state = 4 },
                new FuelTankStatusEvent { Time = 4, unitId = 77, partId = 9, ruptured = true, onFire = true, flameActive = true, suppressBurst = true },
                new MissileDetonationEvent { Time = 8, unitId = 91, position = new GlobalPosition(1,2,3), normal = new Vector3(4,5,6), armed = true, hitArmor = true, hitTerrain = true } })
            {
                var bytes = RollingReplayBuffer.Encode(original);
                for (int length = 0; length < bytes.Length; length++)
                {
                    bool rejected = false;
                    try { var invalid = RollingReplayBuffer.Decode(bytes.Take(length).ToArray()); ReplayEventFactory.Return(invalid); }
                    catch (EndOfStreamException) { rejected = true; }
                    Require(rejected, "incomplete VFX record cannot become valid state");
                }
                original.Reset();
                Require(original.Time == 0, "reset timestamp");
                if (original is UnitStatusEvent status) Require(status.unitId == 0 && !status.disabled && status.state == 0, "no previous disable state reused");
                if (original is FuelTankStatusEvent fuel) Require(fuel.unitId == 0 && fuel.partId == 0 && !fuel.ruptured && !fuel.onFire && !fuel.flameActive && !fuel.suppressBurst, "no previous tank flags reused");
                if (original is MissileDetonationEvent blast) Require(blast.unitId == 0 && blast.position.x == 0 && blast.position.y == 0 && blast.position.z == 0 && blast.normal.x == 0 && blast.normal.y == 0 && blast.normal.z == 0 && !blast.armed && !blast.hitArmor && !blast.hitTerrain, "no previous impact reused");
            }
        });
        test("VFX seek checkpoint keeps latest surviving tank state and omits historical explosions", () =>
        {
            using var state = new ReplaySeekState();
            state.Add(new SpawnEvent { unitId = 7, ownerId = 7, Time = 0 });
            state.Add(new FuelTankStatusEvent { unitId = 7, partId = 1, Time = 1, ruptured = true });
            state.Add(new FuelTankStatusEvent { unitId = 7, partId = 1, Time = 2, ruptured = true, onFire = true, flameActive = true });
            state.Add(new FuelTankStatusEvent { unitId = 7, partId = 2, Time = 3, onFire = true, flameActive = true });
            state.Add(new UnitStatusEvent { unitId = 7, Time = 2.5, disabled = true, state = 4 });
            state.Add(new SpawnEvent { unitId = 9, ownerId = 7, Time = 2 });
            state.Add(new MissileDetonationEvent { unitId = 9, Time = 3, armed = true });
            var snapshot = new Queue<IReplayEvent>(); state.CopyTo(snapshot);
            try
            {
                var fuel = snapshot.OfType<FuelTankStatusEvent>().ToArray();
                Require(fuel.Length == 2 && fuel.Single(e => e.partId == 1).Time == 2 && fuel.Single(e => e.partId == 1).onFire && fuel.Single(e => e.partId == 2).onFire, "independent tank state restored at a checkpoint");
                Require(fuel.All(e => e.suppressBurst && e.flameActive), "restored persistent flames cannot invent an old fireball burst");
                Require(!snapshot.OfType<MissileDetonationEvent>().Any(), "past one-shot explosions are not resurrected at every seek");
                Require(!snapshot.OfType<SpawnEvent>().Any(e => e.unitId == 9), "impacted missile root is not recreated as a visible projectile");
                Require(snapshot.OfType<UnitStatusEvent>().Single().disabled && snapshot.OfType<UnitStatusEvent>().Single().state == 4, "critical disabled state survives checkpoint");
            }
            finally { Return(snapshot); }
        });
        test("VFX actor reuse and clone ownership cannot leak flames into a new lifetime", () =>
        {
            using var state = new ReplaySeekState();
            state.Add(new SpawnEvent { unitId = 5, ownerId = 5, Time = 0 });
            state.Add(new FuelTankStatusEvent { unitId = 5, Time = 1, partId = 3, onFire = true });
            state.Add(new UnitStatusEvent { unitId = 5, Time = 1.5, disabled = true, state = 4 });
            using var previous = state.Clone();
            state.Add(new DespawnEvent { unitId = 5, Time = 2 });
            state.Add(new SpawnEvent { unitId = 5, ownerId = 5, Time = 4 });
            state.Add(new FuelTankStatusEvent { unitId = 5, Time = 3, partId = 3, onFire = true });
            var fresh = new Queue<IReplayEvent>(); var old = new Queue<IReplayEvent>();
            state.CopyTo(fresh); previous.CopyTo(old);
            try { Require(!fresh.OfType<FuelTankStatusEvent>().Any() && !fresh.OfType<UnitStatusEvent>().Any() && old.OfType<FuelTankStatusEvent>().Single().onFire && old.OfType<UnitStatusEvent>().Single().disabled, "old lifetime is cleared and snapshot owns independent fuel and disable events"); }
            finally { Return(fresh); Return(old); }
        });
        test("VFX rolling clips restore burning tanks at clip start without expired detonations", () =>
        {
            using var ring = new RollingReplayBuffer(30);
            ring.Add(new SpawnEvent { Time = 0, unitId = 8, ownerId = 8, jsonKey = "test-aircraft", factionName = "test-faction" });
            ring.Add(new FuelTankStatusEvent { Time = 1, unitId = 8, partId = 4, ruptured = true, onFire = true, flameActive = true });
            ring.Add(new SpawnEvent { Time = 1.5, unitId = 9, ownerId = 8, jsonKey = "test-missile", factionName = "test-faction" });
            ring.Add(new MissileDetonationEvent { Time = 2, unitId = 9, armed = true });
            ring.Trim(50);
            var decoded = new Queue<IReplayEvent>(ring.Snapshot(50).Select(RollingReplayBuffer.Decode));
            try { var fuel = decoded.OfType<FuelTankStatusEvent>().Single(); Require(fuel.Time == 0 && fuel.onFire && fuel.ruptured && fuel.flameActive && fuel.suppressBurst && !decoded.OfType<MissileDetonationEvent>().Any(), "saved recent clip starts with persistent flames and no old explosion"); }
            finally { Return(decoded); }
        });
        test("VFX attached particle lifetime only overrides false replay orphan culling", () =>
        {
            Require(ReplayVfxPolicy.KeepAttachedParticles(true, true, false, true), "kinematic owned actor keeps its native flames/smoke");
            Require(!ReplayVfxPolicy.KeepAttachedParticles(false, true, false, true), "live gameplay native culling retained");
            Require(!ReplayVfxPolicy.KeepAttachedParticles(true, false, false, true), "unowned object retains native culling");
            Require(!ReplayVfxPolicy.KeepAttachedParticles(true, true, true, true), "detached part can stop/depart");
            Require(!ReplayVfxPolicy.KeepAttachedParticles(true, true, false, false), "destroyed/reset parent can release its effects");
            Require(ReplayVfxPolicy.ShouldCreateFlame(false, true, false) && !ReplayVfxPolicy.ShouldCreateFlame(true, true, true), "initial flame starts and repeated identical state is idempotent");
            Require(ReplayVfxPolicy.ShouldCreateFlame(true, true, false) && !ReplayVfxPolicy.ShouldCreateFlame(true, false, true), "missing persistent flame can restore and extinguished state does not restart");
            Require(!ReplayVfxPolicy.IsFinite(float.NaN) && !ReplayVfxPolicy.IsFinite(float.PositiveInfinity) && ReplayVfxPolicy.IsFinite(-100), "invalid impact position rejected before native transform");
        });
    }

    private static T RoundTrip<T>(T value) where T : class, IReplayEvent => (T)RollingReplayBuffer.Decode(RollingReplayBuffer.Encode(value));
    private static void Return(Queue<IReplayEvent> events) { while (events.Count > 0) ReplayEventFactory.Return(events.Dequeue()); }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
