using System;
using HarmonyLib;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(UnitRegistry))]
public class UnitRegistryPatch
{
	public static Action<Unit, PersistentID> onRegisterUnit;

	public static Action<Unit> onUnregisterUnit;

	[HarmonyPatch("RegisterUnit")]
	[HarmonyPostfix]
	public static void RegisterUnitPatch(Unit unit, PersistentID id)
	{
		onRegisterUnit?.Invoke(unit, id);
	}

	[HarmonyPatch("UnregisterUnit")]
	[HarmonyPrefix]
	public static void UnregisterUnitPatch(Unit unit)
	{
		onUnregisterUnit?.Invoke(unit);
	}
}
