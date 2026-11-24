using System.IO;
using ReplayMod.Core;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

// Reject NaN and infinity before they enter Unity transforms. Preserve finite
// legacy poses, including non-unit quaternions that older recordings may hold.
public static class ReplayPoseValidation
{
    private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    public static bool Finite(GlobalPosition p,Quaternion q,Vector3 v)=>
        Finite(p.x)&&Finite(p.y)&&Finite(p.z)&&Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w)&&Finite(v.x)&&Finite(v.y)&&Finite(v.z);
}

[ReplayEvent(EventType.Move)]
public class UpdatePositionEvent : IReplayEvent
{
	public uint unitId;

	public GlobalPosition position;

	public Quaternion rotation;

	public Vector3 velocity;

	public const int QUANT = 100;

	public const int ROT_QUANT = 1000;

	public bool isAbs;

	public EventType EventType { get; } = EventType.Move;

	public double Time { get; set; }

	public void Execute(object worker)
	{
		if (worker is UnitController unitController)
		{
			unitController.Execute(this);
		}
	}

	public void Read(BinaryReader br)
	{
		unitId = (uint)br.ReadInt32();
		Time = br.ReadDouble();
		position = new GlobalPosition
		{
			x = br.ReadSingle(),
			y = br.ReadSingle(),
			z = br.ReadSingle()
		};
		rotation = new Quaternion
		{
			x = br.ReadSingle(),
			y = br.ReadSingle(),
			z = br.ReadSingle(),
			w = br.ReadSingle()
		};
		velocity = new Vector3
		{
			x = br.ReadSingle(),
			y = br.ReadSingle(),
			z = br.ReadSingle()
		};
		if(!ReplayPoseValidation.Finite(position,rotation,velocity))throw new InvalidDataException("Replay movement contains non-finite position, rotation or velocity");
	}

	public void Reset()
	{
		unitId = 0u;
		position = default(GlobalPosition);
		rotation = default(Quaternion);
		velocity = default(Vector3);
	}

	public void Write(BinaryWriter bw)
	{
		if(!ReplayPoseValidation.Finite(position,rotation,velocity))throw new InvalidDataException("Replay movement contains non-finite position, rotation or velocity");
		bw.Write(unitId);
		bw.Write(Time);
		bw.Write(position.x);
		bw.Write(position.y);
		bw.Write(position.z);
		bw.Write(rotation.x);
		bw.Write(rotation.y);
		bw.Write(rotation.z);
		bw.Write(rotation.w);
		bw.Write(velocity.x);
		bw.Write(velocity.y);
		bw.Write(velocity.z);
	}

	public void CopyFromSnapshot(PositionSnapshot snapshot)
	{
		position = snapshot.position;
		rotation = snapshot.rotation;
		velocity = snapshot.velocity;
		Time = snapshot.time;
	}
}
