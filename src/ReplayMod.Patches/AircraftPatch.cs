using System;
using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(Aircraft))]
public class AircraftPatch
{
	public static Action<Aircraft, bool> onSetGear;

	[HarmonyPatch("StartEjectionSequence")]
	[HarmonyPrefix]
	public static bool StartEjectionSequencePatch()
	{
		if (ReplayMod.Core.ReplayRuntime.IsPlayback)
		{
			return false;
		}
		return true;
	}

	[HarmonyPatch("SetGear", new Type[] { typeof(bool) })]
	[HarmonyPrefix]
	public static void SetGearPatch(Aircraft __instance, bool deployed)
	{
		onSetGear?.Invoke(__instance, deployed);
	}

	[HarmonyPatch("CmdLaunchMissile")]
	[HarmonyPrefix]
	public static bool CmdLaunchMissilePatch(byte stationIndex, Unit target, GlobalPosition aimpoint)
	{
		return !ReplayMod.Core.ReplayRuntime.IsPlayback;
	}

	[HarmonyPatch("RpcLaunchMissile")]
	[HarmonyPrefix]
	public static bool RpcLaunchMissilePatch(byte stationIndex, Unit target, GlobalPosition aimpoint)
	{
		return !ReplayMod.Core.ReplayRuntime.IsPlayback;
	}
}
