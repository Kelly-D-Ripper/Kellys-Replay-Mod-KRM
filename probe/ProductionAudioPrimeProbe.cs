using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

[HarmonyPatch(typeof(Player),"ApplyPlaybackClock")]
internal static class ProductionAudioPrimeProbe
{
    internal sealed class Snapshot
    {
        internal readonly Dictionary<AudioSource,(float volume,bool mute)> sources=new Dictionary<AudioSource,(float,bool)>();
    }
    internal static int Primed,ProtectedChanged,Transitions;
    internal static bool Enabled=>Environment.GetEnvironmentVariable("REPLAY_MONITOR_PROP_AUDIO")=="1";
    [HarmonyPrepare] private static bool Prepare()=>Enabled;
    [HarmonyPrefix] private static void Prefix(Player __instance,out Snapshot __state)
    {
        __state=null;
        if(TimeScaleManager.Scale>0||!ReplayRuntime.IsPlayback||!__instance.enabled||__instance.IsPaused||__instance.IsSeeking)return;
        __state=new Snapshot();
        foreach(var craft in __instance.Actors().Select(pair=>pair.Value).OfType<Aircraft>())
            if(craft!=null&&craft.engines!=null)foreach(var prop in craft.engines.OfType<PropFan>())
                if(prop.source!=null)__state.sources[prop.source]=(prop.source.volume,prop.source.mute);
    }
    [HarmonyPostfix] private static void Postfix(Snapshot __state)
    {
        if(__state==null||TimeScaleManager.Scale<=0)return;
        Transitions++;
        foreach(var pair in __state.sources)
        {
            var source=pair.Key;if(source==null)continue;
            if(Math.Abs(source.volume-pair.Value.volume)<=.0001f)continue;
            if(pair.Value.mute||pair.Value.volume>.0001f)ProtectedChanged++;else if(source.volume>0)Primed++;
        }
    }
}
