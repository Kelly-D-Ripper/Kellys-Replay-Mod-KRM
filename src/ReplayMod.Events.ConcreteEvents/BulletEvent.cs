using System.IO;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

// Capture the actual bullet after the game's initial trajectory step and spread.
// Playback inserts this state directly and never calls WeaponManager.Fire for guns.
[ReplayEvent(EventType.Bullet)]
public class BulletEvent : IReplayEvent
{
    public uint unitId;
    public ushort gunIndex;
    public GlobalPosition position;
    public Vector3 velocity;
    public float life, tracerSize, reliability;
    public Color color;
    public bool tracer, active, impacted, explosive;
    public double Time { get; set; }
    public EventType EventType => EventType.Bullet;
    public void Execute(object worker = null) { if (worker is UnitController c) c.Execute(this); }
    public void Write(BinaryWriter w)
    {
        w.Write(Time); w.Write(unitId); w.Write(gunIndex);
        w.Write(position.x); w.Write(position.y); w.Write(position.z);
        w.Write(velocity.x); w.Write(velocity.y); w.Write(velocity.z);
        w.Write(life); w.Write(tracerSize); w.Write(reliability);
        w.Write(color.r); w.Write(color.g); w.Write(color.b); w.Write(color.a);
        w.Write(tracer); w.Write(active); w.Write(impacted); w.Write(explosive);
    }
    public void Read(BinaryReader r)
    {
        Time = r.ReadDouble(); unitId = r.ReadUInt32(); gunIndex = r.ReadUInt16();
        position = new GlobalPosition(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        velocity = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        life = r.ReadSingle(); tracerSize = r.ReadSingle(); reliability = r.ReadSingle();
        color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        tracer = r.ReadBoolean(); active = r.ReadBoolean(); impacted = r.ReadBoolean(); explosive = r.ReadBoolean();
    }
    public void Reset()
    { Time = 0; unitId = 0; gunIndex = 0; position = default; velocity = default; life = tracerSize = reliability = 0; color = default; tracer = active = impacted = explosive = false; }
}
