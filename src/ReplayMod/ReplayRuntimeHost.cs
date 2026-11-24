using ReplayMod.Diagnostics;
using ReplayMod.Misc;
using UnityEngine;

namespace ReplayMod;

// An owned root avoids changing the shared BepInEx manager or sibling plugins.
// It also gives startup diagnostics a lifecycle independent of that manager.
public sealed class ReplayRuntimeHost : MonoBehaviour
{
    public static ReplayRuntimeHost Instance { get; private set; }
    private void Awake() { Instance=this;ReplayMod.Patches.ReplayPhysicsContinuationLifetime.Initialize();ReplayStartup.Mark("Owned runtime Awake"); }
    private void Start()
    {
        try { ReflectionCache.Init();ReplayStartup.Mark("Owned runtime Start"); }
        catch(System.Exception error) { ReplayStartup.Mark("Reflection initialization failed");Plugin.logger?.LogError(error); }
    }
    private void Update() { ReplayStartup.Tick();Plugin.DrainActions();ReplayMod.Patches.ReplayPhysicsContinuationLifetime.Pump(); }
    private void OnApplicationQuit()
    {
        ReplayStartup.Mark("Application quitting");
        if(!ReplayMod.Core.ReplayRecordingRecovery.WaitForPendingWritesOnQuit())
            Plugin.SafeLogWarning("A replay file is still saving as the game exits; its partial may be recoverable.");
    }
    private void OnDestroy()
    {
        if(Instance!=this)return;
        Instance=null;ReplayStartup.Mark("Owned runtime destroyed");ReplayDiagnostics.Shutdown();
    }
}
