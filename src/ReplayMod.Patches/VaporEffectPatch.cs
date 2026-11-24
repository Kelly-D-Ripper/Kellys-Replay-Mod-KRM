using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(VaporEffect))]
public class VaporEffectPatch
{
	[HarmonyPatch("FixedUpdate")]
	[HarmonyPrefix]
	public static bool FixedUpdatePatch()
	{
		return true;
	}
}
