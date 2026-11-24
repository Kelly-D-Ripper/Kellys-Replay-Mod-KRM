using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class MissileImpactChecks
{
    public static int Run()
    {
        int passed = 0;
        Test("missile impact blocks the supplied recording's future terminal correction", () =>
        {
            var buffer = new BoundedEventBuffer(8);
            const double before = 83.804949, impactTime = 83.82071203528903, after = 83.863337;
            var flight = Move(156, before, 10729.21777f, 408.24536f, 15745.00879f);
            var impact = Impact(156, impactTime);
            var terminal = Move(156, after, 10691.23535f, 472.147827f, 15676.66699f);
            buffer.Add(flight, default); buffer.Add(impact, default); buffer.Add(terminal, default);
            Check(buffer.TryTakeIfReady(83.815, out var next) && ReferenceEquals(next, flight), "last flight sample remains available");
            var history = new List<PositionSnapshot> {PositionSnapshot.Create((UpdatePositionEvent)next)};
            ReplayEventFactory.Return(next);
            Check(!buffer.TryTakeIfReady(83.815, out _), "100 metre client impact correction cannot enter pre-impact interpolation");
            Check(SnapshotHistory.Locate(history, 83.815, p => p.time, out int left, out int right, out _) && left == right,
                "pre-impact pose retains its flight anchor instead of blending toward the terminal correction");
            Check(buffer.TryTakeIfReady(impactTime, out next) && ReferenceEquals(next, impact), "impact action executes at its recorded time");
            buffer.MarkActionExecuted(next); ReplayEventFactory.Return(next);
            Check(buffer.TryTakeIfReady(impactTime, out next) && ReferenceEquals(next, terminal), "post-impact sample releases only after the terminal action");
            ReplayEventFactory.Return(next); buffer.ClearAndReturnToPool();
            Check(buffer.FreeSpace == 8, "impact and held samples release every capacity slot");
        });
        Test("one missile impact does not starve another actor's lookahead", () =>
        {
            var buffer = new BoundedEventBuffer(8);
            buffer.Add(Impact(7, 2), default); buffer.Add(Move(7, 2.1, -100), default);
            var other = Move(8, 2.2, 50); buffer.Add(other, default);
            Check(buffer.TryTakeIfReady(1.9, out var next) && ReferenceEquals(next, other), "unrelated actor retains future interpolation");
            ReplayEventFactory.Return(next);
            Check(!buffer.TryTakeIfReady(1.9, out _), "only impacted actor's terminal samples wait");
            buffer.ClearAndReturnToPool(); Check(buffer.Count == 0 && buffer.FreeSpace == 8, "held terminal sample clears once");
        });
        Test("same-time flight and terminal samples preserve impact file ordering", () =>
        {
            var buffer = new BoundedEventBuffer(8);
            var flight = Move(7, 2, 10); var impact = Impact(7, 2); var terminal = Move(7, 2, -100);
            buffer.Add(flight, default); buffer.Add(impact, default); buffer.Add(terminal, default);
            Check(buffer.TryTakeIfReady(1.99, out var next) && ReferenceEquals(next, flight), "genuine flight sample before equal-time impact may look ahead");
            ReplayEventFactory.Return(next);
            Check(!buffer.TryTakeIfReady(1.99, out _), "equal-time post-impact correction stays behind action");
            Check(buffer.TryTakeIfReady(2, out next) && ReferenceEquals(next, impact), "equal-time impact precedes correction");
            buffer.MarkActionExecuted(next); ReplayEventFactory.Return(next);
            Check(buffer.TryTakeIfReady(2, out next) && ReferenceEquals(next, terminal), "equal-time correction releases after impact");
            ReplayEventFactory.Return(next); buffer.ClearAndReturnToPool();
        });
        Test("post-impact seek checkpoints and clones omit the invisible native missile root", () =>
        {
            using var state = new ReplaySeekState(true);
            state.Add(Spawn(7, 0)); state.Add(Move(7, 1, 10)); state.Add(Status(7, 1.1));
            using var before = state.Clone();
            state.Add(Impact(7, 2)); state.Add(Move(7, 2.1, -100)); state.Add(Status(7, 2.2));
            using var after = state.Clone();
            var past = Snapshot(before); var present = Snapshot(state); var cloned = Snapshot(after);
            try
            {
                Check(past.OfType<SpawnEvent>().Single().unitId == 7 && past.OfType<MissileStatusEvent>().Single().unitId == 7,
                    "pre-impact checkpoint owns independent flight state");
                Check(present.Count == 0 && cloned.Count == 0 && state.EstimatedBytes == 0 && after.EstimatedBytes == 0,
                    "post-impact samples cannot resurrect a mesh, motor, explosion or checkpoint allocation");
            }
            finally { Return(past); Return(present); Return(cloned); }
        });
        Test("impact termination resets for reused missile IDs including equal-time respawns", () =>
        {
            using var state = new ReplaySeekState();
            state.Add(Spawn(7, 0)); state.Add(Impact(7, 2)); state.Add(Spawn(7, 2, "replacement"));
            state.Add(Move(7, 1.9, -999)); state.Add(Status(7, 1.9)); state.Add(Move(7, 2.1, 25));
            var result = Snapshot(state);
            try
            {
                Check(result.OfType<SpawnEvent>().Single().jsonKey == "replacement", "equal-time replacement lifetime survives");
                Check(result.OfType<UpdatePositionEvent>().Single().position.x == 25 && !result.OfType<MissileStatusEvent>().Any(),
                    "only replacement poses survive and older motor state remains absent");
                Check(!result.OfType<MissileDetonationEvent>().Any(), "old impact does not attach to replacement");
            }
            finally { Return(result); }
        });
        Test("rolling cuts before impact cannot interpolate their opening pose into terminal correction", () =>
        {
            using var ring = new RollingReplayBuffer(10);
            ring.Add(Spawn(7, 0)); ring.Add(Move(7, 1, 10)); ring.Add(Impact(7, 2)); ring.Add(Move(7, 2.1, -100));
            var result = Decode(ring.Snapshot(11.5));
            try
            {
                Check(Math.Abs(ring.CutTime - 1.5) < .000001, "cut is in the last flight interval");
                Check(result.OfType<SpawnEvent>().Single().pos.x == 10, "future post-impact sample cannot pull cut baseline backwards");
                Check(result.OfType<UpdatePositionEvent>().Single(e => e.Time == 0).position.x == 10, "cut pose keeps its valid pre-impact anchor");
                Check(result.OfType<MissileDetonationEvent>().Single().Time == .5, "real future impact keeps its rebased time");
            }
            finally { Return(result); }
        });
        Test("rolling cuts after impact retain surviving actors without respawning expired missiles", () =>
        {
            using var ring = new RollingReplayBuffer(10);
            ring.Add(Spawn(8, 0, "survivor")); ring.Add(Spawn(7, .1)); ring.Add(Move(7, 1, 10));
            ring.Add(Impact(7, 2)); ring.Add(Move(7, 2.1, -100)); ring.Add(Status(7, 2.2));
            var first = Decode(ring.Snapshot(12.5)); var second = Decode(ring.Snapshot(12.5));
            try
            {
                foreach (var result in new[] {first, second})
                {
                    Check(result.OfType<SpawnEvent>().Single().unitId == 8, "only surviving actor opens the clip");
                    Check(!result.OfType<UpdatePositionEvent>().Any() && !result.OfType<MissileStatusEvent>().Any() &&
                        !result.OfType<MissileDetonationEvent>().Any(), "expired missile root, motor and historical burst stay absent");
                }
            }
            finally { Return(first); Return(second); }
        });
        Test("rolling cuts exactly at impact execute terminal action after the zero-time spawn", () =>
        {
            using var ring = new RollingReplayBuffer(10);
            ring.Add(Spawn(7, 0)); ring.Add(Move(7, 1, 10)); ring.Add(Impact(7, 2)); ring.Add(Move(7, 2.1, -100));
            var result = Decode(ring.Snapshot(12));
            try
            {
                var spawn = result.OfType<SpawnEvent>().Single(); var impact = result.OfType<MissileDetonationEvent>().Single();
                Check(spawn.Time == 0 && impact.Time == 0 && result.IndexOf(spawn) < result.IndexOf(impact), "terminal action applies in the first frame after spawn");
                Check(spawn.pos.x == 10, "opening spawn does not use post-impact correction");
                using var state = new ReplaySeekState();
                foreach (var item in result.Where(e => e.Time == 0)) state.Add(RollingReplayBuffer.Decode(RollingReplayBuffer.Encode(item)));
                var atCut = Snapshot(state);
                try { Check(atCut.Count == 0, "zero-time impact leaves no visible missile in restored cut frame"); }
                finally { Return(atCut); }
            }
            finally { Return(result); }
        });
        Console.WriteLine($"{passed} missile impact regression groups passed");
        return passed;
        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    }

    private static SpawnEvent Spawn(uint id, double time, string key = "test-missile")
        => new SpawnEvent {unitId=id, ownerId=id, Time=time, jsonKey=key, factionName="test", rotation=new Quaternion {w=1}};
    private static UpdatePositionEvent Move(uint id, double time, float x, float y=0, float z=0)
        => new UpdatePositionEvent {unitId=id, Time=time, position=new GlobalPosition(x,y,z), rotation=new Quaternion {w=1}, velocity=new Vector3(400,0,0)};
    private static MissileStatusEvent Status(uint id, double time)
        => new MissileStatusEvent {unitId=id, Time=time, stage=0, age=1, throttle=1, thrust=100, fuel=new[] {1f}, delay=new[] {0f}, activated=new[] {true}};
    private static MissileDetonationEvent Impact(uint id, double time)
        => new MissileDetonationEvent {unitId=id, Time=time, position=new GlobalPosition(10691.23535f,472.147827f,15676.66699f), normal=new Vector3(0,1,0), armed=true};
    private static List<IReplayEvent> Snapshot(ReplaySeekState state) { var queue=new Queue<IReplayEvent>(); state.CopyTo(queue); return queue.ToList(); }
    private static List<IReplayEvent> Decode(List<byte[]> data) => data.Select(RollingReplayBuffer.Decode).ToList();
    private static void Return(IEnumerable<IReplayEvent> values) { foreach (var item in values) ReplayEventFactory.Return(item); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
