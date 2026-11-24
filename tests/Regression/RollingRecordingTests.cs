using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class RollingRecordingTests
{
    public static int Run()
    {
        TestRetentionChanges();
        Console.WriteLine("PASS recent recording retention changes preserve the live baseline");
        TestSaveIndependence();
        Console.WriteLine("PASS saved clip snapshots survive continuous recording and disposal");
        TestMemoryLimitReporting();
        Console.WriteLine("PASS recent clip reports when the event memory cap shortens its duration");
        TestInvalidRetention();
        Console.WriteLine("PASS recent recording rejects invalid retention settings");
        return 4;
    }

    private static void TestRetentionChanges()
    {
        using var ring = new RollingReplayBuffer(120);
        ring.Add(new SpawnEvent { Time = 0, unitId = 1, ownerId = 1, jsonKey = "jet", factionName = "blue" });
        for (int second = 1; second <= 180; second++)
        {
            ring.Add(new UpdatePositionEvent { Time = second, unitId = 1, position = new GlobalPosition(second, 0, 0) });
            if (second == 5) ring.Add(new PartDamageEvent { Time = 5, unitId = 1, partId = 2, hitPoints = 35 });
        }
        Check(ring.CutTime == 60, "initial 120-second window");
        ring.SetWindow(30, 180);
        var data = Decode(ring.Snapshot(180));
        Check(ring.CutTime == 150, "lower retention applies immediately");
        Check(data.OfType<SpawnEvent>().Single().pos.x == 150, "exact cut position forms the baseline");
        Check(data.OfType<PartDamageEvent>().Single().hitPoints == 35, "baseline damage preserved");
        Check(data.Max(e => e.Time) == 30, "saved clip spans the new window");
        Return(data);
        ring.SetWindow(300, 180);
        Check(ring.CutTime == 150, "raising retention does not invent already discarded history");
        Check(!ring.MemoryLimited, "history discarded by a shorter setting is not reported as a memory limit");
        ring.Add(new UpdatePositionEvent { Time = 210, unitId = 1, position = new GlobalPosition(210, 0, 0) });
        Check(210 - ring.CutTime == 60, "longer window fills while recording continues");
    }

    private static void TestSaveIndependence()
    {
        var ring = new RollingReplayBuffer(30);
        ring.Add(new SpawnEvent { unitId = 2, ownerId = 2, jsonKey = "aircraft", factionName = "blue" });
        ring.Add(new UpdateInputsEvent { Time = 2, id = 2, throttle = .5f });
        ring.Add(new LandingEvent { Time = 35, unitId = 2 });
        var saved = ring.Snapshot(35);
        byte[][] original = saved.Select(bytes => bytes.ToArray()).ToArray();
        ring.Add(new UpdateInputsEvent { Time = 36, id = 2, throttle = .9f });
        ring.Add(new LandingEvent { Time = 70, unitId = 2 });
        var next = Decode(ring.Snapshot(70));
        Check(next.OfType<UpdateInputsEvent>().Single().throttle > .89, "second save contains recent state");
        Return(next);
        ring.Dispose();
        Check(saved.Zip(original, (a, b) => a.SequenceEqual(b)).All(value => value), "background save bytes remain immutable");
        var previous = Decode(saved);
        Check(previous.OfType<UpdateInputsEvent>().Single().throttle > .49 && previous.OfType<UpdateInputsEvent>().Single().throttle < .51, "old save keeps original throttle");
        Check(previous.OfType<LandingEvent>().Single().Time == 30, "old save keeps its rebased endpoint");
        Return(previous);
    }

    private static void TestMemoryLimitReporting()
    {
        using var ring = new RollingReplayBuffer(300, 1024);
        ring.Add(new SpawnEvent { unitId = 3, ownerId = 3, jsonKey = "jet", factionName = "blue" });
        for (int n = 0; n < 100; n++)
            ring.Add(new UpdatePositionEvent { Time = n, unitId = 3, position = new GlobalPosition(n, 0, 0) });
        Check(ring.Bytes <= 1024, "event bytes bounded");
        Check(ring.MemoryLimited && ring.CutTime > 0, "cap reports shorter clip rather than pretending the requested window is available");
        ring.Trim(450);
        Check(!ring.MemoryLimited && ring.CutTime == 150, "report clears after ordinary time retention catches up");
    }

    private static void TestInvalidRetention()
    {
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -10, 0, 9.9 })
        {
            bool rejected = false;
            try { using var invalid = new RollingReplayBuffer(value); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "constructor rejects invalid window");
            using var ring = new RollingReplayBuffer(30);
            rejected = false;
            try { ring.SetWindow(value, 0); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "runtime changes reject invalid window");
        }
    }

    private static IReplayEvent[] Decode(List<byte[]> bytes) => bytes.Select(RollingReplayBuffer.Decode).ToArray();
    private static void Return(IEnumerable<IReplayEvent> events) { foreach (var e in events) ReplayEventFactory.Return(e); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
