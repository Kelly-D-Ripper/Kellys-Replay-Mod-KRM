using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayMod.Director;

// Hides managed plugin/debug IMGUI callbacks during intentional clean capture.
// Every plugin continues receiving Update and its gameplay/network callbacks.
public sealed class CleanCaptureImguiGuard : MonoBehaviour
{
    private static CleanCaptureImguiGuard instance;
    private readonly HashSet<Assembly> scannedAssemblies = new HashSet<Assembly>();
    private readonly HashSet<MethodInfo> patchedCallbacks = new HashSet<MethodInfo>();
    private readonly HashSet<MethodInfo> failedCallbacks = new HashSet<MethodInfo>();
    private readonly HashSet<MethodInfo> uiCallbacks = new HashSet<MethodInfo>();
    private Harmony harmony;
    private int failedAssemblyScans;
    private int lastPluginCount = -1;
    private int reportedCallbackCount = -1;
    private int reportedFailures = -1;
    private bool pendingPluginInstances;
    private bool uiDiscoveryDone;
    private float nextRefresh;
    public static int CallbackCount => instance?.patchedCallbacks.Count ?? 0;
    public static int FailureCount => instance == null ? 1 : instance.failedCallbacks.Count + instance.failedAssemblyScans;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
        harmony = new Harmony("com.chiester3105.replayMod.cleanCaptureImgui");
    }

    private void Start() { InstallUiLifecycleHooks(); DiscoverTargets(); }

    // Called before Clean becomes true, so known OnGUI methods are guarded from the first event.
    public static bool RefreshTargets()
    {
        if (instance == null)
        {
            if (ReplayDirector.i != null) ReplayDirector.i.Status = "Clean capture guard is unavailable; some mod overlays may remain.";
            return false;
        }
        instance.InstallUiLifecycleHooks();
        // Assemblies already guarded at startup stay guarded. F8 must not
        // repeat reflection discovery unless the plugin roster has changed.
        if(instance.pendingPluginInstances || Chainloader.PluginInfos.Count!=instance.lastPluginCount)
            return instance.DiscoverTargets();
        return FailureCount==0;
    }

    private void Update()
    {
        bool clean = ReplayDirector.i?.Clean == true;
        if (!clean || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 1;
        if (pendingPluginInstances || Chainloader.PluginInfos.Count != lastPluginCount) DiscoverTargets();
    }

    private void InstallUiLifecycleHooks()
    {
        if(uiDiscoveryDone)return;
        uiDiscoveryDone=true;
        var postfix=new HarmonyMethod(typeof(CleanCaptureImguiGuard).GetMethod(nameof(UiEnabled),BindingFlags.Static|BindingFlags.NonPublic));
        PatchUi(typeof(Graphic),"OnEnable",postfix);
        PatchUi(typeof(Graphic),"OnTransformParentChanged",postfix);
        PatchUi(typeof(CanvasScaler),"OnEnable",postfix);
        PatchUi(typeof(UnityEngine.EventSystems.BaseRaycaster),"OnEnable",postfix);
        Plugin.SafeLog($"Clean capture UI lifecycle guard: {uiCallbacks.Count} managed callbacks guarded.");
    }
    private void PatchUi(Type type,string name,HarmonyMethod postfix)
    {
        MethodInfo method=null;
        try
        {
            method=type.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            // These lifecycle methods must continue executing: this postfix only
            // registers their UI canvas. Native externs and destruction stay untouched.
            if(method==null || method.IsStatic || method.ReturnType!=typeof(void) || method.GetParameters().Length!=0 || method.GetMethodBody()==null)
            {failedAssemblyScans++;return;}
            harmony.Patch(method,postfix:postfix);uiCallbacks.Add(method);
        }
        catch(Exception){if(method!=null)failedCallbacks.Add(method);else failedAssemblyScans++;}
    }
    private static void UiEnabled(Component __instance)
    {
        if(ReplayDirector.i?.Clean==true && (__instance is Graphic || __instance is CanvasScaler || __instance is GraphicRaycaster))
            PresentationOverrides.RegisterCaptureUi(__instance);
    }

    private bool DiscoverTargets()
    {
        try
        {
            // The game has a few managed debug GUIs. Native Unity/IMGUI externs are excluded.
            ScanAssembly(typeof(GameManager).Assembly);
            var plugins = Chainloader.PluginInfos.Values.ToArray();
            lastPluginCount = plugins.Length;
            pendingPluginInstances = false;
            foreach (var plugin in plugins)
            {
                if (plugin.Instance == null) { pendingPluginInstances = true; continue; }
                ScanAssembly(plugin.Instance.GetType().Assembly);
            }
        }
        catch (Exception)
        {
            // Keep discovery failure visible without printing plugin paths or exception payloads.
            failedAssemblyScans++;
        }
        int failures = failedCallbacks.Count + failedAssemblyScans;
        if (reportedCallbackCount != patchedCallbacks.Count || reportedFailures != failures)
        {
            reportedCallbackCount = patchedCallbacks.Count; reportedFailures = failures;
            Plugin.SafeLog($"Clean capture IMGUI guard: {patchedCallbacks.Count} managed callbacks guarded, {failures} failures.");
            if (failures > 0)
            {
                Plugin.SafeLogWarning($"Clean capture could not guard {failures} managed UI callbacks or assemblies.");
                if (ReplayDirector.i != null) ReplayDirector.i.Status = "Some mod/debug overlays could not be hidden. Check clean capture before recording.";
            }
        }
        return failures == 0;
    }

    private void ScanAssembly(Assembly assembly)
    {
        if (assembly == null || !scannedAssemblies.Add(assembly)) return;
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        { types = ex.Types.Where(type => type != null).ToArray(); failedAssemblyScans++; }
        catch (Exception) { failedAssemblyScans++; return; }
        var prefix = new HarmonyMethod(typeof(CleanCaptureImguiGuard).GetMethod(nameof(ShouldDraw), BindingFlags.Static | BindingFlags.NonPublic));
        foreach (var type in types)
        {
            if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
            try
            {
                foreach (var callback in GuiCallbackFilter.FindCallbacks(type, typeof(MonoBehaviour)))
                {
                    if (patchedCallbacks.Contains(callback) || failedCallbacks.Contains(callback)) continue;
                    try { harmony.Patch(callback, prefix: prefix); patchedCallbacks.Add(callback); }
                    catch (Exception) { failedCallbacks.Add(callback); }
                }
            }
            catch (Exception) { failedAssemblyScans++; }
        }
    }

    // No HarmonyPrefix attribute: these are bounded, programmatically selected managed messages.
    private static bool ShouldDraw() => ReplayDirector.i?.Clean != true;

    private void OnDestroy()
    {
        if (instance != this) return;
        try { harmony?.UnpatchSelf(); }
        catch (Exception) { Plugin.SafeLogWarning("Clean capture IMGUI guard cleanup could not unpatch its callbacks."); }
        instance = null;
    }
}
