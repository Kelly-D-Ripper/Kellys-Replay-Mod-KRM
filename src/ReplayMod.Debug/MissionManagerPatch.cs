using HarmonyLib;
using NuclearOption.SavedMission;

namespace ReplayMod.Debug;

[HarmonyPatch(typeof(MissionManager))]
public class MissionManagerPatch
{
	[HarmonyPatch("SetMission")]
	[HarmonyPrefix]
	public static void SetMissionPatch(Mission mission, bool checkIfSame)
	{
		Plugin.logger.LogWarning((object)($"checkIfSame: {checkIfSame}\n" + $"{mission}"));
		foreach (MissionFaction faction in mission.factions)
		{
			Plugin.logger.LogWarning((object)$"{faction} {faction.factionName}");
		}
	}
}
