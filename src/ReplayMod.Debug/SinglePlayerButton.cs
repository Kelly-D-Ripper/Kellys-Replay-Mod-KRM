using HarmonyLib;
using NuclearOption.SavedMission;

namespace ReplayMod.Debug;

[HarmonyPatch(typeof(SinglePlayerMenu))]
public class SinglePlayerButton
{
	[HarmonyPatch("StartMission")]
	[HarmonyPrefix]
	public static void Patch(Mission mission)
	{
		Plugin.logger.LogWarning((object)$"{mission}");
		foreach (Faction faction in FactionRegistry.factions)
		{
			Plugin.logger.LogInfo((object)$"{faction}, {faction.factionName}");
		}
		foreach (MissionFaction faction2 in mission.factions)
		{
			Plugin.logger.LogWarning((object)$"{faction2} {faction2.factionName}");
		}
	}
}
