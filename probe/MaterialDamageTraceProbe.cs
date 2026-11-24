using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

// Diagnostic only: retain IDs and short labels, never Unity object references.
[HarmonyPatch(typeof(UnitPart),"ApplyDamage")]
public static class MaterialDamageTraceProbe
{
    public static readonly Dictionary<int,string> Created=new Dictionary<int,string>();
    [HarmonyPrefix] public static void Prefix(UnitPart __instance,out Dictionary<Renderer,HashSet<int>> __state)
    {
        __state=null;
        if(Environment.GetEnvironmentVariable("REPLAY_STRESS_DAMAGE_MATERIAL_TRACE")!="1"||!ReplayRuntime.IsPlayback||__instance==null)return;
        var parts=__instance.damageMaterial?.renderers;if(parts==null)return;
        __state=new Dictionary<Renderer,HashSet<int>>();
        foreach(var renderer in parts)
        {
            if(renderer==null||__state.ContainsKey(renderer))continue;
            __state[renderer]=new HashSet<int>(renderer.sharedMaterials.Where(m=>m!=null).Select(m=>m.GetInstanceID()));
        }
    }
    [HarmonyPostfix] public static void Postfix(UnitPart __instance,Dictionary<Renderer,HashSet<int>> __state)
    {
        if(__state==null)return;
        foreach(var item in __state)
        {
            if(item.Key==null)continue;
            foreach(var material in item.Key.sharedMaterials)
                if(material!=null&&!item.Value.Contains(material.GetInstanceID()))
                    Created[material.GetInstanceID()]=(__instance?.parentUnit==null?"unknown":__instance.parentUnit.name)+"/"+(__instance==null?"unknown":__instance.name);
        }
    }
}
