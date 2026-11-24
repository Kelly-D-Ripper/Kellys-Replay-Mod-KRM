using HarmonyLib;
using ReplayMod.Core;
using Cysharp.Threading.Tasks;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(Building))]
public class BuildingPatch
{
	[HarmonyPatch("UnitDisabled")]
	[HarmonyPrefix]
	public static bool UnitDisabledPrefix(Building __instance, bool oldState, bool newState)
	{
		if(ReplayRuntime.IsPlayback&&ReplayVfxPlayback.IsOwnedActor(__instance))return false;
		return (ReplayManager.i == null || ReplayManager.i.ShouldContinue(__instance.persistentID.Id));
	}

	[HarmonyPatch("UnitDisabled")]
	[HarmonyPostfix]
	public static void UnitDisabledPostfix(Building __instance)
	{if(!ReplayRuntime.IsPlayback)Recorder.Active?.ObserveDisabledBuilding(__instance);}

	[HarmonyPatch("Collapse")]
	[HarmonyPrefix]
	public static bool CollapsePatch(Building __instance,ref UniTask __result)
	{
		if(ReplayRuntime.IsPlayback&&ReplayVfxPlayback.IsOwnedActor(__instance))
		{__result=UniTask.CompletedTask;return false;}
		return (ReplayManager.i == null || ReplayManager.i.ShouldContinue(__instance.persistentID.Id));
	}
}

[HarmonyPatch(typeof(Unit),nameof(Unit.InitializeUnit))]
public static class ReplayBuildingInitializedPatch
{
	[HarmonyPostfix]
	public static void Postfix(Unit __instance)
	{if(!ReplayRuntime.IsPlayback&&__instance is Building building)Recorder.Active?.ObserveInitializedBuilding(building);}
}
