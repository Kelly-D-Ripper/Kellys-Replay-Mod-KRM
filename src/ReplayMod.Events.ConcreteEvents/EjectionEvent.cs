using System.IO;
using ReplayMod.Core;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.Ejection)]
public class EjectionEvent : IReplayEvent
{
    public uint unitId;
    public bool capsule;
    public double Time { get; set; }
    public EventType EventType => EventType.Ejection;
    public void Execute(object worker = null) { if (worker is UnitController c) c.Execute(this); }
    public void Write(BinaryWriter w) { w.Write(Time); w.Write(unitId); w.Write(capsule); }
    public void Read(BinaryReader r) { Time = r.ReadDouble(); unitId = r.ReadUInt32(); capsule = r.ReadBoolean(); }
    public void Reset() { Time = 0; unitId = 0; capsule = false; }
}
