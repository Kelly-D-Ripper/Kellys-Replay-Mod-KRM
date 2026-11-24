using System;
using System.Collections.Generic;

namespace ReplayMod.Core;

// Pure presentation decisions shared by the runtime and regression harness.
public static class ReplayVfxPolicy
{
    public static bool KeepAttachedParticles(bool playback, bool ownedParent, bool detachedPart, bool activeParent)
        => playback && ownedParent && !detachedPart && activeParent;

    public static bool ShouldCreateFlame(bool wasOnFire, bool onFire, bool hasLiveFlame)
        => onFire && (!wasOnFire || !hasLiveFlame);

    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public static bool HasNearCameraFade(float nearDistance, float inverseRange)
        => IsFinite(nearDistance) && IsFinite(inverseRange) && nearDistance > 0 && inverseRange > 0;

    public static bool ShouldRestoreDistantFragments(bool ownedBeforeDetach, bool alreadyFragmented)
        => ownedBeforeDetach && !alreadyFragmented;
}

// Captures each owned native setting once. Lifetime cleanup restores every live
// target, and the capacity bounds retention even during an unusually busy replay.
public sealed class ReplayOwnedOverrideCache<T, TState> where T : class
{
    private readonly Dictionary<T, TState> originals = new Dictionary<T, TState>();
    private readonly int capacity;
    public int Count => originals.Count;
    public ReplayOwnedOverrideCache(int capacity)
    { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); this.capacity = capacity; }
    public bool Contains(T target) => target != null && originals.ContainsKey(target);
    public bool TryApply(T target, Func<T, TState> capture, Action<T, TState> apply)
    {
        if (target == null || originals.ContainsKey(target) || originals.Count >= capacity) return false;
        var original = capture(target);
        originals.Add(target, original);
        apply(target, original);
        return true;
    }
    public void Prune(Func<T, bool> alive)
    {
        var remove = new List<T>();
        foreach (var item in originals) if (!alive(item.Key)) remove.Add(item.Key);
        foreach (var target in remove) originals.Remove(target);
    }
    public int Restore(Func<T, bool> alive, Action<T, TState> restore)
    {
        int failures = 0;
        try
        {
            foreach (var item in originals)
                if (alive(item.Key))
                    try { restore(item.Key, item.Value); } catch (Exception) { failures++; }
        }
        finally { originals.Clear(); }
        return failures;
    }
}
