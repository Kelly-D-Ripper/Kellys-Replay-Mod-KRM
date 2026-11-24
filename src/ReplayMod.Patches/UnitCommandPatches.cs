using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(UnitCommand))]
public class UnitCommandPatches
{
	[HarmonyPatch("SetDestination")]
	[HarmonyPrefix]
	public static bool SetDestinationPatch(GlobalPosition waypoint, bool playerCommand)
	{
		if (ReplayMod.Core.ReplayRuntime.IsPlayback)
		{
			return false;
		}
		return true;
	}
}
