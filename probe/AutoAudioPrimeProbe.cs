using System;
using System.Linq;
using HarmonyLib;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;

// Test-only: set the native PropFan volume formula immediately before a
// paused replay resumes, instead of waiting for its first slow physics tick.
[HarmonyPatch(typeof(Player),"ApplyPlaybackClock")]
internal static class AutoAudioPrimeProbe
{
    internal static int Primed;
    internal static bool Enabled=>Environment.GetEnvironmentVariable("REPLAY_AUDIO_AUTO_PRIME")=="1";
    [HarmonyPrepare] private static bool Prepare()=>Enabled;
    [HarmonyPrefix] private static void Prefix(out float __state)=>__state=TimeScaleManager.Scale;
    [HarmonyPostfix] private static void Postfix(Player __instance,float __state)
    {
        if(__state>0||TimeScaleManager.Scale<=0||!ReplayRuntime.IsPlayback||!__instance.enabled||__instance.IsPaused||__instance.IsSeeking||
           !string.IsNullOrEmpty(__instance.PlaybackFailure)||ReplayDirector.i?.CameraTakeHoldsPlayback==true)return;
        foreach(var craft in __instance.Actors().Select(pair=>pair.Value).OfType<Aircraft>())
        {
            if(craft==null||craft.engines==null)continue;
            foreach(var prop in craft.engines.OfType<PropFan>())
            {
                var source=prop.source;
                if(source==null||source.mute||!source.isPlaying||source.volume>.0001f||prop.nominalRPM<=0)continue;
                float ratio=prop.currentRPM/prop.nominalRPM,drive=Mathf.Min(prop.powerRatio,craft.controlInputs.throttle);
                float volume=prop.volumeBase+prop.rpmVolumePortion*ratio+prop.aoaVolumePortion*drive*ratio;
                if(float.IsNaN(volume)||float.IsInfinity(volume)||volume<=0)continue;
                source.volume=Mathf.Clamp01(volume);Primed++;
            }
        }
    }
}
