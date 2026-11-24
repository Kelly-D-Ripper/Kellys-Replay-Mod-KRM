using System.IO;
using ReplayMod.Core;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.AircraftStatus)]
public class AircraftStatusEvent : IReplayEvent
{
    public uint unitId;
    public bool ignition;
    public float[] rpm = new float[0];
    public double Time { get; set; }
    public EventType EventType => EventType.AircraftStatus;
    public void Execute(object worker = null) { if (worker is UnitController c) c.Execute(this); }
    public void Write(BinaryWriter w)
    { w.Write(Time); w.Write(unitId); w.Write(ignition); w.Write((ushort)rpm.Length); foreach (var value in rpm) w.Write(value); }
    public void Read(BinaryReader r)
    { Time = r.ReadDouble(); unitId = r.ReadUInt32(); ignition = r.ReadBoolean(); rpm = new float[r.ReadUInt16()]; for (int i = 0; i < rpm.Length; i++) rpm[i] = r.ReadSingle(); }
    public void Reset() { Time = 0; unitId = 0; ignition = false; rpm = new float[0]; }
}
