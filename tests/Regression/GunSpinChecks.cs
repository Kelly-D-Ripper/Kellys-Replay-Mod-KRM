using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

public static class GunSpinChecks
{
    public static int Run()
    {
        int passed = 0;
        Test("rotary gun native one-second tail integrates once and stays still on paused frames", () =>
        {
            var spin = new ReplayGunSpin(); Check(spin.RecordShot(6), "first shot captured");
            Near(spin.PhaseAt(6), 0); Near(spin.PhaseAt(6.25), .21875); Near(spin.SpeedAt(6.25), .75);
            Near(spin.PhaseAt(6.5), .375); Near(spin.PhaseAt(7), .5); Near(spin.SpeedAt(7), 0);
            for (int i = 0; i < 1000; i++) { Near(spin.PhaseAt(6.5), .375); Near(spin.PhaseAt(600), .5); }
        });
        Test("rotary gun continuous fire refreshes speed without resetting phase or counting pellet duplicates", () =>
        {
            var spin = new ReplayGunSpin(); spin.RecordShot(6); spin.RecordShot(6.25); spin.RecordShot(6.5);
            Near(spin.PhaseAt(6.75), .65625); Near(spin.SpeedAt(6.75), .75);
            Check(!spin.RecordShot(6.5) && !spin.RecordShot(6.25), "duplicate and stale shots ignored");
            Check(!spin.RecordShot(double.NaN) && !spin.RecordShot(double.PositiveInfinity) && !spin.RecordShot(-1), "invalid clock ignored");
            Near(spin.PhaseAt(7.5), .9375); Near(spin.PhaseAt(100), .9375);
        });
        Test("rotary gun replay-clock phase survives frame rate speed changes and checkpoint restoration", () =>
        {
            var continuous = new ReplayGunSpin(); continuous.RecordShot(0); continuous.RecordShot(.2); continuous.RecordShot(.4);
            var restored = new ReplayGunSpin(); restored.Restore(.65, continuous.PhaseAt(.65), continuous.SpeedAt(.65));
            foreach (double time in new[] {.65, .7, .9, 1.2, 1.4, 2, 100})
            { Near(restored.PhaseAt(time), continuous.PhaseAt(time)); Near(restored.SpeedAt(time), continuous.SpeedAt(time)); }
            double fixedFrame = continuous.PhaseAt(1.3);
            for (int i = 0; i < 10000; i++) continuous.PhaseAt(.4 + i * .00009);
            Near(continuous.PhaseAt(1.3), fixedFrame);
            foreach (var pace in new[] {.1, .25, .5, 1.0})
                Near(continuous.PhaseAt(.4 + (.9 / pace) * pace), fixedFrame);
        });
        Test("rotary gun state codec owns identity and rejects corrupt phase speed or names", () =>
        {
            foreach (bool legacy in new[] {false, true})
            {
                var original = Anchor(5, 3, .75, .25); original.legacyAddress = legacy; original.legacyGunIndex = 17;
                var copy = (GunSpinStateEvent)RollingReplayBuffer.Decode(RollingReplayBuffer.Encode(original));
                Check(copy.unitId == 5 && copy.legacyAddress == legacy && copy.legacyGunIndex == 17 && copy.partId == 2 && copy.weaponName == "30 mm", "address roundtrip");
                Near(copy.Time, 3); Near(copy.phase, .75); Near(copy.speed, .25); ReplayEventFactory.Return(copy);
            }
            foreach (double bad in new[] {double.NaN, double.PositiveInfinity, -1, 1e13})
            { var badPhase = Anchor(1, 0, bad, 1); Throws(() => RollingReplayBuffer.Encode(badPhase)); }
            foreach (double bad in new[] {double.NaN, double.NegativeInfinity, -.1, 1.01})
            { var badSpeed = Anchor(1, 0, 0, bad); Throws(() => RollingReplayBuffer.Encode(badSpeed)); }
            var badName = Anchor(1, 0, 0, 1); badName.weaponName = new string('x', 513); Throws(() => RollingReplayBuffer.Encode(badName));
            var badAddress = Anchor(1, 0, 0, 1); badAddress.station = 256; Throws(() => RollingReplayBuffer.Encode(badAddress));
            var bytes = RollingReplayBuffer.Encode(Anchor(1, 0, 0, 1)); Throws(() => RollingReplayBuffer.Decode(bytes.Take(bytes.Length - 1).ToArray()));
            var pooled = ReplayEventFactory.GetEvent<GunSpinStateEvent>(); pooled.CopyFrom(Shot(9, 3)); pooled.phase = 12; pooled.speed = .5; ReplayEventFactory.Return(pooled);
            pooled = ReplayEventFactory.GetEvent<GunSpinStateEvent>(); Check(pooled.unitId == 0 && pooled.phase == 0 && pooled.speed == 0 && pooled.station == ushort.MaxValue && pooled.partId == -1, "pooled state fully resets"); ReplayEventFactory.Return(pooled);
        });
        Test("rotary gun seek reducer derives old and new projectile formats without restoring projectile effects", () =>
        {
            using var state = new ReplaySeekState(); state.Add(Spawn(1, 0)); state.Add(Shot(1, 6)); state.Add(Shot(1, 6.25));
            state.Add(new BulletEvent {unitId = 1, gunIndex = 3, Time = 6.5});
            var snapshot = new Queue<IReplayEvent>(); state.CopyTo(snapshot);
            var anchors = snapshot.OfType<GunSpinStateEvent>().ToArray();
            Check(anchors.Length == 2 && !snapshot.OfType<GunBulletEvent>().Any() && !snapshot.OfType<BulletEvent>().Any(), "only compact cosmetic states restored");
            Near(anchors.Single(value => !value.legacyAddress).phase, .21875); Near(anchors.Single(value => value.legacyAddress).phase, 0);
            anchors[0].phase = 777; Return(snapshot);
            snapshot.Clear(); state.CopyTo(snapshot); Check(snapshot.OfType<GunSpinStateEvent>().All(value => value.phase < 1), "snapshot events are independent"); Return(snapshot);
            state.Add(new DespawnEvent {unitId = 1, Time = 7}); state.Add(Spawn(1, 8)); state.Add(Shot(1, 6));
            state.CopyTo(snapshot); Check(!snapshot.OfType<GunSpinStateEvent>().Any(), "actor reuse cannot inherit previous spin"); Return(snapshot);
        });
        Test("rotary gun reducer clone and per-weapon phases are independent and memory accounting is bounded", () =>
        {
            var reducer = new ReplayGunSpinState(); reducer.Add(Shot(1, 0)); reducer.Add(Shot(1, .5)); reducer.Add(Shot(11, .25));
            var clone = new ReplayGunSpinState(); reducer.CloneTo(clone); Check(clone.EstimatedBytes == reducer.EstimatedBytes && clone.Count == 2, "clone preserves accounting");
            reducer.Forget(1); Check(reducer.Count == 1 && clone.Count == 2, "exact unit removal cannot match unit 11");
            long before = clone.EstimatedBytes;
            for (int i = 0; i < 10000; i++) clone.Add(Shot(1, .5 + i * .01));
            Check(clone.Count == 2 && clone.EstimatedBytes == before, "continuous shots do not grow checkpoint storage");
            var bounded = new ReplayGunSpinState();
            for (int i = 1; i <= ReplayGunSpinState.MaximumGuns + 1; i++) bounded.Add(Shot((uint)i, 1));
            Check(bounded.Count == ReplayGunSpinState.MaximumGuns && bounded.EstimatedBytes < 32L * 1024 * 1024, "cosmetic budget does not fault recording");
            clone.Clear(); Check(clone.Count == 0 && clone.EstimatedBytes == 0, "clear releases accounting");
        });
        Test("rotary gun rolling cut keeps completed phase and partial spin while rebasing time", () =>
        {
            var expected = new ReplayGunSpin(); expected.RecordShot(0); expected.RecordShot(1);
            using var ring = new RollingReplayBuffer(10); ring.Add(Spawn(1, 0)); ring.Add(Shot(1, 0)); ring.Add(Shot(1, 1));
            var events = ring.Snapshot(10.5).Select(RollingReplayBuffer.Decode).ToArray();
            var anchor = events.OfType<GunSpinStateEvent>().Single(); Near(anchor.Time, 0); Near(anchor.phase, .375); Near(anchor.speed, .5);
            var reconstructed = new ReplayGunSpin(); reconstructed.Restore(anchor.Time, anchor.phase, anchor.speed);
            foreach (var shot in events.OfType<GunBulletEvent>().OrderBy(value => value.Time)) reconstructed.RecordShot(shot.Time);
            Near(reconstructed.PhaseAt(1.5), expected.PhaseAt(2)); Near(reconstructed.SpeedAt(1.5), expected.SpeedAt(2));
            foreach (var value in events) ReplayEventFactory.Return(value);
            var completed = Anchor(1, 6, .25, .5); completed.RebaseAt(9, 0); Near(completed.phase, .375); Near(completed.speed, 0); Near(completed.Time, 0);
            Throws(() => completed.RebaseAt(-1, 0));
            var edge = new ReplayGunSpin(); edge.Restore(0, .375, .5);
            Check(edge.RecordShot(0) && !edge.RecordShot(0), "shot exactly on the cut refreshes an anchor once"); Near(edge.SpeedAt(0), 1); Near(edge.PhaseAt(1), .875);
        });
        return passed;
        void Test(string name, Action body) { body(); passed++; Console.WriteLine("PASS " + name); }
    }
    private static GunBulletEvent Shot(uint id, double time) => new GunBulletEvent {unitId = id, Time = time, station = 1, weaponSlot = 0, partId = 2, partGunSlot = 0, weaponName = "30 mm", sound = true};
    private static GunSpinStateEvent Anchor(uint id, double time, double phase, double speed) => new GunSpinStateEvent {unitId = id, Time = time, station = 1, weaponSlot = 0, partId = 2, partGunSlot = 0, weaponName = "30 mm", phase = phase, speed = speed};
    private static SpawnEvent Spawn(uint id, double time) => new SpawnEvent {unitId = id, ownerId = id, Time = time, jsonKey = "test", factionName = "test"};
    private static void Return(Queue<IReplayEvent> queue) { while (queue.Count > 0) ReplayEventFactory.Return(queue.Dequeue()); }
    private static void Near(double actual, double expected) { if (Math.Abs(actual - expected) > 1e-9) throw new Exception("Expected " + expected + ", got " + actual); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws(Action body) { try { body(); } catch (InvalidDataException) { return; } catch (EndOfStreamException) { return; } throw new Exception("Malformed gun state unexpectedly accepted"); }
}
