using HarmonyLib;
using ReplayMod.UI;
using UnityEngine;

namespace ReplayMod.Patches;

// Native Hide UI leaves forceHidden set until its map/pause UI clears it. Keep
// that native preference intact, but make the explicitly opened studio usable.
[HarmonyPatch(typeof(CursorManager),nameof(CursorManager.Refresh))]
public static class StudioCursorPatch
{
    [HarmonyPostfix]
    public static void Postfix(ref bool ___visible)
    {
        if(TrailerSuiteUi.Instance?.IsVisible!=true)return;
        ___visible=true;Cursor.visible=true;Cursor.lockState=CursorLockMode.None;
    }
}
