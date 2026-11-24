using System;
using System.Collections.Generic;
using ReplayMod.Misc;
using UnityEngine;
using UnityEngine.Rendering;

namespace ReplayMod.Core;

// Only existing native renderers on one exact offline replay actor are touched.
// No camera possession, Pilot state, engine audio, actor transforms or prefabs
// are changed. A newly reconstructed actor receives a fresh binding.
public static class ReplayInteriorPresentation
{
    private sealed class RendererBinding
    {
        public Renderer renderer;
        public UnitPart part;
        public Pilot pilot;
        public Aircraft aircraft;
        public bool verifiedStandin;
        public uint actorId;
        public long actorGeneration;
    }
    private static readonly List<RendererBinding> cockpits=new List<RendererBinding>();
    private static readonly List<RendererBinding> exterior=new List<RendererBinding>();
    private static readonly List<RendererBinding> pilots=new List<RendererBinding>();
    private static readonly Dictionary<Renderer,RendererBinding> otherCockpits=new Dictionary<Renderer,RendererBinding>();
    private static readonly ReplayInteriorVisibilityState<Renderer> visibility=new ReplayInteriorVisibilityState<Renderer>(8192);
    private static readonly ReplayInteriorCameraMask cameraMask=new ReplayInteriorCameraMask();
    private static readonly ReplayCockpitFlightScreens flightScreens=new ReplayCockpitFlightScreens();
    private static readonly ReplayOwnedOverrideCache<Cockpit,bool> cockpitControls=new ReplayOwnedOverrideCache<Cockpit,bool>(16);
    private static Player owner;
    private static Aircraft target;
    private static uint actorId;
    private static long generation;
    private static bool detailed,seated,eyeView,requestedEyeView,showFlight,applying,subscribed;
    private static CameraStateManager cameraManager;
    private static UnityEngine.Camera overlay,main,nativeOverlay;
    private static bool originalOverlayEnabled;
    public static int CockpitRendererCount=>detailed?cockpits.Count:0;
    public static int PilotRendererCount=>seated?pilots.Count:0;
    public static int FlightScreenCount=>flightScreens.Count;

