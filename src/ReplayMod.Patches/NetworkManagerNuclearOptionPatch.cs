using System;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SceneLoading;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(NetworkManagerNuclearOption))]
public class NetworkManagerNuclearOptionPatch
{
	[HarmonyPatch("ClientLoadScene")]
	[HarmonyPrefix]
	public static void ClientLoad(LoadMapMessage msg)
	{
	}

	[HarmonyPatch("ServerLoadMapScene")]
	[HarmonyPrefix]
	public static void ServerLoad(MapKey mapKey, IProgress<float> progress)
	{
	}
}
