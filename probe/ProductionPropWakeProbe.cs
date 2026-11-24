using System;
using System.Collections.Generic;
using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

// Observe the built DLL's selected sources without changing the playback.
[HarmonyPatch(typeof(UnitController),"WakePrimedPropAudio")]
internal static class ProductionPropWakeProbe
{
    internal static bool Enabled=>Environment.GetEnvironmentVariable("REPLAY_MONITOR_PROP_WAKE")=="1";
    internal static int selected,invalid,phaseMismatches;
    internal sealed class Entry {internal AudioSource source;internal AudioClip clip;internal int sample;}
    internal sealed class State {internal readonly List<Entry> entries=new List<Entry>();}
    [HarmonyPrepare] private static bool Prepare()=>Enabled;
    [HarmonyPrefix] private static void Prefix(List<(AudioSource source,AudioClip clip)> primed,out State __state)
    {
        __state=new State();if(primed==null)return;
        foreach(var item in primed)
        {
            var source=item.source;var clip=item.clip;
            if(source==null||clip==null||source.clip!=clip||source.mute||!source.isPlaying||source.volume<=.0001f||!source.loop||clip.loadState!=AudioDataLoadState.Loaded||clip.loadType!=AudioClipLoadType.DecompressOnLoad){invalid++;continue;}
            selected++;__state.entries.Add(new Entry{source=source,clip=clip,sample=source.timeSamples});
        }
    }
    [HarmonyPostfix] private static void Postfix(State __state)
    {
        if(__state==null)return;foreach(var entry in __state.entries)
        {
            if(entry.source==null||entry.source.clip!=entry.clip||!entry.source.isPlaying||Math.Abs(entry.source.timeSamples-entry.sample)>2048)phaseMismatches++;
        }
    }
}
