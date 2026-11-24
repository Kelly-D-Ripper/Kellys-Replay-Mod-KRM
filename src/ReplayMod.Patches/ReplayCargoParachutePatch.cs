using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

// Format 10 recreates cargo chutes from recorded visual state. The native
// delayed spawn would otherwise create a second, independently simulated rig.
[HarmonyPatch(typeof(GroundVehicle),"CheckAirdrop")]
internal static class ReplayGroundVehicleAirdropPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref UniTask __result)
    {
        if(!ReplayRuntime.IsPlayback||ReplayRuntime.FormatMajor<10)return true;
        __result=UniTask.CompletedTask;return false;
    }
}
