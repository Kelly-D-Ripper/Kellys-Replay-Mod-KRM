using System.IO;
using ReplayMod.Core;

namespace ReplayMod.Events.ConcreteEvents;

// Fuel ignition is an authoritative outcome, not reproducible from HP alone.
[ReplayEvent(EventType.FuelTankStatus)]
public sealed class FuelTankStatusEvent : IReplayEvent
{
    public uint unitId;
    public byte partId;
    public bool ruptured, onFire, flameActive, suppressBurst;
    public double Time { get; set; }
    public EventType EventType => EventType.FuelTankStatus;
    public void Execute(object worker = null) { if (worker is UnitController controller) controller.Execute(this); }
    public void Write(BinaryWriter writer)
    { writer.Write(Time); writer.Write(unitId); writer.Write(partId); writer.Write(ruptured); writer.Write(onFire); writer.Write(flameActive); writer.Write(suppressBurst); }
    public void Read(BinaryReader reader)
    { Time = reader.ReadDouble(); unitId = reader.ReadUInt32(); partId = reader.ReadByte(); ruptured = reader.ReadBoolean(); onFire = reader.ReadBoolean(); flameActive = reader.ReadBoolean(); suppressBurst = reader.ReadBoolean(); }
    public void Reset() { Time = 0; unitId = 0; partId = 0; ruptured = onFire = flameActive = suppressBurst = false; }
}