    public static void Update(Player playback,uint id,Aircraft aircraft,bool detailedCockpit,bool seatedPilots,bool cockpitEyeView=false,bool flightInstruments=true)
    {
        var manager=SceneSingleton<CameraStateManager>.i;
        var currentMain=manager!=null?manager.mainCamera:null;
        var currentOverlay=manager!=null?manager.cockpitCamRender:null;
        bool effectiveEye=cockpitEyeView||NativeEyeView(manager,aircraft);
        if((!detailedCockpit&&!seatedPilots&&!effectiveEye)||!ValidActor(playback,id,aircraft))
        {Clear();return;}
        long current=playback.CurrentActorGeneration(id);bool rebound=false;
        if(!ReferenceEquals(owner,playback)||!ReferenceEquals(target,aircraft)||actorId!=id||generation!=current||detailed!=detailedCockpit||seated!=seatedPilots||eyeView!=effectiveEye||
            !ReferenceEquals(cameraManager,manager)||!ReferenceEquals(main,currentMain)||!ReferenceEquals(nativeOverlay,currentOverlay))
        {
            Clear();owner=playback;target=aircraft;actorId=id;generation=current;detailed=detailedCockpit;seated=seatedPilots;eyeView=effectiveEye;
            if(detailed&&aircraft.cockpitRenderers!=null)
                foreach(var renderer in aircraft.cockpitRenderers)
                {
                    if(renderer==null||cockpits.Count>=240)continue;
                    cockpits.Add(new RendererBinding {renderer=renderer,part=renderer.GetComponentInParent<UnitPart>(true)});
                }
            if(detailed&&aircraft.exteriorRenderers!=null)
                foreach(var renderer in aircraft.exteriorRenderers)
                {
                    if(renderer==null||exterior.Count>=240||cockpits.Exists(binding=>ReferenceEquals(binding.renderer,renderer)))continue;
                    exterior.Add(new RendererBinding {renderer=renderer,part=renderer.GetComponentInParent<UnitPart>(true),verifiedStandin=VerifiedStandin(renderer)});
                }
            if(detailed)
                foreach(var cockpit in aircraft.GetComponentsInChildren<Cockpit>(true))
                    if(cockpit!=null&&cockpit.aircraft==aircraft)
                        cockpitControls.TryApply(cockpit,item=>item.enabled,(item,original)=>item.enabled=true);
            if((seated||eyeView)&&aircraft.pilots!=null)
                foreach(var pilot in aircraft.pilots)
                {
                    if(pilot==null||pilot.skinnedMeshRenderer==null||pilots.Count>=16)continue;
                    pilots.Add(new RendererBinding {renderer=pilot.skinnedMeshRenderer,part=pilot.GetUnitPart(),pilot=pilot});
                }
            cameraManager=manager;main=currentMain;nativeOverlay=currentOverlay;
            overlay=detailed&&nativeOverlay!=main?nativeOverlay:null;
            if(overlay!=null)originalOverlayEnabled=overlay.enabled;
            rebound=true;
            UnityEngine.Camera.onPreCull+=BeforeCameraRender;
            RenderPipelineManager.beginCameraRendering+=BeforePipelineCameraRender;subscribed=true;
        }
        requestedEyeView=cockpitEyeView;
        showFlight=flightInstruments;
        Apply();
        if(rebound&&detailed)foreach(var actor in playback.Actors())ActorSpawned(actor.Value);
    }
    private static bool ValidActor(Player playback,uint id,Aircraft aircraft)
        =>ReplayRuntime.IsPlayback&&!GameManager.IsHeadless&&!SceneHelper.IsTransitionPending&&playback!=null&&
          ReferenceEquals(ReplayManager.i.Playback,playback)&&playback.enabled&&!playback.IsSeeking&&
          aircraft!=null&&ReferenceEquals(playback.FindActor(id),aircraft)&&playback.CurrentActorGeneration(id)>0&&ReplayVfxPlayback.IsOwnedActor(aircraft);
    private static bool ValidBinding()
    {
        var current=SceneSingleton<CameraStateManager>.i;
        return ValidActor(owner,actorId,target)&&owner.CurrentActorGeneration(actorId)==generation&&current!=null&&main!=null&&
            ReferenceEquals(current,cameraManager)&&ReferenceEquals(current.mainCamera,main)&&ReferenceEquals(current.cockpitCamRender,nativeOverlay);
    }
    private static bool NativeEyeView(CameraStateManager manager,Aircraft aircraft)
        =>manager!=null&&aircraft!=null&&manager.currentState==manager.cockpitState&&ReferenceEquals(manager.followingUnit,aircraft);
    private static bool PartPresent(UnitPart part)
        =>part==null||(part.gameObject.activeInHierarchy&&!part.IsDetached()&&!part.fragmented&&part.parentUnit==target);
    private static bool CockpitPresent()=>target!=null&&target.cockpit!=null&&target.cockpit.gameObject.activeInHierarchy&&PartPresent(target.cockpit);
    private static bool Eligible(RendererBinding binding)
    {
        if(binding.renderer==null||!binding.renderer.gameObject.activeInHierarchy||!PartPresent(binding.part))return false;
        if(binding.pilot!=null)
            return target!=null&&!target.HasEjected()&&!binding.pilot.ejected&&!binding.pilot.dead&&binding.pilot.gameObject.activeInHierarchy&&ReferenceEquals(binding.pilot.aircraft,target);
        return CockpitPresent();
    }
    private static bool VerifiedStandin(Renderer renderer)
    {
        // Native prefab audits distinguish opaque simplified cockpit inserts
        // from exterior glass. Unknown modded exterior/body meshes stay visible.
        string name=renderer.gameObject.name;
        if(!name.EndsWith("_simple",StringComparison.Ordinal)||renderer.GetComponent<MeshFilter>()?.sharedMesh==null)return false;
        foreach(var material in renderer.sharedMaterials)
            if(material==null||material.name.Replace(" (Instance)","")!="Matte_black")return false;
        string counterpart=name.Substring(0,name.Length-7);
        var part=renderer.GetComponentInParent<UnitPart>(true);var bounds=renderer.bounds;
        foreach(var binding in cockpits)
        {
            var detail=binding.renderer;
            if(detail==null||detail.gameObject.name!=counterpart||!ReferenceEquals(binding.part,part)||detail.GetComponent<MeshFilter>()?.sharedMesh==null)continue;
            var detailedBounds=detail.bounds;
            if(detailedBounds.Contains(bounds.center)&&bounds.size.x<=detailedBounds.size.x*1.5f&&bounds.size.y<=detailedBounds.size.y*1.5f&&bounds.size.z<=detailedBounds.size.z*1.5f)return true;
        }
        return false;
    }
    public static void ActorSpawned(Unit unit)
    {
        if(!detailed||!(unit is Aircraft aircraft)||ReferenceEquals(aircraft,target)||!ValidBinding()||!owner.OwnsReplayAircraft(aircraft)||aircraft.cockpitRenderers==null)return;
        uint id=0;bool found=false;
        foreach(var actor in owner.Actors())if(ReferenceEquals(actor.Value,aircraft)){id=actor.Key;found=true;break;}
        if(!found)return;
        foreach(var renderer in aircraft.cockpitRenderers)
        {
            if(renderer==null||otherCockpits.ContainsKey(renderer)||otherCockpits.Count>=7600)continue;
            if((cameraMask.AddedLayers&(1<<renderer.gameObject.layer))==0)continue;
            otherCockpits.Add(renderer,new RendererBinding {renderer=renderer,aircraft=aircraft,part=renderer.GetComponentInParent<UnitPart>(true),actorId=id,actorGeneration=owner.CurrentActorGeneration(id)});
            visibility.Apply(renderer,false,item=>item.enabled,(item,value)=>item.enabled=value);
        }
    }
    private static bool OtherEligible(RendererBinding binding)
    {
        var aircraft=binding.aircraft;var part=binding.part;
        return owner!=null&&binding.actorGeneration>0&&owner.CurrentActorGeneration(binding.actorId)==binding.actorGeneration&&ReferenceEquals(owner.FindActor(binding.actorId),aircraft)&&
            aircraft!=null&&!aircraft.HasEjected()&&binding.renderer!=null&&binding.renderer.gameObject.activeInHierarchy&&
            (part==null||part.gameObject.activeInHierarchy&&!part.IsDetached()&&!part.fragmented&&part.parentUnit==aircraft)&&
            aircraft.cockpit!=null&&aircraft.cockpit.gameObject.activeInHierarchy&&!aircraft.cockpit.IsDetached()&&!aircraft.cockpit.fragmented;
    }
    private static bool RestoreEligible(Renderer renderer)
    {
        foreach(var binding in cockpits)if(ReferenceEquals(binding.renderer,renderer))return Eligible(binding);
        foreach(var binding in exterior)if(ReferenceEquals(binding.renderer,renderer))return Eligible(binding);
        foreach(var binding in pilots)if(ReferenceEquals(binding.renderer,renderer))return Eligible(binding);
        if(otherCockpits.TryGetValue(renderer,out var other))return OtherEligible(other);
        return false;
    }
    private static void Apply()
    {
        if(applying)return;
        if(!ValidBinding()){Clear();return;}
        applying=true;
        try
        {
            bool showCockpit=false;
            int cockpitLayers=0;
            foreach(var binding in cockpits)
            {
                if(binding.renderer==null)continue;
                bool show=Eligible(binding);showCockpit|=show;
                if(show)cockpitLayers|=1<<binding.renderer.gameObject.layer;
                visibility.Apply(binding.renderer,show,renderer=>renderer.enabled,(renderer,value)=>renderer.enabled=value);
            }
            foreach(var binding in pilots)
                // The native cockpit camera hides the seats so the eye camera
                // cannot end up inside a pilot head. Free camera pilot shots
                // can deliberately enable the existing models instead.
                if(binding.renderer!=null)visibility.Apply(binding.renderer,seated&&!eyeView&&Eligible(binding),renderer=>renderer.enabled,(renderer,value)=>renderer.enabled=value);
            // Native high-detail and low-detail cockpit renderer arrays are
            // complementary. Keeping both creates intersecting panels/glass.
            foreach(var binding in exterior)
                if(binding.renderer!=null)
                {
                    if(showCockpit&&(eyeView||binding.verifiedStandin))visibility.Apply(binding.renderer,false,renderer=>renderer.enabled,(renderer,value)=>renderer.enabled=value);
                    else visibility.Release(binding.renderer,Eligible(binding),(renderer,value)=>renderer.enabled=value);
                }
            if(overlay!=null)
            {
                overlay.enabled=false;
            }
            // Every studio shot uses normal world depth/projection, including
            // pilot-eye views; the native 5 m depth-clearing overlay clips parts
            // and draws other cockpit geometry over the world.
            if(main!=null)main.cullingMask=cameraMask.Apply(main.cullingMask,showCockpit?cockpitLayers:0);
            foreach(var binding in otherCockpits.Values)
                if(binding.renderer!=null)
                {
                    // Spawn callbacks can precede publishing the new frame's
                    // virtual time. Bind its generation once that frame lands.
                    if(binding.actorGeneration==0&&ReferenceEquals(owner.FindActor(binding.actorId),binding.aircraft))binding.actorGeneration=owner.CurrentActorGeneration(binding.actorId);
                    if((cameraMask.AddedLayers&(1<<binding.renderer.gameObject.layer))!=0)visibility.Apply(binding.renderer,false,renderer=>renderer.enabled,(renderer,value)=>renderer.enabled=value);
                    else visibility.Release(binding.renderer,OtherEligible(binding),(renderer,value)=>renderer.enabled=value);
                }
            if(showCockpit&&showFlight)flightScreens.Update(owner,target,owner.currentVirtualTime);
            else flightScreens.Clear();
        }
        catch(Exception error){Clear();Plugin.SafeLogWarning("Replay cockpit/pilot rendering stopped: "+error.Message);}
        finally{applying=false;}
    }
    private static void BeforeCameraRender(UnityEngine.Camera camera)
    {if(camera==main)Apply();}
    private static void BeforePipelineCameraRender(ScriptableRenderContext context,UnityEngine.Camera camera)
    {if(camera==main||camera==overlay)Apply();}
    public static void NativeCockpitVisibilityChanged(Aircraft aircraft,bool enabled)
    {
        if(!ReferenceEquals(aircraft,target))
        {
            if(!detailed||!ValidBinding()||!owner.OwnsReplayAircraft(aircraft))return;
            ActorSpawned(aircraft);
            if(aircraft.cockpitRenderers!=null)foreach(var renderer in aircraft.cockpitRenderers)visibility.ObserveNative(renderer,enabled);
            Apply();return;
        }
        foreach(var binding in cockpits)visibility.ObserveNative(binding.renderer,enabled);
        foreach(var binding in exterior)visibility.ObserveNative(binding.renderer,!enabled);
        Apply();
    }
    public static void NativePilotVisibilityChanged(Pilot pilot,bool enabled)
    {
        if(pilot==null||!ReferenceEquals(pilot.aircraft,target))return;
        visibility.ObserveNative(pilot.skinnedMeshRenderer,enabled);Apply();
    }
    public static void NativeCameraModeChanged(CameraStateManager manager)
    {
        if(manager==null||!ReferenceEquals(manager,cameraManager))return;
        // Native Enter/LeaveState may run while a studio override is active.
        // Releasing the override must respect the newly selected native mode.
        if(overlay!=null&&manager.cockpitCamRender==overlay)
        {
            bool nativeCockpit=manager.currentState==manager.cockpitState;
            originalOverlayEnabled=nativeCockpit;
        }
        eyeView=requestedEyeView||NativeEyeView(manager,target);Apply();
    }
    public static bool TryGetCockpitEye(Player playback,uint id,Aircraft aircraft,out Vector3 position,out Quaternion rotation,int seat=0)
    {
        position=Vector3.zero;rotation=Quaternion.identity;
        if(!ValidActor(playback,id,aircraft)||aircraft.cockpit==null||!aircraft.cockpit.gameObject.activeInHierarchy||
            aircraft.cockpit.IsDetached()||aircraft.cockpit.fragmented||aircraft.cockpitViewPoint==null||!aircraft.cockpitViewPoint.gameObject.activeInHierarchy)return false;
        return ReplayCockpitSeats.TryEye(aircraft,seat,out position,out rotation);
    }
    private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    public static void Clear()
    {
        ReplayCockpitSeats.Clear();
        if(subscribed)
        {
            UnityEngine.Camera.onPreCull-=BeforeCameraRender;
            RenderPipelineManager.beginCameraRendering-=BeforePipelineCameraRender;subscribed=false;
        }
        int failures=visibility.Restore(renderer=>renderer!=null,RestoreEligible,(renderer,value)=>renderer.enabled=value);
        flightScreens.Clear();
        failures+=cockpitControls.Restore(cockpit=>cockpit!=null,(cockpit,original)=>cockpit.enabled=original);
        try
        {
            if(main!=null)main.cullingMask=cameraMask.Restore(main.cullingMask);
            else cameraMask.Restore(0);
            if(overlay!=null)
            {
                overlay.enabled=originalOverlayEnabled;
            }
        }
        catch(Exception){failures++;}
        finally{cameraMask.Restore(0);cockpits.Clear();exterior.Clear();pilots.Clear();otherCockpits.Clear();owner=null;target=null;cameraManager=null;main=null;overlay=null;nativeOverlay=null;actorId=0;generation=0;detailed=false;seated=false;eyeView=false;requestedEyeView=false;showFlight=false;}
        if(failures>0)Plugin.SafeLogWarning("Replay cockpit/pilot restore failures: "+failures);
    }
}


