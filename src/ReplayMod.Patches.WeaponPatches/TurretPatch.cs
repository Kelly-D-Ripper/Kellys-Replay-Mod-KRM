using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches.WeaponPatches;

[HarmonyPatch(typeof(Turret))]
public class TurretPatch
{
	[HarmonyPatch("FixedUpdate")]
	[HarmonyPrefix]
	public static bool FixedUpdatePatch(Turret __instance)
	{
		return !ReplayMod.Core.ReplayRuntime.IsPlayback;
	}
}
