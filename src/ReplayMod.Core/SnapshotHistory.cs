using System;
using System.Collections.Generic;

namespace ReplayMod.Core;

// Shared by actor pose, controls and turret histories. Samples can arrive from a
// restored past anchor plus producer lookahead, so insertion must remain sorted.
public static class SnapshotHistory
{
    public static bool Insert<T>(List<T> samples, T sample, Func<T, double> timestamp)
    {
        double time = timestamp(sample);
        if (double.IsNaN(time) || double.IsInfinity(time)) return false;
        int low = 0, high = samples.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (timestamp(samples[mid]) < time) low = mid + 1; else high = mid;
        }
        if (low < samples.Count && timestamp(samples[low]) == time) samples[low] = sample;
        else samples.Insert(low, sample);
        return true;
    }

    public static bool Locate<T>(List<T> samples, double time, Func<T, double> timestamp,
        out int left, out int right, out float fraction)
    {
        left = right = -1; fraction = 0;
        if (double.IsNaN(time) || double.IsInfinity(time)) return false;
        int low = 0, high = samples.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (timestamp(samples[mid]) <= time) low = mid + 1; else high = mid;
        }
        left = low - 1;
        if (left < 0) return false;
        right = Math.Min(left + 1, samples.Count - 1);
        double start = timestamp(samples[left]), end = timestamp(samples[right]);
        if (right != left && end > start)
            fraction = (float)Math.Max(0, Math.Min(1, (time - start) / (end - start)));
        return true;
    }

    public static void Trim<T>(List<T> samples, int left, int threshold)
    { if (left >= threshold && left > 1) samples.RemoveRange(0, left - 1); }

    public static float InterpolateAngle(float from, float to, float fraction)
    {
        float delta = (to - from) % 360f;
        if (delta > 180f) delta -= 360f; else if (delta < -180f) delta += 360f;
        return from + delta * Math.Max(0, Math.Min(1, fraction));
    }
}
