using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Debug.Patches;

[HarmonyPatch(typeof(BulletSim))]
internal class BulletSimPatch
{
	[HarmonyPatch("AddBullet"), HarmonyPrefix]
	public static bool Prefix(BulletSim __instance, out int __state)
	{ __state = __instance.bullets.Count; return !ReplayRuntime.IsPlayback; }
	[HarmonyPatch("AddBullet"), HarmonyPostfix]
	public static void Postfix(BulletSim __instance, int __state, Color tracerColor, float tracerSize)
	{
		if (Recorder.Active != null && __instance.bullets.Count > __state)
			Recorder.Active.RecordBullet(__instance, __instance.bullets[__state], tracerColor, tracerSize);
	}
}
