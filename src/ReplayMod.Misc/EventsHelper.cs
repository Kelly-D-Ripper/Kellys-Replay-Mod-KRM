using System;
using System.Collections.Generic;
using System.IO;
using Mirage.Serialization;
using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Misc;

public static class EventsHelper
{
	private static Dictionary<uint, Inputs> _lastInputs = new Dictionary<uint, Inputs>();
	private sealed class ReaderHistory
	{
		internal readonly WeakReference reader;
		internal readonly Dictionary<uint,Inputs> inputs=new Dictionary<uint,Inputs>();
		internal ReaderHistory(BinaryReader reader){this.reader=new WeakReference(reader);}
	}
	private static readonly object ReaderHistoryGate=new object();
	private static readonly List<ReaderHistory> ReaderHistories=new List<ReaderHistory>();
	private static Dictionary<uint,Inputs> HistoryFor(BinaryReader reader)
	{
		lock(ReaderHistoryGate)
		{
			Dictionary<uint,Inputs> found=null;
			for(int i=ReaderHistories.Count-1;i>=0;i--)
			{
				var target=ReaderHistories[i].reader.Target;
				if(target==null){ReaderHistories.RemoveAt(i);continue;}
				if(ReferenceEquals(target,reader))found=ReaderHistories[i].inputs;
			}
			if(found!=null)return found;
			var history=new ReaderHistory(reader);ReaderHistories.Add(history);return history.inputs;
		}
	}

	private const byte ABS_INTERVAL = 10;

	private static Dictionary<uint, PositionSnapshot> _actualPositions = new Dictionary<uint, PositionSnapshot>();

	private static Dictionary<uint, byte> _steps = new Dictionary<uint, byte>();

	public static void Reset()
	{
		_lastInputs.Clear();
		_actualPositions.Clear();
		_steps.Clear();
	}

	public static void GetInputsMask(UpdateInputsEvent e, out byte mask)
	{
		mask = 0;
		if (!_lastInputs.TryGetValue(e.id, out var value))
		{
			_lastInputs[e.id] = new Inputs
			{
				pitch = e.pitch,
				roll = e.roll,
				yaw = e.yaw,
				throttle = e.throttle,
				brake = e.brake,
				customAxis1 = e.customAxis1
			};
			mask = 63;
			return;
		}
		if (e.pitch.ValueChanged(value.pitch))
		{
			mask |= 1;
			value.pitch = e.pitch;
		}
		if (e.roll.ValueChanged(value.roll))
		{
			mask |= 2;
			value.roll = e.roll;
		}
		if (e.yaw.ValueChanged(value.yaw))
		{
			mask |= 4;
			value.yaw = e.yaw;
		}
		if (e.throttle.ValueChanged(value.throttle))
		{
			mask |= 8;
			value.throttle = e.throttle;
		}
		if (e.brake.ValueChanged(value.brake))
		{
			mask |= 16;
			value.brake = e.brake;
		}
		if (e.customAxis1.ValueChanged(value.customAxis1))
		{
			mask |= 32;
			value.customAxis1 = e.customAxis1;
		}
	}

	public static Inputs GetInputs(uint id, byte mask, BinaryReader br, int quant)
	{
		var lastInputs = HistoryFor(br);
		if (!lastInputs.TryGetValue(id, out var value))
		{
			value = new Inputs();
			lastInputs[id] = value;
		}
		if ((mask & 1) != 0)
		{
			value.pitch = (float)br.ReadInt16() / (float)quant;
		}
		if ((mask & 2) != 0)
		{
			value.roll = (float)br.ReadInt16() / (float)quant;
		}
		if ((mask & 4) != 0)
		{
			value.yaw = (float)br.ReadInt16() / (float)quant;
		}
		if ((mask & 8) != 0)
		{
			value.throttle = (float)br.ReadInt16() / (float)quant;
		}
		if ((mask & 0x10) != 0)
		{
			value.brake = (float)br.ReadInt16() / (float)quant;
		}
		if ((mask & 0x20) != 0)
		{
			value.customAxis1 = (float)br.ReadInt16() / (float)quant;
		}
		return value;
	}

