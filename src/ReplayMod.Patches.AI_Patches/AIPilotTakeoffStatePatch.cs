using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches.AI_Patches;

[HarmonyPatch(typeof(AIPilotTakeoffState))]
public class AIPilotTakeoffStatePatch
{
	[HarmonyPatch("UpdateState")]
	[HarmonyPrefix]
	public static bool UpdateStatePatch(Pilot pilot)
	{
		if (ReplayMod.Core.ReplayRuntime.IsPlayback)
		{
			return false;
		}
		return true;
	}

	[HarmonyPatch("FixedUpdateState")]
	[HarmonyPrefix]
	public static bool FixedUpdateState(Pilot pilot)
	{
		if (ReplayMod.Core.ReplayRuntime.IsPlayback)
		{
			return false;
		}
		return true;
	}
}
