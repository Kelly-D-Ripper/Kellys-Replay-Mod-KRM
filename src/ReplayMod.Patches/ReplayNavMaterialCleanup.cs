using System;
using System.Collections.Generic;
using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Patches;

// Native navigation-light initialization requests a per-renderer material.
// Tie copies created during replay to that renderer's lifetime; never claim the
// shared material which existed before initialization.
[HarmonyPatch(typeof(NavLights.NavLight),"Initialize")]
internal static class ReplayNavMaterialCleanupPatch
{
    internal struct Before
    {
        internal bool active;
        internal Renderer renderer;
        internal Material material;
    }

    [HarmonyPrefix]
    private static void Prefix(NavLights.NavLight __instance,out Before __state)
    {
        __state=default;
        if(!ReplayRuntime.IsPlayback||__instance?.renderer==null)return;
        var renderer=__instance.renderer;
        if(!renderer.gameObject.scene.IsValid())return;
        __state=new Before{active=true,renderer=renderer,material=renderer.sharedMaterial};
    }

    [HarmonyPostfix]
    private static void Postfix(NavLights.NavLight __instance,Before __state)
    {
        if(!__state.active||__instance==null||__instance.renderer!=__state.renderer)return;
        var renderer=__state.renderer;var material=__instance.baseMaterial;
        if(!ReplayNavMaterialPolicy.ShouldTrack(ReplayRuntime.IsPlayback,renderer!=null,material!=null&&material!=__state.material))return;
        try
        {
            var lease=new ReplayNavMaterialLease(material,renderer);
            ReplayNavMaterialOwner.Add(renderer.gameObject,lease,false);
            // Unity skips OnDestroy for a GameObject which was never active.
            // A previously active ancestor handles those lights; a detached
            // renderer is not released by that former ancestor.
            if(!renderer.gameObject.activeInHierarchy)
            {
                var parent=renderer.transform.parent;
                while(parent!=null&&!parent.gameObject.activeInHierarchy)parent=parent.parent;
                if(parent!=null)ReplayNavMaterialOwner.Add(parent.gameObject,lease,true);
            }
        }
        catch(Exception error){Plugin.SafeLogWarning("Could not track replay navigation material: "+error.Message);}
    }
}

internal sealed class ReplayNavMaterialLease
{
    private Material material;
    private Renderer renderer;
    private bool released;
    internal ReplayNavMaterialLease(Material material,Renderer renderer){this.material=material;this.renderer=renderer;}
    internal void Release(Transform owner,bool ancestor)
    {
        if(released||material==null)return;
        bool exists=renderer!=null;
        bool child=exists&&ancestor&&renderer.transform.IsChildOf(owner);
        if(!ReplayNavMaterialPolicy.CanRelease(ancestor,exists,child))return;
        released=true;UnityEngine.Object.Destroy(material);material=null;renderer=null;
    }
}

internal sealed class ReplayNavMaterialOwner : MonoBehaviour
{
    private struct Entry {internal ReplayNavMaterialLease lease;internal bool ancestor;}
    private readonly List<Entry> entries=new List<Entry>();
    internal static void Add(GameObject target,ReplayNavMaterialLease lease,bool ancestor)
    {
        var owner=target.GetComponent<ReplayNavMaterialOwner>()??target.AddComponent<ReplayNavMaterialOwner>();
        owner.entries.Add(new Entry{lease=lease,ancestor=ancestor});
    }
    private void OnDestroy()
    {
        foreach(var entry in entries)entry.lease.Release(transform,entry.ancestor);
        entries.Clear();
    }
}
