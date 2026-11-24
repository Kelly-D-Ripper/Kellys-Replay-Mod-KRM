using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.Effects;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;
using UnityEngine.Rendering;

[HarmonyPatch(typeof(DetailRenderer),"LateUpdate")]
public static class FoliageOrderObserver
{
    static readonly FieldInfo invoking=AccessTools.Field(typeof(ReplayMod.Patches.ReplayDetailRenderOrder),"invoking");
    static readonly FieldInfo instance=AccessTools.Field(typeof(ReplayMod.Patches.ReplayDetailRenderOrder),"instance");
    static readonly FieldInfo detailCamera=AccessTools.Field(typeof(DetailRenderer),"camera");
    public static bool active;public static int updates,matches,otherCameras;public static float maxPosition,maxAngle,maxFov;
    static int sampleFrame=-1;static Camera sampleCamera;static Vector3 pos;static Quaternion rot;static float fov;
    [HarmonyPostfix,HarmonyPriority(Priority.Last)] public static void Postfix(DetailRenderer __instance)
    {
        if(!active)return;var runner=instance.GetValue(null);if(runner==null||!(bool)invoking.GetValue(runner))return;
        var camera=(Camera)detailCamera.GetValue(__instance);if(camera==null)return;updates++;sampleFrame=Time.frameCount;sampleCamera=camera;pos=camera.transform.position;rot=camera.transform.rotation;fov=camera.fieldOfView;
    }
    public static void BeforeRender(ScriptableRenderContext _,Camera camera)
    {
        if(!active||Time.frameCount!=sampleFrame||camera==null)return;
        if(camera!=sampleCamera){otherCameras++;return;}matches++;
        maxPosition=Math.Max(maxPosition,Vector3.Distance(pos,camera.transform.position));maxAngle=Math.Max(maxAngle,Quaternion.Angle(rot,camera.transform.rotation));maxFov=Math.Max(maxFov,Math.Abs(fov-camera.fieldOfView));
    }
}
public static class FoliageOrderProbe
{
    public static async UniTask Run(Action<string> log)
    {
        var player=ReplayManager.i.Playback;player.Pause();await player.TimelineJump(2,0,false);
        var d=ReplayDirector.i;d.EnterFreeCamera();var camera=SceneSingleton<CameraStateManager>.i;if(camera==null||camera.mainCamera==null)throw new Exception("Replay camera unavailable");
        var origin=camera.transform.position.ToGlobalPosition();Quaternion q=Quaternion.Euler(25,0,0);
        CameraKey Point(double t,float x,float z)=>new CameraKey{time=t,x=origin.x+x,y=origin.y,z=origin.z+z,qx=q.x,qy=q.y,qz=q.z,qw=q.w,fov=20,easing=1};
        d.Project.cameras.Clear();d.Project.cameras.Add(Point(2,0,0));d.Project.cameras.Add(Point(4,500,100));d.Project.cameras.Add(Point(6,500,600));d.Project.animatePathFov=true;d.Project.smoothCameraPath=true;
        if(!d.PlayCameraPath())throw new Exception("Path did not start: "+d.Status);
        FoliageOrderObserver.active=true;RenderPipelineManager.beginCameraRendering+=FoliageOrderObserver.BeforeRender;
        try
        {
            d.SetPlaybackSpeed(1);d.SetClean(true);player.Resume();float deadline=Time.realtimeSinceStartup+20;double nextCapture=2.2;int captures=0;
            while(player.currentVirtualTime<5.8){if(Time.realtimeSinceStartup>deadline)throw new Exception("Path playback stalled");
                if(player.currentVirtualTime>=nextCapture){string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ghost-probe","foliage-v18-"+captures.ToString("00")+".png");if(File.Exists(file))throw new Exception("Frame destination already exists");ScreenCapture.CaptureScreenshot(file);captures++;nextCapture+=.4;}
                await UniTask.NextFrame();}
            player.Pause();log("FOLIAGE ORDER updates="+FoliageOrderObserver.updates+" matchingRenders="+FoliageOrderObserver.matches+" otherCameras="+FoliageOrderObserver.otherCameras+" maxPosition="+FoliageOrderObserver.maxPosition.ToString("F4")+" maxAngle="+FoliageOrderObserver.maxAngle.ToString("F4")+" maxFov="+FoliageOrderObserver.maxFov.ToString("F4")+" capturedFrames="+captures);
            if(FoliageOrderObserver.updates<30||FoliageOrderObserver.matches<30||FoliageOrderObserver.maxPosition>.01||FoliageOrderObserver.maxAngle>.05||FoliageOrderObserver.maxFov>.01)throw new Exception("Foliage culling camera differs from final rendered camera");
            log("FOLIAGE ORDER PASS 20-degree three-point moving path used matching culling and render camera poses");
        }
        finally{FoliageOrderObserver.active=false;RenderPipelineManager.beginCameraRendering-=FoliageOrderObserver.BeforeRender;d.SetClean(false);d.StopCameraPathPreview();}
    }
}
