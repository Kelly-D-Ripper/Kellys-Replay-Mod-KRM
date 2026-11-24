using System.Collections.Generic;
using Mirage;
using ReplayMod.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ReplayMod.Director;

public sealed class PresentationOverrides
{
    private LevelInfo level;
    private float hour, factor, conditions, clouds, wind, heading, mainHeading, turbulence, randomHeading;
    private float? moon;
    private readonly VisibilityState<Canvas> canvases = new VisibilityState<Canvas>();
    private readonly VisibilityState<GraphicRaycaster> raycasters = new VisibilityState<GraphicRaycaster>();
    private readonly CaptureDiscoverySchedule captureDiscovery=new CaptureDiscoverySchedule();
    private static PresentationOverrides activeCapture;
    private static readonly System.Func<Canvas,bool> CanvasAlive=canvas=>canvas!=null;
    private static readonly System.Func<Canvas,bool> CanvasVisible=canvas=>canvas.enabled;
    private static readonly System.Action<Canvas,bool> SetCanvasVisible=(canvas,visible)=>canvas.enabled=visible;
    private static readonly System.Func<GraphicRaycaster,bool> RaycasterAlive=raycaster=>raycaster!=null;
    private static readonly System.Func<GraphicRaycaster,bool> RaycasterVisible=raycaster=>raycaster.enabled;
    private static readonly System.Action<GraphicRaycaster,bool> SetRaycasterVisible=(raycaster,visible)=>raycaster.enabled=visible;
    private bool captureActive;
    private bool enforcingCapture;
    public void Update(ReplayDirector director)
    {
        var p=director.Project;
        if(p.environment.enabled)
        {
            var current=NetworkSceneSingleton<LevelInfo>.i;
            if(current != null && level != current)
            {
                RestoreEnvironment(); level=current; hour=current.timeOfDay;factor=current.timeFactor;conditions=current.conditions;clouds=current.cloudHeight;
                wind=current.windSpeed;heading=current.GetWindHeading();mainHeading=current.windMainHeading;turbulence=current.windTurbulence;moon=current.moonPhase;randomHeading=current.windRandomArc;
                ApplyEnvironment(p.environment);
            }
        }
        else RestoreEnvironment();
    }
    public void ApplyEnvironment(EnvironmentSettings e)
    {
        if(level==null || !e.Valid())return;
        level.Networkconditions=e.conditions;level.NetworkcloudHeight=e.cloudHeight;
        level.timeFactor=0; level.windRandomArc=0;level.SetWindSpeed(e.windSpeed);level.SetWindHeading(e.windHeading);level.SetWindTurbulence(e.turbulence);
        level.NetworkmoonPhase=e.moonPhase;level.SetMoonPhase(e.moonPhase);level.SetTimeOfDay(e.hour);
    }
    public void Restore()
    {UpdateCapture(false);RestorePlayback();CursorManager.Refresh();Cursor.visible=CursorManager.Visible;}
    public void RestorePlayback() {RestoreEnvironment();ReplayInteriorPresentation.Clear();ReplayCloseCameraVisibility.Clear();}
    public void UpdateCapture(bool clean)
    {
        if(clean)
        {
            if(!captureActive)
            {
                captureActive=true;activeCapture=this;captureDiscovery.Reset();
                Canvas.willRenderCanvases+=HideBeforeRender;
                SceneManager.sceneLoaded+=CaptureSceneLoaded;
            }
            if(captureDiscovery.ShouldDiscover(Time.realtimeSinceStartup,Time.frameCount))DiscoverCaptureUi();
            HideBeforeRender();
        }
        else
        {
            if(captureActive)
            {
                captureActive=false;Canvas.willRenderCanvases-=HideBeforeRender;
                SceneManager.sceneLoaded-=CaptureSceneLoaded;
                if(activeCapture==this)activeCapture=null;
            }
            canvases.Restore(CanvasAlive,SetCanvasVisible);
            raycasters.Restore(RaycasterAlive,SetRaycasterVisible);
        }
    }
    private void CaptureSceneLoaded(Scene scene,LoadSceneMode mode)=>captureDiscovery.Invalidate();
    private void DiscoverCaptureUi()
    {
        // One bounded-rate query replaces three whole-scene scans per frame and
        // per render pass. Arbitrary world objects are never deactivated here.
        foreach(var item in Object.FindObjectsByType(typeof(Canvas),FindObjectsInactive.Include,FindObjectsSortMode.None))
            RegisterCanvas((Canvas)item);
    }
    public static void RegisterCaptureUi(Component component)
    {
        if(activeCapture==null || !activeCapture.captureActive || component==null)return;
        activeCapture.RegisterCanvas(component.GetComponentInParent<Canvas>(true));
    }
    private void RegisterCanvas(Canvas canvas)
    {
        if(canvas==null || canvas.name=="Replay seek cover")return;
        canvases.HideSingle(canvas,CanvasAlive,CanvasVisible,SetCanvasVisible);
        var raycaster=canvas.GetComponent<GraphicRaycaster>();
        if(raycaster!=null)raycasters.HideSingle(raycaster,RaycasterAlive,RaycasterVisible,SetRaycasterVisible);
    }
    private void HideBeforeRender()
    {
        if(!captureActive || enforcingCapture)return;
        enforcingCapture=true;
        try
        {
            canvases.HideKnown(CanvasAlive,CanvasVisible,SetCanvasVisible);
            raycasters.HideKnown(RaycasterAlive,RaycasterVisible,SetRaycasterVisible);
            Cursor.visible=false;
        }
        finally{enforcingCapture=false;}
    }
    private void RestoreEnvironment()
    {
        if(level!=null && !ReplayWorldPlayback.RestoreRecordedEnvironment(level)) { level.Networkconditions=conditions;level.NetworkcloudHeight=clouds;level.timeFactor=factor;level.windRandomArc=randomHeading;level.SetWindSpeed(wind);level.SetWindHeading(heading);level.windMainHeading=mainHeading;level.SetWindTurbulence(turbulence);level.NetworkmoonPhase=moon;if(moon.HasValue)level.SetMoonPhase(moon.Value);level.SetTimeOfDay(hour); }
        level=null;
    }
}
