using System;
using System.Collections.Generic;

namespace ReplayMod.Core;

// Replay ownership must not depend on the native radar/map registry. For
// example, Building.hideOnMap prevents UnitRegistry registration entirely.
public sealed class ReplayActorRegistry<T> where T : class
{
    private sealed class Entry
    {
        public uint NativeId;
        public T Actor;
    }
    private readonly Dictionary<uint, Entry> actors = new Dictionary<uint, Entry>();
    private readonly Func<T, bool> alive;
    private readonly Func<T, uint> nativeId;

    public ReplayActorRegistry(Func<T, bool> alive, Func<T, uint> nativeId)
    {
        this.alive = alive ?? throw new ArgumentNullException(nameof(alive));
        this.nativeId = nativeId ?? throw new ArgumentNullException(nameof(nativeId));
    }

    public int Count => actors.Count;

    public void Track(uint recordedId, uint expectedNativeId, T actor)
    {
        if (!alive(actor) || nativeId(actor) != expectedNativeId)
            throw new ArgumentException("Replay actor must belong to its native unit lifetime", nameof(actor));
        actors[recordedId] = new Entry { NativeId = expectedNativeId, Actor = actor };
    }

    public bool TryGet(uint recordedId, uint expectedNativeId, out T actor)
    {
        actor = null;
        if (!actors.TryGetValue(recordedId, out var entry)) return false;
        if (entry.NativeId != expectedNativeId || !alive(entry.Actor) || nativeId(entry.Actor) != expectedNativeId)
        {
            actors.Remove(recordedId);
            return false;
        }
        actor = entry.Actor;
        return true;
    }

    public void Remove(uint recordedId) => actors.Remove(recordedId);
    public void Clear() => actors.Clear();
}
