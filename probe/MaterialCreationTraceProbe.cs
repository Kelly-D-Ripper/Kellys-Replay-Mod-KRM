using System;
using System.Collections.Generic;
using HarmonyLib;
using ReplayMod.Core;
using UnityEngine;

// Test-only provenance. IDs, never Unity object references, are retained.
[HarmonyPatch(typeof(NavLights.NavLight),"Initialize")]
public static class MaterialNavTraceProbe
{
    public static readonly HashSet<int> Materials=new HashSet<int>();
    public static readonly Dictionary<int,string> Reasons=new Dictionary<int,string>();
    public static int OwnersAdded,ExistingLeftAlone,Destroyed;
    public static void VerifyProductionGuards()
    {
        var assembly=typeof(ReplayManager).Assembly;
        var patch=assembly.GetType("ReplayMod.Patches.ReplayNavMaterialCleanupPatch",true);
        var leaseType=assembly.GetType("ReplayMod.Patches.ReplayNavMaterialLease",true);
        var ownerType=assembly.GetType("ReplayMod.Patches.ReplayNavMaterialOwner",true);
        var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
        var prefix=patch.GetMethod("Prefix",flags);var postfix=patch.GetMethod("Postfix",flags);
        var released=leaseType.GetField("released",flags);var add=ownerType.GetMethod("Add",flags);
        var shader=Shader.Find("Standard")??Shader.Find("Unlit/Color");
        if(shader==null)foreach(var existing in Resources.FindObjectsOfTypeAll<Material>())if(existing!=null&&existing.shader!=null){shader=existing.shader;break;}
        if(shader==null)throw new Exception("Missing production guard test shader");
        var go=new GameObject("isolated-production-shared-guard");var renderer=go.AddComponent<MeshRenderer>();var shared=new Material(shader);renderer.sharedMaterial=shared;
        try
        {
            var light=new NavLights.NavLight{renderer=renderer,baseMaterial=shared};object[] args={light,null};prefix.Invoke(null,args);postfix.Invoke(null,new[]{(object)light,args[1]});
            if(go.GetComponent(ownerType)!=null)throw new Exception("Production claimed an existing shared material");
        }
        finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(shared);}
        var parent=new GameObject("isolated-production-detached-parent");var child=new GameObject("isolated-production-detached-child");
        var part=child.AddComponent<MeshRenderer>();child.transform.SetParent(parent.transform);var material=new Material(shader);
        object lease=Activator.CreateInstance(leaseType,flags,null,new object[]{material,part},null);add.Invoke(null,new object[]{parent,lease,true});
        child.transform.SetParent(null);UnityEngine.Object.DestroyImmediate(parent);
        if((bool)released.GetValue(lease))throw new Exception("Production released detached renderer material");
        UnityEngine.Object.DestroyImmediate(child);UnityEngine.Object.DestroyImmediate(material);
        parent=new GameObject("isolated-production-inactive-parent");child=new GameObject("isolated-production-inactive-child");
        part=child.AddComponent<MeshRenderer>();child.transform.SetParent(parent.transform);child.SetActive(false);material=new Material(shader);
        lease=Activator.CreateInstance(leaseType,flags,null,new object[]{material,part},null);add.Invoke(null,new object[]{parent,lease,true});
        UnityEngine.Object.DestroyImmediate(parent);
        if(!(bool)released.GetValue(lease))throw new Exception("Production failed to reclaim inactive child material");
        UnityEngine.Object.DestroyImmediate(material);
    }
    public static void VerifySharedMaterialGuard()
    {
        var shader=Shader.Find("Standard")??Shader.Find("Unlit/Color");
        if(shader==null)foreach(var existing in Resources.FindObjectsOfTypeAll<Material>())if(existing!=null&&existing.shader!=null){shader=existing.shader;break;}
        if(shader==null)throw new Exception("Missing test shader");
        var go=new GameObject("isolated-shared-material-guard");var renderer=go.AddComponent<MeshRenderer>();var shared=new Material(shader);renderer.sharedMaterial=shared;
        try
        {
            var light=new NavLights.NavLight{renderer=renderer,baseMaterial=shared};
            Prefix(light,out var before);Postfix(light,before);
            if(go.GetComponent<MaterialNavOwnerProbe>()!=null)throw new Exception("Existing shared material was incorrectly claimed");
        }
        finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(shared);}
        var parent=new GameObject("isolated-detached-material-parent");var child=new GameObject("isolated-detached-material-child");
        var detachedRenderer=child.AddComponent<MeshRenderer>();child.transform.SetParent(parent.transform);var detachedMaterial=new Material(shader);
        parent.AddComponent<MaterialNavOwnerProbe>().Track(detachedMaterial,detachedRenderer,true);child.transform.SetParent(null);
        int priorDestroyed=Destroyed;UnityEngine.Object.DestroyImmediate(parent);
        if(Destroyed!=priorDestroyed)throw new Exception("Detached renderer material was destroyed by former ancestor");
        UnityEngine.Object.DestroyImmediate(child);UnityEngine.Object.DestroyImmediate(detachedMaterial);
        parent=new GameObject("isolated-inactive-material-parent");child=new GameObject("isolated-inactive-material-child");
        var inactiveRenderer=child.AddComponent<MeshRenderer>();child.transform.SetParent(parent.transform);child.SetActive(false);var inactiveMaterial=new Material(shader);
        parent.AddComponent<MaterialNavOwnerProbe>().Track(inactiveMaterial,inactiveRenderer,true);
        priorDestroyed=Destroyed;UnityEngine.Object.DestroyImmediate(parent);
        if(Destroyed!=priorDestroyed+1)throw new Exception("Inactive child material was not reclaimed with its ancestor");
        UnityEngine.Object.DestroyImmediate(inactiveMaterial);
    }
    [HarmonyPrefix] public static void Prefix(NavLights.NavLight __instance,out Material __state)
    {
        __state=null;
        if(Environment.GetEnvironmentVariable("REPLAY_TEST_NAV_CLEANUP")=="1"&&ReplayRuntime.IsPlayback&&__instance?.renderer!=null)
            __state=__instance.renderer.sharedMaterial;
    }
    [HarmonyPostfix] public static void Postfix(NavLights.NavLight __instance,Material __state)
    {
        if(Environment.GetEnvironmentVariable("REPLAY_STRESS_CREATION_TRACE")=="1"&&__instance?.baseMaterial!=null)
        {
            Materials.Add(__instance.baseMaterial.GetInstanceID());
            Reasons[__instance.baseMaterial.GetInstanceID()]="playback="+ReplayRuntime.IsPlayback+"/state="+(ReplayManager.i==null?"none":ReplayManager.i.GetState().ToString())+"/renderer="+(__instance.renderer==null?"none":__instance.renderer.gameObject.name);
        }
        if(Environment.GetEnvironmentVariable("REPLAY_TEST_NAV_CLEANUP")!="1"||!ReplayRuntime.IsPlayback||__instance?.renderer==null||__instance.baseMaterial==null)return;
        if(__state==__instance.baseMaterial){ExistingLeftAlone++;return;}
        var target=__instance.renderer.gameObject;
        var owner=target.GetComponent<MaterialNavOwnerProbe>()??target.AddComponent<MaterialNavOwnerProbe>();
        owner.Track(__instance.baseMaterial,__instance.renderer,false);OwnersAdded++;
        if(!target.activeInHierarchy)
        {
            var parent=target.transform.parent;
            while(parent!=null&&!parent.gameObject.activeInHierarchy)parent=parent.parent;
            if(parent!=null)
            {
                var ancestor=parent.GetComponent<MaterialNavOwnerProbe>()??parent.gameObject.AddComponent<MaterialNavOwnerProbe>();
                ancestor.Track(__instance.baseMaterial,__instance.renderer,true);
            }
        }
    }
}
public sealed class MaterialNavOwnerProbe:MonoBehaviour
{
    private sealed class Entry{public Material material;public Renderer renderer;public bool requireChild;}
    private readonly List<Entry> owned=new List<Entry>();
    public void Track(Material material,Renderer renderer,bool requireChild)
    {if(material!=null&&!owned.Exists(e=>e.material==material))owned.Add(new Entry{material=material,renderer=renderer,requireChild=requireChild});}
    public bool Owns(Material material)=>owned.Exists(e=>e.material==material);
    private void OnDestroy()
    {
        foreach(var entry in owned)
            if(entry.material!=null&&(!entry.requireChild||entry.renderer==null||entry.renderer.transform.IsChildOf(transform)))
            {Destroy(entry.material);MaterialNavTraceProbe.Destroyed++;}
        owned.Clear();
    }
}
[HarmonyPatch(typeof(MeshLightSettings),"Awake")]
public static class MaterialMeshTraceProbe
{
    public static readonly HashSet<int> Materials=new HashSet<int>();
    [HarmonyPostfix] public static void Postfix(MeshLightSettings __instance)
    {
        if(Environment.GetEnvironmentVariable("REPLAY_STRESS_CREATION_TRACE")=="1"&&__instance?.meshLightBlink!=null)
            Materials.Add(__instance.meshLightBlink.GetInstanceID());
    }
}
