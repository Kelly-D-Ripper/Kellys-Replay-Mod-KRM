using System;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Director;

public partial class ReplayDirector
{
    private Unit lastPairActor;
    public bool SelectSecondaryCameraTarget(uint id)
    {
        if(!ReplayRuntime.IsPlayback||IsBusy)return false;
        var actor=ReplayManager.i.Playback.FindActor(id);
        if(!(actor is Aircraft)||id==Project.target){Status="Choose a different aircraft for the second subject.";return false;}
        Project.secondTarget=id;cameraTargets.Select(id,actor);ResetCameraSmoothing();
        Status="Second aircraft selected.";return true;
    }
    public void CaptureFollowAngle()
    {
        if(!ReplayRuntime.IsPlayback||IsBusy)return;
        var actor=ResolveCameraActor(Project.target);var camera=SceneSingleton<CameraStateManager>.i;
        if(!(actor is Aircraft)||camera?.mainCamera==null){Status="Select an aircraft first.";return;}
        var offset=camera.transform.position-actor.transform.position;
        if(Project.followWithAircraft)offset=actor.transform.InverseTransformDirection(offset);
        if(offset.magnitude<1||offset.magnitude>1000){Status="Move between 1 and 1000 metres from the aircraft before locking this angle.";return;}
        Project.distance=offset.magnitude;
        Project.followAzimuth=Mathf.Atan2(offset.x,offset.z)*Mathf.Rad2Deg;
        Project.followElevation=Mathf.Clamp(Mathf.Asin(Mathf.Clamp(offset.y/offset.magnitude,-1,1))*Mathf.Rad2Deg,-89,89);
        SelectCameraRig(7);Status="Fixed-angle follow locked to this viewing angle.";
    }
    private static Vector3 ViewOffset(float azimuth,float elevation)
    {
        float a=azimuth*Mathf.Deg2Rad,e=elevation*Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(a)*Mathf.Cos(e),Mathf.Sin(e),Mathf.Cos(a)*Mathf.Cos(e));
    }
    private bool TryFramingCamera(CameraStateManager camera,Unit primary,double time,out Vector3 position,out Quaternion rotation)
    {
        position=default;rotation=Quaternion.identity;
        Vector3 centre=primary.transform.position;
        Vector3 direction=ViewOffset(Project.followAzimuth,Project.followElevation);
        float distance=Project.distance;
        bool pair=Project.rig==6;
        Unit secondary=null;
        if(pair)
        {
            secondary=ResolveCameraActor(Project.secondTarget);
            if(!(secondary is Aircraft)||ReferenceEquals(primary,secondary))
            {Status="Two-Aircraft Framing needs two different aircraft present at this replay time.";return false;}
            centre=(primary.transform.position+secondary.transform.position)*.5f;
            if(!TwoAircraftFraming.TryDistance(Vector3.Distance(primary.transform.position,secondary.transform.position),
                Math.Max(1,primary.maxRadius),Math.Max(1,secondary.maxRadius),Project.fov,camera.mainCamera.aspect,
                Project.distance,camera.mainCamera.farClipPlane,out double fitted,out _))
            {Status="The aircraft are too far apart for the current camera render distance. Choose a closer moment or a wider FOV.";return false;}
            distance=(float)fitted;
        }
        else if(Project.followWithAircraft)direction=primary.transform.TransformDirection(direction);
        bool reset=double.IsNaN(lastCameraTime)||time<lastCameraTime||time-lastCameraTime>.5||lastRig!=Project.rig||
            !ReferenceEquals(lastTrackingActor,primary)||!ReferenceEquals(lastPairActor,secondary);
        // Ease the viewing direction, not the midpoint. Recompute the required
        // distance every frame so an expanding formation cannot outrun framing.
        if(!relativeTracking.Update(direction.x,direction.y,direction.z,time,Project.smoothing,reset))return false;
        var eased=new Vector3((float)relativeTracking.X,(float)relativeTracking.Y,(float)relativeTracking.Z);
        if(eased.sqrMagnitude<.0001f)eased=direction;
        position=centre+eased.normalized*distance;
        rotation=Quaternion.LookRotation(centre-position,!pair&&Project.followWithAircraft?primary.transform.up:Vector3.up);
        lastPairActor=secondary;
        return true;
    }
}

