using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

// Offline replay worlds do not need a delayed join prompt. The native wait
// otherwise retains each destroyed replay world while playback is paused.
[HarmonyPatch(typeof(NuclearOption.Networking.Player),"WaitShowJoinMenu")]
internal static class ReplayJoinMenuWaitPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref UniTask __result)
    {
        if(!ReplayRuntime.IsPlayback)return true;
        __result=UniTask.CompletedTask;
        return false;
    }
}
