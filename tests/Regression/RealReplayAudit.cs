using System.Diagnostics;
using System.Security.Cryptography;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

// Optional read-only integration audit of real replay files. Production codecs,
// reducer and checkpoint cache run unchanged; Unity objects are not created.
public static class RealReplayAudit
{
    public static void Run(string[] paths)
    {
        Encyclopedia.WeaponLookup.ResolveNamesOnly = true;
        try { foreach (var path in paths) Audit(path); }
        finally { Encyclopedia.WeaponLookup.ResolveNamesOnly = false; }
        Console.WriteLine($"Real replay audit: {paths.Length} files passed (read-only CPU checks; Unity acceptance pending)");
    }

    public static void AuditGenerated(string path) => Audit(path, 34);

    private static void Audit(string path, int restorePasses = 1)
    {
        string before = FileHash(path);
        var watch = Stopwatch.StartNew();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        var header = ReadHeader(reader);
        var validation = new ReplayTimelineValidation(header.Duration, header.Count, header.Format < 3);
        using var state = new ReplaySeekState();
        using var cache = new SeekCheckpointCache<ReplaySeekState>(32L * 1024 * 1024, 64, 30, s => s.Dispose());
        var targets = new[] { 0d, header.Duration * .2, header.Duration * .5, header.Duration * .8, header.Duration * .99, header.Duration };
        var expected = new string[targets.Length];
        int targetIndex = 0;
        double previous = -1;
        long duplicateTimes = 0;
        long groupCount = 0, largestGroup = 0;
        var counts = new Dictionary<EventType, long>();
        while (stream.Position < stream.Length)
        {
            long offset = stream.Position;
            var e = ReplayRecordTransport.ReadEvent(reader,header.Format);
            bool owned = true;
            try
            {
                validation.Add(e.Time, e is UpdateTurretEvent);
                counts[e.EventType] = counts.TryGetValue(e.EventType, out long count) ? count + 1 : 1;
                if (e.Time == previous) { duplicateTimes++; groupCount++; }
                if (e.Time > previous)
                {
                    largestGroup = Math.Max(largestGroup, groupCount); groupCount = 1;
                    // Offset begins the next complete timestamp group.
                    if (header.Format >= 3 && previous >= 0 && cache.ShouldAdd(previous, state.EstimatedBytes))
                        cache.Add(previous, offset, state.EstimatedBytes, state.Clone());
                    while (targetIndex < targets.Length && targets[targetIndex] < e.Time)
                        expected[targetIndex++] = SnapshotHash(state);
                }
                previous = e.Time;
                owned = false; state.Add(e);
            }
            finally { if (owned) ReplayEventFactory.Return(e); }
        }
        validation.Complete();
        largestGroup = Math.Max(largestGroup, groupCount);
        while (targetIndex < targets.Length) expected[targetIndex++] = SnapshotHash(state);

        long suffixEvents = 0, fullPrefixEvents = 0;
        for (int pass = 0; pass < restorePasses; pass++)
        for (int destination = 0; destination < targets.Length; destination++)
        {
            int i = pass % 2 == 0 ? destination : targets.Length - 1 - destination;
            // Legacy delta input histories require sequential decoding. Keep the
            // independent audit conservative and start these formats at the header.
            var checkpoint = header.Format >= 3 ? cache.Find(targets[i]) : null;
            using var restored = checkpoint?.State.Clone() ?? new ReplaySeekState();
            using var suffixStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var suffixReader = new BinaryReader(suffixStream);
            ReadHeader(suffixReader);
            suffixStream.Position = checkpoint?.Offset ?? header.DataOffset;
            while (suffixStream.Position < suffixStream.Length)
            {
                var e = ReplayRecordTransport.ReadEvent(suffixReader,header.Format);
                if (e.Time > targets[i]) { ReplayEventFactory.Return(e); break; }
                suffixEvents++; restored.Add(e);
            }
            string actual = SnapshotHash(restored);
            if (actual != expected[i]) throw new Exception($"Checkpoint restoration differs from full prefix at {targets[i]:R}s in {Path.GetFileName(path)}");
        }
        // Independent full-prefix event counts quantify skipped decoding without
        // equating CPU audit time to runtime scene loading or game FPS.
        using (var countStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var countReader = new BinaryReader(countStream))
        {
            ReadHeader(countReader);
            while (countStream.Position < countStream.Length)
            {
                var e = ReplayRecordTransport.ReadEvent(countReader,header.Format);
                try { fullPrefixEvents += targets.Count(t => t >= e.Time); }
                finally { ReplayEventFactory.Return(e); }
            }
        }
        string after = FileHash(path);
        if (before != after) throw new Exception("Read-only audit input changed: " + path);
        Console.WriteLine($"PASS real replay {Path.GetFileName(path)}: format {header.Format}, {validation.Count} events, {header.Duration:F3}s, {duplicateTimes} duplicate timestamps, largest timestamp group {largestGroup}, SHA256 {before}");
        Console.WriteLine($"PASS checkpoint/full-prefix equivalence: {targets.Length*restorePasses} restorations, {targets.Length} destinations, {cache.Count} checkpoints, {cache.Bytes} estimated retained bytes, {suffixEvents} suffix events vs {fullPrefixEvents*restorePasses} full-prefix events, {watch.ElapsedMilliseconds} ms CPU/file audit");
        Console.WriteLine("Event counts: " + string.Join(", ", counts.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")));
    }

    private static string SnapshotHash(ReplaySeekState state)
    {
        var queue = new Queue<IReplayEvent>(); state.CopyTo(queue);
        var payloads = new List<byte[]>();
        try
        {
            while (queue.Count > 0)
            {
                var e = queue.Dequeue();
                try { payloads.Add(RollingReplayBuffer.Encode(e)); }
                finally { ReplayEventFactory.Return(e); }
            }
        }
        finally { while (queue.Count > 0) ReplayEventFactory.Return(queue.Dequeue()); }
        // Canonical ordering ignores dictionary insertion order at tied times.
        payloads.Sort((a, b) => a.AsSpan().SequenceCompareTo(b));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var payload in payloads) { hash.AppendData(BitConverter.GetBytes(payload.Length)); hash.AppendData(payload); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static (byte Format, double Duration, long Count, long DataOffset) ReadHeader(BinaryReader r)
    {
        var header=ReplayRecordingHeader.Read(r);
        return (header.Format,header.Duration,header.EventCount,header.DataOffset);
    }
    private static string FileHash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
}
