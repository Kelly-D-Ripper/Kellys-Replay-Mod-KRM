using System;
using System.IO;
using System.Text;
using ReplayMod.Core;
using ReplayMod.Data;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.HangarDoorState)]
public sealed class HangarDoorStateEvent : IReplayEvent
{
    public uint unitId;
    public string hangarPath = string.Empty;
    public float openAmount, currentSpeed, blendSeconds;
    public bool opening, available, selfDisabled;
    public double Time { get; set; }
    public EventType EventType => EventType.HangarDoorState;
    public void Execute(object worker = null) { if (worker is UnitController controller) controller.Execute(this); }
    public void Write(BinaryWriter writer)
    {
        var path = Encoding.UTF8.GetBytes(hangarPath ?? string.Empty);
        if (path.Length == 0 || path.Length > WorldStateSnapshot.MaxPathBytes || !HangarDoorSnapshot.Valid(this))
            throw new InvalidDataException("Invalid replay hangar state");
        writer.Write(Time); writer.Write(unitId); writer.Write((ushort)path.Length); writer.Write(path);
        writer.Write(openAmount); writer.Write(currentSpeed); writer.Write(blendSeconds);
        writer.Write(opening); writer.Write(available); writer.Write(selfDisabled);
    }
    public void Read(BinaryReader reader)
    {
        Time = reader.ReadDouble(); unitId = reader.ReadUInt32(); int length = reader.ReadUInt16();
        if (length == 0 || length > WorldStateSnapshot.MaxPathBytes) throw new InvalidDataException("Invalid replay hangar path");
        byte[] path = reader.ReadBytes(length); if (path.Length != length) throw new EndOfStreamException();
        hangarPath = Encoding.UTF8.GetString(path);
        openAmount = reader.ReadSingle(); currentSpeed = reader.ReadSingle(); blendSeconds = reader.ReadSingle();
        opening = reader.ReadBoolean(); available = reader.ReadBoolean(); selfDisabled = reader.ReadBoolean();
        if (!HangarDoorSnapshot.Valid(this)) throw new InvalidDataException("Invalid replay hangar state");
    }
    public void Reset()
    { Time = 0; unitId = 0; hangarPath = string.Empty; openAmount = currentSpeed = blendSeconds = 0; opening = available = selfDisabled = false; }
}
