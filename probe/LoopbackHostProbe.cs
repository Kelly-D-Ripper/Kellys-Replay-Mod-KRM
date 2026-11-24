using System;
using System.IO;
using System.Net;
using System.Threading;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Mirage.SocketLayer;
using Mirage.Sockets.Udp;
using NuclearOption.Networking;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Misc;
using UnityEngine;

// Test harness only: the native UDP transport normally binds every interface.
// The local multiplayer fixture refuses to run unless this loopback patch ran.
internal static class LoopbackHostProbe
{
    internal static bool Enabled=>Environment.GetEnvironmentVariable("REPLAY_LOOPBACK_HOST")=="1";
    internal static bool BoundLoopback;
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        if(!ushort.TryParse(Environment.GetEnvironmentVariable("REPLAY_LOOPBACK_PORT"),out ushort port)||port<49152)
            throw new Exception("Expected a high local-only test port");
        string directory=Environment.GetEnvironmentVariable("REPLAY_LOOPBACK_DIR");
        if(string.IsNullOrWhiteSpace(directory)||!Directory.Exists(directory))throw new Exception("Missing isolated coordination directory");
        string ready=Path.Combine(directory,"host-ready.txt"),stop=Path.Combine(directory,"stop.txt");
        if(File.Exists(ready)||File.Exists(stop))throw new Exception("Coordination files already exist");
        Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
        await ReplayManager.i.SwitchState(ModStates.Idle);await SceneHelper.StopReplayConnectionAsync();
        var mission=SceneHelper.CreateEmptyMission(key,"Isolated loopback replay test");
        MissionManager.SetMission(mission,checkIfSame:false);GameManager.ClearDisconnectReason();
        var manager=NetworkManagerNuclearOption.i;
        await manager.StartHostAsync(new HostOptions(SocketType.UDP,GameState.Multiplayer,key){UdpPort=port,MaxConnections=2});
        if(!BoundLoopback||!manager.Server.Active||!manager.Client.Active)throw new Exception("Loopback host did not start with the guarded bind");
        float deadline=Time.realtimeSinceStartup+60;
        while(!MissionManager.IsRunning||NetworkSceneSingleton<Spawner>.i==null)
        {if(Time.realtimeSinceStartup>deadline)throw new Exception("Loopback mission did not become ready");await UniTask.NextFrame();}
        File.WriteAllText(ready,"loopback-only port="+port);log("LOOPBACK HOST READY UDP ::1:"+port+"; native host active");
        deadline=Time.realtimeSinceStartup+90;
        while(!File.Exists(stop)&&Time.realtimeSinceStartup<deadline)await UniTask.NextFrame();
        await SceneHelper.StopReplayConnectionAsync();log("LOOPBACK HOST STOPPED");
    }
}

[HarmonyPatch(typeof(UdpSocketFactory),"CreateServerSocket")]
internal static class LoopbackSocketKindProbe
{
    [HarmonyPrepare] private static bool Prepare()=>LoopbackHostProbe.Enabled;
    [HarmonyPrefix] private static void Prefix(UdpSocketFactory __instance)=>__instance.SocketLib=SocketLib.Managed;
}

[HarmonyPatch(typeof(UdpSocketFactory),"GetBindEndPoint")]
internal static class LoopbackBindProbe
{
    [HarmonyPrepare] private static bool Prepare()=>LoopbackHostProbe.Enabled;
    [HarmonyPrefix] private static bool Prefix(UdpSocketFactory __instance,ref IBindEndPoint __result)
    {
        if(__instance.SocketLib!=SocketLib.Managed||__instance.Port<49152)throw new InvalidOperationException("Refusing a non-loopback test socket");
        __result=new UdpConnectionHandle(new IPEndPoint(IPAddress.IPv6Loopback,__instance.Port));
        LoopbackHostProbe.BoundLoopback=true;return false;
    }
}
