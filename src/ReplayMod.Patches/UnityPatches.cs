using System;
using HarmonyLib;
using UnityEngine;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(UnityEngine.Object))]
public class UnityPatches
{
	public static Action<UnityEngine.Object> onObjectInstantiate;
}
