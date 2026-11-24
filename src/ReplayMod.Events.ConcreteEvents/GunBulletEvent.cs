using System;
using System.IO;
using System.Text;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

// Format 5 resolves a gun through its native station/part identity. Aircraft
// complex-physics parts need not remain below the Unit transform.
[ReplayEvent(EventType.GunBullet)]
public sealed class GunBulletEvent : IReplayEvent
{
    public uint unitId;
    public ushort station = ushort.MaxValue, weaponSlot, partGunSlot;
    public short partId = -1;
    public string weaponName = "";
    public GlobalPosition position;
    public Vector3 velocity;
    public float life, tracerSize, reliability;
    public Color color;
    public bool tracer, active, impacted, explosive, sound;
    public double Time { get; set; }
    public EventType EventType => EventType.GunBullet;
    public void Execute(object worker = null) { if (worker is UnitController c) c.Execute(this); }
    public void Write(BinaryWriter w)
    {
        ValidatePayload();
        byte[] name = Encoding.UTF8.GetBytes(weaponName ?? "");
        if (name.Length > 512) throw new InvalidDataException("Replay gun name exceeds limit");
        w.Write(Time); w.Write(unitId); w.Write(station); w.Write(weaponSlot); w.Write(partId); w.Write(partGunSlot);
        w.Write((ushort)name.Length); w.Write(name);
        w.Write(position.x); w.Write(position.y); w.Write(position.z);
        w.Write(velocity.x); w.Write(velocity.y); w.Write(velocity.z);
        w.Write(life); w.Write(tracerSize); w.Write(reliability);
        w.Write(color.r); w.Write(color.g); w.Write(color.b); w.Write(color.a);
        w.Write(tracer); w.Write(active); w.Write(impacted); w.Write(explosive); w.Write(sound);
    }
    public void Read(BinaryReader r)
    {
        Time = r.ReadDouble(); unitId = r.ReadUInt32(); station = r.ReadUInt16(); weaponSlot = r.ReadUInt16(); partId = r.ReadInt16(); partGunSlot = r.ReadUInt16();
        ValidateAddress();
        int length = r.ReadUInt16(); if (length > 512) throw new InvalidDataException("Replay gun name exceeds limit");
        byte[] bytes = r.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException(); weaponName = Encoding.UTF8.GetString(bytes);
        position = new GlobalPosition(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        velocity = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        life = r.ReadSingle(); tracerSize = r.ReadSingle(); reliability = r.ReadSingle();
        color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        tracer = r.ReadBoolean(); active = r.ReadBoolean(); impacted = r.ReadBoolean(); explosive = r.ReadBoolean(); sound = r.ReadBoolean();
        ValidatePayload();
    }
    public bool HasValidPayload() => unitId != 0 && !double.IsNaN(Time) && !double.IsInfinity(Time) && Time >= 0 &&
        (station == ushort.MaxValue || station <= byte.MaxValue) && partId >= -1 && partId <= byte.MaxValue &&
        (station != ushort.MaxValue || partId >= 0) && Encoding.UTF8.GetByteCount(weaponName ?? "") <= 512 &&
        Finite(position.x) && Finite(position.y) && Finite(position.z) &&
        Finite(velocity.x) && Finite(velocity.y) && Finite(velocity.z) &&
        Finite(life) && life >= 0 && Finite(tracerSize) && tracerSize >= 0 &&
        Finite(reliability) && reliability >= 0 && reliability <= 1 &&
        Finite(color.r) && Finite(color.g) && Finite(color.b) && Finite(color.a);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private void ValidatePayload()
    { if (!HasValidPayload()) throw new InvalidDataException("Invalid replay gun projectile payload"); }
    private void ValidateAddress()
    {
        if ((station != ushort.MaxValue && station > byte.MaxValue) || partId < -1 || partId > byte.MaxValue ||
            (station == ushort.MaxValue && partId == -1)) throw new InvalidDataException("Invalid replay gun address");
    }
    public void Reset()
    {
        Time = 0; unitId = 0; station = ushort.MaxValue; weaponSlot = partGunSlot = 0; partId = -1; weaponName = "";
        position = default; velocity = default; life = tracerSize = reliability = 0; color = default;
        tracer = active = impacted = explosive = sound = false;
    }
}
