using System;
using System.Linq;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

internal static class ReplayGunIdentity
{
    internal static UnitPart FindPart(Unit unit, Gun gun)
    {
        if (gun.hardpoint?.part != null && gun.hardpoint.part.parentUnit == unit) return gun.hardpoint.part;
        for (var transform = gun.transform; transform != null; transform = transform.parent)
        {
            var part = transform.GetComponent<UnitPart>();
            if (part != null && part.parentUnit == unit) return part;
        }
        return null;
    }

    internal static Gun[] PartGuns(Unit unit, UnitPart part) => part.GetComponentsInChildren<Gun>(true)
        .Where(g => g != null && g.attachedUnit == unit && FindPart(unit, g) == part).ToArray();

    internal static bool Capture(Unit unit, Gun gun, GunBulletEvent e)
    {
        if (unit == null || gun == null || gun.attachedUnit != unit) return false;
        e.weaponName = gun.info?.weaponName ?? "";
        if (System.Text.Encoding.UTF8.GetByteCount(e.weaponName) > 512) return false;
        if (unit.weaponStations != null)
            foreach (var station in unit.weaponStations)
            {
                if (station?.Weapons == null) continue;
                int index = station.Weapons.IndexOf(gun);
                if (index >= 0 && index <= ushort.MaxValue) { e.station = station.Number; e.weaponSlot = (ushort)index; break; }
            }
        var part = FindPart(unit, gun);
        if (part != null)
        {
            int index = Array.IndexOf(PartGuns(unit, part), gun);
            if (index >= 0 && index <= ushort.MaxValue) { e.partId = part.id; e.partGunSlot = (ushort)index; }
        }
        return e.station != ushort.MaxValue || e.partId >= 0;
    }

    internal static bool Matches(Unit unit, Gun gun, GunBulletEvent e) => gun != null && gun.attachedUnit == unit &&
        StringComparer.Ordinal.Equals(gun.info?.weaponName ?? "", e.weaponName) &&
        (e.partId < 0 || FindPart(unit, gun)?.id == e.partId);

    internal static Gun Resolve(Unit unit, GunBulletEvent e)
    {
        // A native weapon station may sort identical guns. Prefer the recorded
        // part-local component slot, which is independent of that station order.
        if (e.partId >= 0 && unit.partLookup != null)
        {
            var part = unit.partLookup.FirstOrDefault(p => p != null && p.id == e.partId);
            if (part != null)
            {
                var guns = PartGuns(unit, part);
                if (e.partGunSlot < guns.Length && Matches(unit, guns[e.partGunSlot], e)) return guns[e.partGunSlot];
            }
            return null;
        }
        if (e.partId < 0 && e.station != ushort.MaxValue && unit.weaponStations != null)
        {
            var station = unit.weaponStations.FirstOrDefault(s => s != null && s.Number == e.station);
            if (station?.Weapons != null && e.weaponSlot < station.Weapons.Count && station.Weapons[e.weaponSlot] is Gun gun && Matches(unit, gun, e)) return gun;
        }
        return null; // Do not fire a different gun when a modded loadout changed.
    }
}

internal readonly struct ReplayGunAddressKey : IEquatable<ReplayGunAddressKey>
{
    private readonly ushort station, weaponSlot, partSlot;
    private readonly short part;
    private readonly string name;
    internal ReplayGunAddressKey(GunBulletEvent e)
    { station = e.station; weaponSlot = e.weaponSlot; partSlot = e.partGunSlot; part = e.partId; name = e.weaponName; }
    public bool Equals(ReplayGunAddressKey other) => station == other.station && weaponSlot == other.weaponSlot && partSlot == other.partSlot && part == other.part && StringComparer.Ordinal.Equals(name, other.name);
    public override bool Equals(object value) => value is ReplayGunAddressKey other && Equals(other);
    public override int GetHashCode()
    {
        unchecked { int result = station; result = result * 397 ^ weaponSlot; result = result * 397 ^ partSlot; result = result * 397 ^ part; return result * 397 ^ (name == null ? 0 : StringComparer.Ordinal.GetHashCode(name)); }
    }
}
