using System.IO;
using ReplayMod.Core;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.Despawn)]
public class DespawnEvent : IReplayEvent
{
	public uint unitId;

	public bool isMissileDetonated;

	public EventType EventType { get; } = EventType.Despawn;

	public double Time { get; set; }

	public void Execute(object worker)
	{
		if (worker is UnitController unitController)
		{
			unitController.DespawnUnit(unitId, isMissileDetonated, Time);
		}
	}

	public void Read(BinaryReader br)
	{
		Time = br.ReadDouble();
		unitId = br.ReadUInt32();
		isMissileDetonated = br.ReadBoolean();
	}

	public void Reset()
	{
		Time = 0.0;
	}

	public void Write(BinaryWriter bw)
	{
		bw.Write(Time);
		bw.Write(unitId);
		bw.Write(isMissileDetonated);
	}
}
