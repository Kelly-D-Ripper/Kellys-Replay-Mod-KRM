using System.IO;
using ReplayMod.Core;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.UpdateTurret)]
public class UpdateTurretEvent : IReplayEvent
{
	public float elevationAngle;

	public float traverseAngle;

	public uint attachedUnitId;

	public byte weaponStationIdx;

	public byte turretIdx;

	public double Time { get; set; }

	public EventType EventType { get; } = EventType.UpdateTurret;
	public bool HasFiniteAngles()=>ReplayDamageStatePolicy.Finite(elevationAngle)&&ReplayDamageStatePolicy.Finite(traverseAngle);

	public void Execute(object worker = null)
	{
		if (worker is UnitController unitController)
		{
			unitController.Execute(this);
		}
	}

	public void Read(BinaryReader br)
	{
		elevationAngle = br.ReadSingle();
		traverseAngle = br.ReadSingle();
		attachedUnitId = br.ReadUInt32();
		weaponStationIdx = br.ReadByte();
		turretIdx = br.ReadByte();
		Time = br.ReadDouble();
		if(!HasFiniteAngles())throw new InvalidDataException("Replay turret has non-finite angles");
	}

	public void Reset()
	{
		elevationAngle = 0f;
		traverseAngle = 0f;
		attachedUnitId = 0u;
		weaponStationIdx = 0;
		turretIdx = 0;
	}

	public void Write(BinaryWriter bw)
	{
		if(!HasFiniteAngles())throw new InvalidDataException("Replay turret has non-finite angles");
		bw.Write(elevationAngle);
		bw.Write(traverseAngle);
		bw.Write(attachedUnitId);
		bw.Write(weaponStationIdx);
		bw.Write(turretIdx);
		bw.Write(Time);
	}
}
