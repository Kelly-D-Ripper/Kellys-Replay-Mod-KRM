using System.IO;
using ReplayMod.Core;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.UnitStatus)]
public sealed class UnitStatusEvent : IReplayEvent
{
    public uint unitId;
    public bool disabled;
    public byte state;
    public double Time { get; set; }
    public EventType EventType => EventType.UnitStatus;
    public void Execute(object worker = null) { if (worker is UnitController controller) controller.Execute(this); }
    public void Write(BinaryWriter writer) { writer.Write(Time); writer.Write(unitId); writer.Write(disabled); writer.Write(state); }
    public void Read(BinaryReader reader) { Time = reader.ReadDouble(); unitId = reader.ReadUInt32(); disabled = reader.ReadBoolean(); state = reader.ReadByte(); }
    public void Reset() { Time = 0; unitId = 0; disabled = false; state = 0; }
}
