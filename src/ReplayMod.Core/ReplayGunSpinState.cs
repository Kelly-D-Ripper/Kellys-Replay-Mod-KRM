using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

// Value-owned, bounded cosmetic state. Ordinary recordings already contain the
// shots, so a seek index derives this without changing or re-recording the file.
public sealed class ReplayGunSpinState
{
    public const int MaximumGuns = 8192;
    private readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>();
    private long estimatedBytes;
    private sealed class Track
    {
        internal GunSpinStateEvent address;
        internal readonly ReplayGunSpin spin = new ReplayGunSpin();
    }
    public int Count => tracks.Count;
    public long EstimatedBytes => estimatedBytes;
    private static string Key(GunSpinStateEvent value) => value.unitId + (value.legacyAddress ? ":legacy:" + value.legacyGunIndex :
        ":gun:" + value.station + ":" + value.weaponSlot + ":" + value.partId + ":" + value.partGunSlot + ":" + value.weaponName);
    private static long Estimate(string key, GunSpinStateEvent value) => 640L + key.Length * 2L + value.weaponName.Length * 2L;

    public void Add(GunBulletEvent shot)
    {
        if (!shot.sound || !shot.HasValidPayload()) return;
        var value = new GunSpinStateEvent(); value.CopyFrom(shot); AddShot(value, shot.Time);
    }
    public void Add(BulletEvent shot)
    {
        if (shot.unitId == 0 || double.IsNaN(shot.Time) || double.IsInfinity(shot.Time) || shot.Time < 0) return;
        AddShot(new GunSpinStateEvent {unitId = shot.unitId, legacyAddress = true, legacyGunIndex = shot.gunIndex}, shot.Time);
    }
    private void AddShot(GunSpinStateEvent address, double time)
    {
        var track = Find(address); track?.spin.RecordShot(time);
    }
    public void Add(GunSpinStateEvent value)
    {
        if (!value.HasValidPayload()) return;
        var track = Find(value);
        if (track != null && (!track.spin.HasState || value.Time >= track.spin.Time)) track.spin.Restore(value.Time, value.phase, value.speed);
    }
    private Track Find(GunSpinStateEvent value)
    {
        string key = Key(value);
        if (tracks.TryGetValue(key, out var track)) return track;
        // Running out of cosmetic checkpoint budget must not fault the replay.
        if (tracks.Count >= MaximumGuns) return null;
        var address = new GunSpinStateEvent
        {
            unitId = value.unitId, legacyAddress = value.legacyAddress, legacyGunIndex = value.legacyGunIndex,
            station = value.station, weaponSlot = value.weaponSlot, partId = value.partId, partGunSlot = value.partGunSlot,
            weaponName = value.weaponName ?? ""
        };
        tracks[key] = track = new Track {address = address}; estimatedBytes += Estimate(key, address); return track;
    }
    public void Forget(uint id)
    {
        foreach (var key in tracks.Where(pair => pair.Value.address.unitId == id).Select(pair => pair.Key).ToArray())
        { estimatedBytes -= Estimate(key, tracks[key].address); tracks.Remove(key); }
    }
    public void CopyTo(Queue<IReplayEvent> destination)
    {
        foreach (var track in tracks.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value))
        {
            var value = ReplayEventFactory.GetEvent<GunSpinStateEvent>(); var address = track.address;
            value.unitId = address.unitId; value.legacyAddress = address.legacyAddress; value.legacyGunIndex = address.legacyGunIndex;
            value.station = address.station; value.weaponSlot = address.weaponSlot; value.partId = address.partId;
            value.partGunSlot = address.partGunSlot; value.weaponName = address.weaponName;
            value.Time = track.spin.Time; value.phase = track.spin.Phase; value.speed = track.spin.Speed;
            destination.Enqueue(value);
        }
    }
    public void CloneTo(ReplayGunSpinState destination)
    {
        var copy = new Queue<IReplayEvent>(); CopyTo(copy);
        try { foreach (var value in copy) destination.Add((GunSpinStateEvent)value); }
        finally { while (copy.Count > 0) ReplayEventFactory.Return(copy.Dequeue()); }
    }
    public void Clear() { tracks.Clear(); estimatedBytes = 0; }
}
