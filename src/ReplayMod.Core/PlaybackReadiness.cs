using System;
using System.IO;

namespace ReplayMod.Core;

// An enqueued timestamp closes only the timestamps strictly before it. Events
// with identical timestamps may still be arriving from the producer.
public static class PlaybackReadiness
{
    public const double LookAheadSeconds = 4;
    public static bool CanCommit(double target, double enqueuedThrough, bool finished, bool capacityReached)
    {
        if (finished) return true;
        if (enqueuedThrough <= target) return false;
        return enqueuedThrough > target + LookAheadSeconds || capacityReached;
    }

    public static int UpperBound(double[] times, double target)
    {
        int lo=0, hi=times.Length;
        while(lo<hi) { int mid=lo+(hi-lo)/2; if(times[mid]<=target)lo=mid+1;else hi=mid; }
        return lo;
    }
}

public sealed class ReplayTimelineValidation
{
    private readonly double duration;
    private readonly long expectedCount;
    private readonly bool allowLegacyTurretCount;
    private long legacyTurretCount;
    private double previous=-1;
    public long Count { get; private set; }
    public double LastTime => previous;
    public bool UsedLegacyCount => Count!=expectedCount;
    public ReplayTimelineValidation(double duration,long expectedCount,bool allowLegacyTurretCount=false)
    {
        if(double.IsNaN(duration)||double.IsInfinity(duration)||duration<0)
            throw new InvalidDataException("Replay duration must be a finite, nonnegative number");
        if(expectedCount<0)throw new InvalidDataException("Replay event count is negative");
        this.duration=duration;this.expectedCount=expectedCount;this.allowLegacyTurretCount=allowLegacyTurretCount;
    }
    public void Add(double time,bool legacyTurret=false)
    {
        if(double.IsNaN(time)||double.IsInfinity(time)||time<0)
            throw new InvalidDataException($"Replay event {Count+1} has an invalid timestamp");
        if(time<previous)throw new InvalidDataException($"Replay event {Count+1} is out of time order ({time:R} after {previous:R})");
        if(time>duration+0.000001)throw new InvalidDataException($"Replay event {Count+1} exceeds the recorded duration ({time:R} > {duration:R})");
        previous=time;Count++;
        if(legacyTurret)legacyTurretCount++;
        if(!allowLegacyTurretCount&&Count>expectedCount)throw new InvalidDataException("Replay contains more events than its completed-file header declares");
    }
    public void Complete()
    {
        if(Count!=expectedCount&&(!allowLegacyTurretCount||Count-legacyTurretCount!=expectedCount))
            throw new InvalidDataException($"Replay is incomplete: expected {expectedCount} events, read {Count}");
    }
}
