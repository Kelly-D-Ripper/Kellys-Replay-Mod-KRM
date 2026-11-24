using System.Collections.Generic;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Director;

public partial class ReplayDirector
{
    private Player ejectionPlayer;
    private uint ejectionAircraft;
    private double ejectionBirth=double.NaN;
    private int ejectionSeek=-1;
    private Unit ejectionCarrier;
    private readonly Dictionary<Renderer,bool> ejectionHidden=new Dictionary<Renderer,bool>();
    private sealed class EjectionInterior {public bool enabled;public UnitPart part;}
    private readonly Dictionary<Renderer,EjectionInterior> ejectionInteriors=new Dictionary<Renderer,EjectionInterior>();
    private readonly Dictionary<GameObject,int> ejectionLayers=new Dictionary<GameObject,int>();
    public bool EjectionCameraActive {get;private set;}
    public void SetFirstPersonEjection(bool value)
    {Project.firstPersonEjection=value;if(!value)RestoreEjectionCamera();Status=value?"Cockpit view follows recorded pilot eyes on ejection. Requires new ejection-eye data.":"First-person ejection disabled.";}
    private bool TryApplyEjectionCamera(CameraStateManager manager)
    {
        var player=ReplayManager.i?.Playback;
        if(!ReplayRuntime.IsPlayback||player==null||!player.enabled||player.IsSeeking||Project.rig!=5||Project.cockpitSeat!=0||CameraKeysEnabled||RailActive||!Project.firstPersonEjection)
        {RestoreEjectionCamera();return false;}
        if(!ReferenceEquals(ejectionPlayer,player)||ejectionAircraft!=Project.target||ejectionSeek!=player.CompletedSeeks)
        {RestoreEjectionCamera();ejectionPlayer=player;ejectionAircraft=Project.target;ejectionBirth=double.NaN;ejectionSeek=player.CompletedSeeks;}
        if(double.IsNaN(ejectionBirth))
        {
            if(ResolveCameraActor(Project.target) is Aircraft&&player.TryActorBirth(Project.target,out double currentBirth))ejectionBirth=currentBirth;
            else if(player.TryEjectionSourceBirth(Project.target,out double recordedBirth))ejectionBirth=recordedBirth;
        }
        if(double.IsNaN(ejectionBirth)||!player.TryEjectionEye(Project.target,ejectionBirth,out var carrier,out var pose))
        {RestoreEjectionCamera();return false;}
        // Release the regular cockpit overlay before taking restoration baselines.
        // Otherwise its old visibility scope can overwrite the capsule on entry.
        ReplayInteriorPresentation.Clear();
        EnsureDrivenCamera(manager);
        if(!ReferenceEquals(ejectionCarrier,carrier))
        {
            RestoreEjectionCamera();ejectionCarrier=carrier;
            if(carrier is PilotDismounted)
            {
                foreach(var renderer in carrier.GetComponentsInChildren<Renderer>(true))
                    if(renderer!=null&&ejectionHidden.Count<128)ejectionHidden[renderer]=renderer.forceRenderingOff;
            }
            else if(carrier is Aircraft aircraft&&aircraft.cockpitRenderers!=null)
            {
                foreach(var renderer in aircraft.cockpitRenderers)
                    if(renderer!=null&&renderer.gameObject.activeInHierarchy&&aircraft.cockpit!=null&&renderer.transform.IsChildOf(aircraft.cockpit.transform)&&ejectionInteriors.Count<240)
                    {
                        var part=renderer.GetComponentInParent<UnitPart>(true);if(part!=null&&part.fragmented)continue;
                        ejectionInteriors[renderer]=new EjectionInterior {enabled=renderer.enabled,part=part};if(!ejectionLayers.ContainsKey(renderer.gameObject))ejectionLayers[renderer.gameObject]=renderer.gameObject.layer;
                    }
                if(aircraft.pilots!=null&&aircraft.pilots.Length>0&&aircraft.pilots[0]!=null&&aircraft.pilots[0].skinnedMeshRenderer!=null)
                    ejectionHidden[aircraft.pilots[0].skinnedMeshRenderer]=aircraft.pilots[0].skinnedMeshRenderer.forceRenderingOff;
            }
        }
        foreach(var pair in ejectionHidden)if(pair.Key!=null)pair.Key.forceRenderingOff=true;
        foreach(var pair in ejectionInteriors)if(pair.Key!=null)pair.Key.enabled=EjectionInteriorPresent(pair.Key,pair.Value.part);
        foreach(var pair in ejectionLayers)if(pair.Key!=null)pair.Key.layer=0;
        manager.transform.SetPositionAndRotation(pose.position.ToLocalPosition(),pose.rotation);manager.CheckOriginShift();manager.allowInputs=false;ApplyLens(manager,Project.fov);
        EjectionCameraActive=true;return true;
    }
    private bool EjectionInteriorPresent(Renderer renderer,UnitPart part)
    {
        var aircraft=ejectionCarrier as Aircraft;
        return renderer!=null&&renderer.gameObject.activeInHierarchy&&aircraft!=null&&aircraft.cockpit!=null&&
            aircraft.cockpit.gameObject.activeInHierarchy&&!aircraft.cockpit.fragmented&&renderer.transform.IsChildOf(aircraft.cockpit.transform)&&
            // Native descendants inherit detached-from-aircraft when the intact
            // capsule separates. Only a break from their own parent loses detail.
            (part==null||!part.fragmented&&(ReferenceEquals(part,aircraft.cockpit)||part.attachInfo==null||!part.attachInfo.detachedFromParentPart));
    }
    private void RestoreEjectionCamera()
    {
        foreach(var pair in ejectionHidden)if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;
        foreach(var pair in ejectionInteriors)if(pair.Key!=null)pair.Key.enabled=pair.Value.enabled&&EjectionInteriorPresent(pair.Key,pair.Value.part);
        foreach(var pair in ejectionLayers)if(pair.Key!=null)pair.Key.layer=pair.Value;
        ejectionHidden.Clear();ejectionInteriors.Clear();ejectionLayers.Clear();ejectionCarrier=null;EjectionCameraActive=false;
    }
}

