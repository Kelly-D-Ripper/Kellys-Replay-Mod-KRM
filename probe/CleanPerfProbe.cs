using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;
using NuclearOption.SceneLoading;
using ReplayMod.Director;
using ReplayMod.UI;
using UnityEngine;

public static class CleanPerfProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    public static async UniTask Run(MapKey key,bool live,Action<string> log)
    {
        var director=ReplayDirector.i;Player player=null;
        if(live)
        {
            RollingRecordingController.Instance.SetEnabled(false);await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Live clean UI performance");
            if(!RollingRecordingController.IsUsableGameplay())throw new Exception("Live mission unavailable");TimeScaleManager.Scale=1;
            var def=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
            var craft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
            if(craft==null)throw new Exception("Live aircraft unavailable");
        }
        else{player=ReplayManager.i.Playback;player.Pause();await player.TimelineJump(5,0,false);director.EnterFreeCamera();}
        var studio=TrailerSuiteUi.Instance;if(!studio.IsVisible)studio.Toggle();await Wait(1);
        var manager=SceneSingleton<CameraStateManager>.i;var camera=manager.mainCamera;Vector3 position=camera.transform.position;Quaternion rotation=camera.transform.rotation;float fov=camera.fieldOfView;
        log("CLEAN PERF live="+live+" device="+SystemInfo.graphicsDeviceName+" resolution="+Screen.width+"x"+Screen.height+" targetFPS="+Application.targetFrameRate+" vsync="+QualitySettings.vSyncCount);
        foreach(bool hidden in new[]{false,true,false})
        {
            director.SetClean(hidden);await Wait(2);if(director.Clean!=hidden||studio.IsVisible==hidden)throw new Exception("UI state failed");
            var times=new List<float>();float end=Time.realtimeSinceStartup+12;
            while(Time.realtimeSinceStartup<end){await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);if(Time.unscaledDeltaTime>0)times.Add(Time.unscaledDeltaTime);}
            if(times.Count<60)throw new Exception("Insufficient frame samples");
            if(Vector3.Distance(position,camera.transform.position)>.01||Quaternion.Angle(rotation,camera.transform.rotation)>.01||Math.Abs(fov-camera.fieldOfView)>.01||(!live&&!player.IsPaused)||(live&&!MissionManager.IsRunning))throw new Exception("Camera or playback changed during comparison");
            times.Sort();double mean=times.Average();int tail=Math.Min(times.Count-1,(int)(times.Count*.99));float p99=times[tail];double onePercentLow=1/times.Skip(tail).Average();
            log("CLEAN PERF hidden="+hidden+" frames="+times.Count+" averageFPS="+(1/mean).ToString("F1")+" p99FrameMs="+(p99*1000).ToString("F2")+" onePercentLowFPS="+onePercentLow.ToString("F1"));
        }
        director.SetClean(false);log("CLEAN PERF PASS fixed-camera shown/hidden/shown measurement completed");
    }
}
