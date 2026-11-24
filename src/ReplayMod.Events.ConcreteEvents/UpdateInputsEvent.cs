using System.IO;
using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Misc;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.ControlInputs)]
public class UpdateInputsEvent : IReplayEvent
{
	public uint id;

	public float pitch;

	public float roll;

	public float yaw;

	public float throttle;

	public float brake;

	public float customAxis1;

	public byte mask;

	public const int QUANT = 10000;

	public double Time { get; set; }

	public EventType EventType { get; } = EventType.ControlInputs;

	public void Execute(object worker = null)
	{
		if (worker is UnitController unitController)
		{
			unitController.Execute(this);
		}
	}

	public void Read(BinaryReader br)
	{
		Time = br.ReadDouble();
		id = br.ReadUInt32();
		mask = br.ReadByte();
		if(br is ReplayFrameReader)
		{
			// Framed readers decode one complete event, independently of every
			// other reader. Legacy delta histories only belong to stream readers.
			pitch=(mask&1)!=0?(float)br.ReadInt16()/QUANT:0;
			roll=(mask&2)!=0?(float)br.ReadInt16()/QUANT:0;
			yaw=(mask&4)!=0?(float)br.ReadInt16()/QUANT:0;
			throttle=(mask&8)!=0?(float)br.ReadInt16()/QUANT:0;
			brake=(mask&16)!=0?(float)br.ReadInt16()/QUANT:0;
			customAxis1=(mask&32)!=0?(float)br.ReadInt16()/QUANT:0;
			return;
		}
		Inputs inputs = EventsHelper.GetInputs(id, mask, br, 10000);
		pitch = inputs.pitch;
		roll = inputs.roll;
		yaw = inputs.yaw;
		throttle = inputs.throttle;
		brake = inputs.brake;
		customAxis1 = inputs.customAxis1;
	}

	public void Reset()
	{
		Time = 0.0;
	}

	public void Write(BinaryWriter bw)
	{
		bw.Write(Time);
		bw.Write(id);
		mask = 63;
		bw.Write(mask);
		if ((mask & 1) != 0)
		{
			bw.Write((short)(UnityEngine.Mathf.Clamp(pitch, -1f, 1f) * 10000f));
		}
		if ((mask & 2) != 0)
		{
			bw.Write((short)(UnityEngine.Mathf.Clamp(roll, -1f, 1f) * 10000f));
		}
		if ((mask & 4) != 0)
		{
			bw.Write((short)(UnityEngine.Mathf.Clamp(yaw, -1f, 1f) * 10000f));
		}
		if ((mask & 8) != 0)
		{
			bw.Write((short)(UnityEngine.Mathf.Clamp(throttle, -1f, 1f) * 10000f));
		}
		if ((mask & 0x10) != 0)
		{
			bw.Write((short)(UnityEngine.Mathf.Clamp(brake, -1f, 1f) * 10000f));
		}
		if ((mask & 0x20) != 0)
		{
			bw.Write((short)(UnityEngine.Mathf.Clamp(customAxis1, -1f, 1f) * 10000f));
		}
	}

	public void CopyFromControlInputs(ControlInputs source)
	{
		pitch = source.pitch;
		roll = source.roll;
		yaw = source.yaw;
		throttle = source.throttle;
		brake = source.brake;
		customAxis1 = source.customAxis1;
	}

	public void CopyToControlInputs(ControlInputs inputs)
	{
		inputs.pitch = pitch;
		inputs.roll = roll;
		inputs.yaw = yaw;
		inputs.throttle = throttle;
		inputs.brake = brake;
		inputs.customAxis1 = customAxis1;
	}
}
