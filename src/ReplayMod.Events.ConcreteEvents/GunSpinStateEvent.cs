using System;
using System.IO;
using System.Text;
using ReplayMod.Core;

namespace ReplayMod.Events.ConcreteEvents;

// Compact presentation-only seek/rolling anchor, derived from the recorded
// shots. Phase is the integral of normalized spin speed; multiply by the native
// gun's spinRate to obtain degrees. This never recreates historical projectiles.
[ReplayEvent(EventType.GunSpinState)]
public sealed class GunSpinStateEvent : IReplayEvent
{
    public uint unitId;
    public bool legacyAddress;
    public ushort legacyGunIndex;
    public ushort station = ushort.MaxValue, weaponSlot, partGunSlot;
    public short partId = -1;
    public string weaponName = "";
    public double phase, speed;
    public double Time { get; set; }
    public EventType EventType => EventType.GunSpinState;
    public void Execute(object worker = null) { if (worker is UnitController controller) controller.Execute(this); }

    public bool HasValidPayload() => unitId != 0 && ReplayGunSpin.Valid(Time, phase, speed) &&
        (legacyAddress || ((station == ushort.MaxValue || station <= byte.MaxValue) && partId >= -1 && partId <= byte.MaxValue &&
            (station != ushort.MaxValue || partId >= 0))) && Encoding.UTF8.GetByteCount(weaponName ?? "") <= 512;

    public void Write(BinaryWriter writer)
    {
        if (!HasValidPayload()) throw new InvalidDataException("Invalid replay gun spin state");
        byte[] name = Encoding.UTF8.GetBytes(weaponName ?? "");
        writer.Write(Time); writer.Write(unitId); writer.Write(legacyAddress); writer.Write(legacyGunIndex);
        writer.Write(station); writer.Write(weaponSlot); writer.Write(partId); writer.Write(partGunSlot);
        writer.Write((ushort)name.Length); writer.Write(name); writer.Write(phase); writer.Write(speed);
    }

    public void Read(BinaryReader reader)
    {
        Time = reader.ReadDouble(); unitId = reader.ReadUInt32(); legacyAddress = reader.ReadBoolean(); legacyGunIndex = reader.ReadUInt16();
        station = reader.ReadUInt16(); weaponSlot = reader.ReadUInt16(); partId = reader.ReadInt16(); partGunSlot = reader.ReadUInt16();
        int length = reader.ReadUInt16();
        if (length > 512) throw new InvalidDataException("Replay gun name exceeds limit");
        byte[] name = reader.ReadBytes(length); if (name.Length != length) throw new EndOfStreamException();
        weaponName = Encoding.UTF8.GetString(name); phase = reader.ReadDouble(); speed = reader.ReadDouble();
        if (!HasValidPayload()) throw new InvalidDataException("Invalid replay gun spin state");
    }

    public void CopyFrom(GunBulletEvent shot)
    {
        unitId = shot.unitId; station = shot.station; weaponSlot = shot.weaponSlot; partId = shot.partId;
        partGunSlot = shot.partGunSlot; weaponName = shot.weaponName ?? ""; legacyAddress = false; legacyGunIndex = 0;
    }

    public void CopyTo(GunBulletEvent address)
    {
        address.unitId = unitId; address.station = station; address.weaponSlot = weaponSlot; address.partId = partId;
        address.partGunSlot = partGunSlot; address.weaponName = weaponName;
    }

    public void RebaseAt(double sampleTime, double newTime)
    {
        var spin = new ReplayGunSpin();
        if (!spin.Restore(Time, phase, speed) || double.IsNaN(sampleTime) || double.IsInfinity(sampleTime) || sampleTime < Time ||
            double.IsNaN(newTime) || double.IsInfinity(newTime) || newTime < 0) throw new InvalidDataException("Invalid replay gun spin cut");
        phase = spin.PhaseAt(sampleTime); speed = spin.SpeedAt(sampleTime); Time = newTime;
    }

    public void Reset()
    {
        Time = 0; unitId = 0; legacyAddress = false; legacyGunIndex = 0; station = ushort.MaxValue;
        weaponSlot = partGunSlot = 0; partId = -1; weaponName = ""; phase = speed = 0;
    }
}
