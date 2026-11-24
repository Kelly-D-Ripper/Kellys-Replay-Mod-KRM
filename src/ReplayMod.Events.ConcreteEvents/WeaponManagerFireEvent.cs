using System.IO;
using ReplayMod.Core;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.WeaponManagerFire)]
public class WeaponManagerFireEvent : IReplayEvent
{
	public uint unitId;

	public double Time { get; set; }

	public EventType EventType { get; } = EventType.WeaponManagerFire;

	public void Execute(object worker = null)
	{
		if (worker is UnitController unitController)
		{
			unitController.WeaponManagerFire(this);
		}
	}

	public void Read(BinaryReader br)
	{
		Time = br.ReadDouble();
		unitId = br.ReadUInt32();
	}

	public void Reset()
	{
		Time = 0.0;
	}

	public void Write(BinaryWriter bw)
	{
		bw.Write(Time);
		bw.Write(unitId);
	}
}
