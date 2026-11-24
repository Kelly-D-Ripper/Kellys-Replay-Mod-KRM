using System;
using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(Spawner))]
public class SpawnerPatch
{
	[HarmonyPatch(typeof(Spawner), "SpawnMissile", new Type[]
	{
		typeof(MissileDefinition),
		typeof(Vector3),
		typeof(Quaternion),
		typeof(Vector3),
		typeof(Unit),
		typeof(Unit)
	})]
	[HarmonyPrefix]
	public static bool SpawnMissilePrefix(MissileDefinition missile, Vector3 launchPosition, Quaternion rotation, Vector3 velocity, Unit target, Unit owner)
	{
		return !ReplayMod.Core.ReplayRuntime.IsPlayback;
	}

	[HarmonyPatch(typeof(Spawner), "SpawnMissile", new Type[]
	{
		typeof(GameObject),
		typeof(Vector3),
		typeof(Quaternion),
		typeof(Vector3),
		typeof(Unit),
		typeof(Unit)
	})]
	[HarmonyPrefix]
	public static bool SpawnMissilePrefix2(GameObject missile, Vector3 launchPosition, Quaternion rotation, Vector3 velocity, Unit target, Unit owner)
	{
		return !ReplayMod.Core.ReplayRuntime.IsPlayback;
	}
}
