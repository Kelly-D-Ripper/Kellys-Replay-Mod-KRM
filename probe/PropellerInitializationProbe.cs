using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

// Isolated experiment only: check whether initializing the native propeller
// presentation values after the replay clock starts affects audio onset.
[HarmonyPatch(typeof(Player),"ApplyPlaybackClock")]
internal static class PropellerInitializationProbe
{
    internal static int changed,skippedProtected,phaseMismatches;
    internal sealed class State { internal bool transition;internal readonly Dictionary<AudioSource,AudioClip> silent=new Dictionary<AudioSource,AudioClip>(); }
    private static string Mode=>Environment.GetEnvironmentVariable("REPLAY_TEST_PROP_INITIALIZATION");
    [HarmonyPrepare] private static bool Prepare()=>!string.IsNullOrEmpty(Mode);
    [HarmonyPrefix,HarmonyPriority(Priority.First)] private static void Prefix(Player __instance,out State __state)
    {
        __state=new State{transition=TimeScaleManager.Scale<=0&&ReplayRuntime.IsPlayback&&__instance.enabled&&!__instance.IsPaused&&!__instance.IsSeeking};
        if(!__state.transition||Mode!="fresh")return;
        foreach(var craft in __instance.Actors().Select(x=>x.Value).OfType<Aircraft>())
            if(craft!=null&&craft.engines!=null)foreach(var prop in craft.engines.OfType<PropFan>())
            {
                var source=prop.source;if(source==null)continue;
                if(!source.mute&&source.isPlaying&&source.volume<=.0001f&&source.loop&&source.clip!=null&&source.clip.loadState==AudioDataLoadState.Loaded&&source.clip.loadType!=AudioClipLoadType.Streaming)
                    __state.silent[source]=source.clip;
                else skippedProtected++;
            }
    }
    [HarmonyPostfix,HarmonyPriority(Priority.Last)] private static void Postfix(Player __instance,State __state)
    {
        if(__state==null||!__state.transition||TimeScaleManager.Scale<=0)return;
        var controller=typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(__instance);
        var recorded=controller?.GetType().GetField("_engineRpm",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(controller) as Dictionary<uint,float[]>;
        if(recorded==null)return;
        foreach(var pair in __instance.Actors())
        {
            if(!(pair.Value is Aircraft aircraft)||aircraft.engines==null||aircraft.controlInputs==null||!recorded.TryGetValue(pair.Key,out var rpm))continue;
            for(int i=0;i<aircraft.engines.Count&&i<rpm.Length;i++)
            {
                if(!(aircraft.engines[i] is PropFan prop)||prop.source==null||prop.source.mute||!prop.source.isPlaying||prop.nominalRPM<=0)continue;
                float ratio=rpm[i]/prop.nominalRPM,drive=Mathf.Min(prop.powerRatio,aircraft.controlInputs.throttle);
                float pitch=prop.pitchMultiplier*(prop.rpmPitchPortion*ratio+prop.aoaPitchPortion*drive*ratio);
                if(float.IsNaN(ratio)||float.IsInfinity(ratio)||float.IsNaN(pitch)||float.IsInfinity(pitch)||pitch<=0)continue;
                if(Mode=="ratio"||Mode=="both")prop.rpmRatio=ratio;
                if(Mode=="pitch"||Mode=="both")prop.source.pitch=pitch;
                if(Mode=="volume") {float volume=prop.source.volume;prop.source.volume=0;prop.source.volume=volume;}
                if(Mode=="restart") {int sample=prop.source.timeSamples;prop.source.Stop();prop.source.Play();prop.source.timeSamples=sample;}
                if(Mode=="fresh")
                {
                    if(!__state.silent.TryGetValue(prop.source,out var clip)||prop.source.clip!=clip||prop.source.volume<=.0001f)continue;
                    int sample=prop.source.timeSamples;if(sample<0||sample>=clip.samples)continue;
                    prop.source.Stop();prop.source.Play();prop.source.timeSamples=sample;
                    if(Math.Abs(prop.source.timeSamples-sample)>2048)phaseMismatches++;
                }
                changed++;
            }
        }
    }
}
