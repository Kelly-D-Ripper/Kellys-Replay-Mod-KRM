using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches.AI_Patches;

[HarmonyPatch(typeof(Pilot))]
public class PilotPatche
{
	[HarmonyPatch("TakeGForceDamage")]
	[HarmonyPrefix]
	public static bool GForceDamagePatch(float sqrGForces)
	{
		if (ReplayMod.Core.ReplayRuntime.IsPlayback)
		{
			return false;
		}
		return true;
	}
}
