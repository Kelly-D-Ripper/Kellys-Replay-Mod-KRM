using System;
using System.Collections.Generic;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class Recorder
{
    private struct RecordedGunAddress
    {
        internal ushort station, slot, partSlot;
        internal short part;
        internal string name;
        internal RecordedGunAddress(GunBulletEvent e) { station = e.station; slot = e.weaponSlot; partSlot = e.partGunSlot; part = e.partId; name = e.weaponName; }
        internal void CopyTo(GunBulletEvent e) { e.station = station; e.weaponSlot = slot; e.partGunSlot = partSlot; e.partId = part; e.weaponName = name; }
    }
    private readonly Dictionary<Gun, RecordedGunAddress> _recordedGunAddresses = new Dictionary<Gun, RecordedGunAddress>();
    private readonly Dictionary<Gun, bool> _gunShotSounds = new Dictionary<Gun, bool>();
    private readonly HashSet<Gun> _invalidGunSnapshots = new HashSet<Gun>();
    public void BeginGunShot(Gun gun)
    {
        if (!_acceptEvents || gun == null) return;
        _gunShotSounds[gun] = true;
        // Some guns, including the Argus 127 mm, launch a guided projectile
        // instead of inserting a BulletSim round. The separately recorded
        // projectile supplies motion; retain the gun's sound and muzzle flash.
        GunBulletEvent e=null;
        try
        {
            if (gun.guidedProjectile == null || NuclearOption.Networking.NetworkManagerNuclearOption.i?.Server?.Active != true ||
                !SessionValid || gun.attachedUnit == null || !IsTrackedOrPending(gun.attachedUnit)) return;
            e=ReplayEventFactory.GetEvent<GunBulletEvent>();
            if (_recordedGunAddresses.TryGetValue(gun,out var address)) address.CopyTo(e);
            else
            {
                if (!ReplayGunIdentity.Capture(gun.attachedUnit,gun,e)) return;
                _recordedGunAddresses[gun]=new RecordedGunAddress(e);
            }
            e.Time=_virtualTime;e.unitId=gun.attachedUnit.persistentID.Id;
            var muzzle=gun.muzzles!=null&&gun.muzzles.Length>0&&gun.muzzles[0]!=null?gun.muzzles[0]:gun.transform;
            e.position=muzzle.position.ToGlobalPosition();e.reliability=1;e.sound=true;e.active=false;
            var submitted=e;e=null;QueueEventForUnit(gun.attachedUnit,submitted);
        }
        catch(Exception error)
        {
            CaptureFailureCount++;
            if(_invalidGunSnapshots.Add(gun))Plugin.logger.LogWarning("Could not record guided gun presentation; gameplay continues: "+error);
        }
        finally{if(e!=null)ReplayEventFactory.Return(e);}
    }
    public void EndGunShot(Gun gun) { if (gun != null) _gunShotSounds.Remove(gun); }
    private void ClearRecordingGuns() { _gunShotSounds.Clear(); _recordedGunAddresses.Clear(); _invalidGunSnapshots.Clear(); }
    private void ForgetRecordingGuns(Unit unit)
    {
        var remove = new List<Gun>();
        foreach (var gun in _gunShotSounds.Keys) if (gun == null || gun.attachedUnit == unit) remove.Add(gun);
        foreach (var gun in remove) _gunShotSounds.Remove(gun);
        remove.Clear();
        foreach (var gun in _recordedGunAddresses.Keys) if (gun == null || gun.attachedUnit == unit) remove.Add(gun);
        foreach (var gun in remove) _recordedGunAddresses.Remove(gun);
        _invalidGunSnapshots.RemoveWhere(gun => gun == null || gun.attachedUnit == unit);
    }
    public void RecordResolvedBullet(BulletSim sim, BulletSim.Bullet bullet, Color color, float tracerSize)
    {
        // Observe the one local BulletSim insertion after native random spread
        // and initial tracing. Do not record both weapon commands and RPCs.
        if (!_acceptEvents || !SessionValid || sim == null || bullet == null || sim.owner == null || sim.gun == null || !IsTrackedOrPending(sim.owner)) return;
        var e = ReplayEventFactory.GetEvent<GunBulletEvent>();
        if (_recordedGunAddresses.TryGetValue(sim.gun, out var address)) address.CopyTo(e);
        else
        {
            if (!ReplayGunIdentity.Capture(sim.owner, sim.gun, e)) { ReplayEventFactory.Return(e); return; }
            _recordedGunAddresses[sim.gun] = new RecordedGunAddress(e);
        }
        e.Time = _virtualTime; e.unitId = sim.owner.persistentID.Id;
        // The native initial trace can expire a bullet below zero. It still
        // represents a real shot, but has no remaining simulated lifetime.
        e.position = bullet.position; e.velocity = bullet.velocity;
        e.life = float.IsNaN(bullet.destructTimer) || float.IsInfinity(bullet.destructTimer) ? bullet.destructTimer : Math.Max(0, bullet.destructTimer);
        e.tracer = bullet.tracer != null; e.color = color; e.tracerSize = tracerSize; e.reliability = bullet.reliability;
        e.active = bullet.active && e.life > 0; e.impacted = bullet.impacted; e.explosive = bullet.explosive;
        e.sound = !_gunShotSounds.TryGetValue(sim.gun, out bool first) || first;
        if (!e.HasValidPayload())
        {
            ReplayEventFactory.Return(e);
            CaptureFailureCount++;
            if (_invalidGunSnapshots.Add(sim.gun)) Plugin.logger.LogWarning("Skipped an invalid recorded gun projectile; the rest of the recording continues.");
            return;
        }
        if (_gunShotSounds.ContainsKey(sim.gun)) _gunShotSounds[sim.gun] = false;
        QueueEventForUnit(sim.owner,e);
    }
}
