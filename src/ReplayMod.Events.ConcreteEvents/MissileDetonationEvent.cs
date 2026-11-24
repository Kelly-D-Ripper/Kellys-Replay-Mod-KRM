using System.IO;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

// The resolved RPC position and impact classification choose the native visual.
// Despawn remains a separate lifecycle event; this event never destroys the unit.
[ReplayEvent(EventType.MissileDetonation)]
public sealed class MissileDetonationEvent : IReplayEvent
{
    public uint unitId;
    public GlobalPosition position;
    public Vector3 normal;
    public bool armed, hitArmor, hitTerrain;
    public double Time { get; set; }
    public EventType EventType => EventType.MissileDetonation;
    public void Execute(object worker = null) { if (worker is UnitController controller) controller.Execute(this); }
    public void Write(BinaryWriter writer)
    {
        writer.Write(Time); writer.Write(unitId);
        writer.Write(position.x); writer.Write(position.y); writer.Write(position.z);
        writer.Write(normal.x); writer.Write(normal.y); writer.Write(normal.z);
        writer.Write(armed); writer.Write(hitArmor); writer.Write(hitTerrain);
    }
    public void Read(BinaryReader reader)
    {
        Time = reader.ReadDouble(); unitId = reader.ReadUInt32();
        position = new GlobalPosition(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        normal = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        armed = reader.ReadBoolean(); hitArmor = reader.ReadBoolean(); hitTerrain = reader.ReadBoolean();
    }
    public void Reset() { Time = 0; unitId = 0; position = default; normal = default; armed = hitArmor = hitTerrain = false; }
}
