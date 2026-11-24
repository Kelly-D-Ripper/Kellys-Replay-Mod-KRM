using System.Collections.Generic;

namespace ReplayMod.Data;

public struct WorldSnapshot
{
	public double time;

	public Dictionary<uint, SpawnSnapshot> spawns;

	public Dictionary<uint, PositionSnapshot> positions;

	public long offsetAfter;

	public static WorldSnapshot Create(double time, Dictionary<uint, SpawnSnapshot> spawns, Dictionary<uint, PositionSnapshot> positions, long fileOffset)
	{
		Dictionary<uint, SpawnSnapshot> dictionary = new Dictionary<uint, SpawnSnapshot>();
		foreach (KeyValuePair<uint, SpawnSnapshot> spawn in spawns)
		{
			dictionary.Add(spawn.Key, spawn.Value);
		}
		Dictionary<uint, PositionSnapshot> dictionary2 = new Dictionary<uint, PositionSnapshot>();
		foreach (KeyValuePair<uint, PositionSnapshot> position in positions)
		{
			dictionary2.Add(position.Key, position.Value);
		}
		return new WorldSnapshot
		{
			time = time,
			spawns = dictionary,
			positions = dictionary2,
			offsetAfter = fileOffset
		};
	}
}
