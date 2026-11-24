using System.IO;
using ReplayMod.Core;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.ToggleGear)]
public class SetGearEvent : IReplayEvent
{
	public uint unitId;

	public bool deployed;
	public bool Instant;

	public double Time { get; set; }

	public EventType EventType { get; } = EventType.ToggleGear;

	public void Execute(object worker = null)
	{
		Plugin.DebugLog("Executing setgear");
		if (worker is UnitController unitController)
		{
			unitController.SetGear(this);
		}
	}

	public void Read(BinaryReader br)
	{
		Time = br.ReadDouble();
		unitId = br.ReadUInt32();
		deployed = br.ReadBoolean();
	}

	public void Reset()
	{
		Time = 0; unitId = 0; deployed = false; Instant = false;
	}

	public void Write(BinaryWriter bw)
	{
		bw.Write(Time);
		bw.Write(unitId);
		bw.Write(deployed);
	}
}