	public static bool ValueChanged(this float a, float b)
	{
		if (Math.Abs(a - b) > 0.0001f)
		{
			return true;
		}
		return false;
	}

	public static void WriteCompressedPosition(UpdatePositionEvent e, BinaryWriter bw)
	{
		if (!_actualPositions.ContainsKey(e.unitId))
		{
			PositionSnapshot value = new PositionSnapshot
			{
				position = e.position,
				velocity = e.velocity,
				rotation = e.rotation
			};
			_actualPositions[e.unitId] = value;
		}
		if (ShouldWriteAbs(e.unitId))
		{
			bw.Write(value: true);
			bw.Write(e.position.x);
			bw.Write(e.position.y);
			bw.Write(e.position.z);
			bw.Write(e.velocity.x);
			bw.Write(e.velocity.y);
			bw.Write(e.velocity.z);
		}
		else
		{
			bw.Write(value: false);
			PositionSnapshot positionSnapshot = _actualPositions[e.unitId];
			bw.Write((short)((e.position.x - positionSnapshot.position.x) * 100f));
			bw.Write((short)((e.position.y - positionSnapshot.position.y) * 100f));
			bw.Write((short)((e.position.z - positionSnapshot.position.z) * 100f));
			bw.Write((short)((e.velocity.x - positionSnapshot.velocity.x) * 100f));
			bw.Write((short)((e.velocity.y - positionSnapshot.velocity.y) * 100f));
			bw.Write((short)((e.velocity.z - positionSnapshot.velocity.z) * 100f));
		}
		uint value2 = QuaternionPacker.PackAsInt(e.rotation);
		bw.Write(value2);
		PositionSnapshot value3 = new PositionSnapshot
		{
			position = e.position,
			rotation = e.rotation,
			velocity = e.velocity
		};
		_actualPositions[e.unitId] = value3;
	}

	public static void ReadCompressedPosition(UpdatePositionEvent e, BinaryReader br)
	{
		if (e.isAbs)
		{
			e.position = new GlobalPosition
			{
				x = br.ReadSingle(),
				y = br.ReadSingle(),
				z = br.ReadSingle()
			};
			e.velocity = new Vector3
			{
				x = br.ReadSingle(),
				y = br.ReadSingle(),
				z = br.ReadSingle()
			};
		}
		else
		{
			PositionSnapshot positionSnapshot = _actualPositions[e.unitId];
			GlobalPosition position = positionSnapshot.position;
			position.x += (float)br.ReadInt16() / 100f;
			position.y += (float)br.ReadInt16() / 100f;
			position.z += (float)br.ReadInt16() / 100f;
			Vector3 velocity = positionSnapshot.velocity;
			velocity.x += (float)br.ReadInt16() / 100f;
			velocity.y += (float)br.ReadInt16() / 100f;
			velocity.z += (float)br.ReadInt16() / 100f;
			e.position = position;
			e.velocity = velocity;
		}
		uint value = br.ReadUInt32();
		e.rotation = QuaternionPacker.UnpackFromInt(value);
		PositionSnapshot value2 = new PositionSnapshot
		{
			position = e.position,
			rotation = e.rotation,
			velocity = e.velocity
		};
		_actualPositions[e.unitId] = value2;
		Plugin.logger.LogWarning((object)$"[{e.isAbs}][{e.unitId}] p{e.position} r{e.rotation} v{e.velocity}");
	}

	private static bool ShouldWriteAbs(uint id)
	{
		if (!_steps.ContainsKey(id))
		{
			_steps[id] = 10;
			return true;
		}
		byte b = _steps[id];
		if (b > 0)
		{
			_steps[id] = (byte)(b - 1);
			return false;
		}
		_steps[id] = 10;
		return true;
	}

	public static void OnSpawn(SpawnEvent e)
	{
	}

	public static void OnDespawn(DespawnEvent e)
	{
	}
}
