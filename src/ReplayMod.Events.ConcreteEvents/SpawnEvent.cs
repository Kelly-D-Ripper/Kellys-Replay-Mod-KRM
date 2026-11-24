using System.Collections.Generic;
using System.IO;
using ReplayMod.Core;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.Spawn)]
public class SpawnEvent : IReplayEvent
{
	private const int MaximumWeaponMounts = 1024;
	public uint unitId;

	public GlobalPosition pos;

	public Quaternion rotation;

	public string jsonKey;

	public string factionName;

	public Vector3 startingVelocity;

	public uint ownerId;

	public bool isAircraft;

	public LiveryKey.KeyType liveryType;

	public ulong liveryId;

	public int liveryIndex;

	public List<WeaponMount> weapons;

	public EventType EventType { get; } = EventType.Spawn;

	public double Time { get; set; }

	public void Execute(object worker)
	{
		if (!(worker is UnitController unitController))
		{
			Plugin.logger.LogError((object)"error executing spawn event");
			return;
		}
		Plugin.DebugLog($"Executing spawn: {unitId} {jsonKey}");
		unitController.Execute(this);
	}

	public void Write(BinaryWriter bw)
	{
		if(!ReplayPoseValidation.Finite(pos,rotation,startingVelocity))throw new InvalidDataException("Replay spawn contains non-finite position, rotation or velocity");
		bw.Write(Time);
		bw.Write(unitId);
		bw.Write(jsonKey);
		bw.Write(factionName);
		bw.Write(pos.x);
		bw.Write(pos.y);
		bw.Write(pos.z);
		bw.Write(rotation.x);
		bw.Write(rotation.y);
		bw.Write(rotation.z);
		bw.Write(rotation.w);
		bw.Write(startingVelocity.x);
		bw.Write(startingVelocity.y);
		bw.Write(startingVelocity.z);
		bw.Write(isAircraft);
		if (isAircraft)
		{
			if(weapons==null||weapons.Count>MaximumWeaponMounts)throw new InvalidDataException("Invalid replay aircraft loadout size");
			bw.Write((byte)liveryType);
			bw.Write(liveryId);
			bw.Write(liveryIndex);
			bw.Write(weapons.Count);
			foreach (WeaponMount weapon in weapons)
			{
				if (weapon != null)
				{
					bw.Write(weapon.jsonKey);
				}
				else
				{
					bw.Write("NULLPTR");
				}
			}
		}
		bw.Write(ownerId);
	}

	public void Read(BinaryReader br)
	{
		Time = br.ReadDouble();
		unitId = (uint)br.ReadInt32();
		jsonKey = ReplayRecordingHeader.ReadString(br);
		factionName = ReplayRecordingHeader.ReadString(br);
		pos = new GlobalPosition
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
		startingVelocity = new Vector3
		{
			x = br.ReadSingle(),
			y = br.ReadSingle(),
			z = br.ReadSingle()
		};
		isAircraft = br.ReadBoolean();
		if (isAircraft)
		{
			liveryType = (LiveryKey.KeyType)br.ReadByte();
			liveryId = br.ReadUInt64();
			liveryIndex = br.ReadInt32();
			int num = br.ReadInt32();
			if(num<0||num>MaximumWeaponMounts)throw new InvalidDataException("Invalid replay aircraft loadout size");
			weapons = new List<WeaponMount>();
			for (int i = 0; i < num; i++)
			{
				string text = ReplayRecordingHeader.ReadString(br);
				if (text == "NULLPTR")
				{
					weapons.Add(null);
					continue;
				}
				WeaponMount item;
				try { item = Encyclopedia.WeaponLookup[text]; }
				catch(KeyNotFoundException error) { throw new InvalidDataException("Replay requires missing weapon: " + text, error); }
				weapons.Add(item);
			}
		}
		ownerId = br.ReadUInt32();
		if(!ReplayPoseValidation.Finite(pos,rotation,startingVelocity))throw new InvalidDataException("Replay spawn contains non-finite position, rotation or velocity");
	}

	public void Reset()
	{
		unitId = 0u;
		pos = default(GlobalPosition);
		rotation = default(Quaternion);
		jsonKey = null;
		factionName = null;
		startingVelocity = default(Vector3);
		ownerId = 0u;
		isAircraft = false;
		liveryType = LiveryKey.KeyType.Builtin;
		liveryId = 0uL;
		liveryIndex = 0;
		weapons = null;
	}

	public void CopyFromSnapshot(SpawnSnapshot snapshot)
	{
		Time = snapshot.time;
		jsonKey = snapshot.jsonKey;
		pos = snapshot.pos;
		rotation = snapshot.rotation;
		startingVelocity = snapshot.startingVelocity;
		factionName = snapshot.factionName;
		liveryType = snapshot.liveryType;
		liveryIndex = snapshot.liveryIndex;
		liveryId = snapshot.liveryId;
		weapons = new List<WeaponMount>(snapshot.weapons);
		ownerId = snapshot.ownerId;
	}
}
