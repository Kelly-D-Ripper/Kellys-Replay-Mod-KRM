using System;
using ReplayMod.Camera;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Director;

public partial class ReplayDirector
{
    private readonly CameraEventShot eventShot=new CameraEventShot();
    public bool AutoFrameSubject=>Project.autoFrameSubject;
    public void SetAutoFrameSubject(bool enabled)
    {
        Project.autoFrameSubject=enabled;ResetCameraSmoothing();
        Status=enabled?"Tracking lens frames the selected subject. A manual FOV change turns this off.":"Tracking uses your manual FOV.";
    }
    public void CancelEventShot()=>eventShot.Cancel();
    public async void JumpAndFollow(Bookmark bookmark,int rig=1,double leadInSeconds=5)
    {
        var player=ReplayManager.i?.Playback;
        if(!ReplayRuntime.IsPlayback||player==null||!player.enabled||bookmark==null)return;
        if(!player.TryGetActorLifetime(bookmark.actor,bookmark.actorLifetime,bookmark.time,out var lifetime)||
            !CameraEventShotPlan.TryCreate(lifetime,bookmark.time,leadInSeconds,player.replayDuration,rig,out var plan))
        {Status="This marker has no unambiguous recorded subject. Use Jump to view its time.";return;}
        CancelCameraTake();CancelEventShot();StopRail();CameraManager.i?.StopCamFlight();
        ReleaseCamera();Project.rig=0;CameraKeysEnabled=false;ResetCameraSmoothing();
        long request=eventShot.Begin(plan);player.Pause();
        Status="Preparing event shot with "+(bookmark.time-plan.StartTime).ToString("0.#")+" seconds of lead-in…";
        try
        {
            await player.TimelineJump(plan.StartTime,Project.preRoll,false);
            if(!eventShot.IsCurrent(request))return;
            if(!ReferenceEquals(player,ReplayManager.i?.Playback)||!player.enabled||player.IsSeeking||Math.Abs(player.currentVirtualTime-plan.StartTime)>.001)
            {CancelEventShot();return;}
            if(!eventShot.Prepared(request))return;
            UpdateEventShot();player.Resume();
            if(eventShot.Active)Status=eventShot.Following?"Event shot following its recorded subject.":"Playing the lead-in. Camera follows when this subject appears.";
        }
        catch(Exception error)
        {
            if(eventShot.IsCurrent(request)){CancelEventShot();Status="Event shot failed: "+error.Message;Plugin.logger.LogError(error);}
        }
    }
    private void UpdateEventShot()
    {
        if(!eventShot.Active||eventShot.Preparing||eventShot.Following)return;
        var player=ReplayManager.i?.Playback;
        if(player==null||!player.enabled){CancelEventShot();return;}
        if(player.IsSeeking){CancelEventShot();return;}
        var plan=eventShot.Plan;
        var actor=player.FindActor(plan.Lifetime.Actor);
        long generation=actor!=null?player.CurrentActorGeneration(plan.Lifetime.Actor):0;
        switch(eventShot.Evaluate(player.currentVirtualTime,generation))
        {
            case CameraEventSubjectAction.Follow:
                if(!RigAccepts(plan.Rig,actor))
                {CancelEventShot();Status=plan.Rig==3?"This event's subject is not an aircraft. Choose Chase or Orbit.":"This event's subject is not a missile. Choose Chase or Orbit.";return;}
                cameraTargets.Select(plan.Lifetime.Actor,actor);Project.target=plan.Lifetime.Actor;Project.lightTarget=plan.Lifetime.Actor;
                Project.rig=plan.Rig;CameraKeysEnabled=false;ResetCameraSmoothing();
                UpdateCamera(player.currentVirtualTime);Status="Event shot following its recorded subject.";break;
            case CameraEventSubjectAction.Unavailable:
                Status="That event's subject has no live camera frame here. Playback continues in Free camera.";break;
        }
    }
    private void HandleMissingCameraSubject(CameraStateManager manager,double time)
    {
        bool sameShot=cameraOwned&&ownedCamera==manager.mainCamera&&lastRig==Project.rig&&lastTarget==Project.target;
        // A native view/target change takes priority over a destruction hold.
        // Leave its selected camera state and lens intact instead of switching
        // back to the studio's free state while relinquishing ownership.
        if(sameShot&&(manager.currentState!=manager.freeState||manager.followingUnit!=null))
        {
            CancelEventShot();cameraOwned=false;ownedCamera=null;Project.rig=0;CameraKeysEnabled=false;
            manager.allowInputs=originalInputs;ResetCameraSmoothing();
            Status="Camera returned to the game's selected view.";return;
        }
        var loss=sameShot?subjectHold.Missing(time,2):CameraSubjectLoss.NoPose;
        if(loss==CameraSubjectLoss.Hold)
        {
            var origin=Datum.originPosition;
            manager.transform.SetPositionAndRotation(new Vector3((float)(heldCameraX+origin.x),(float)(heldCameraY+origin.y),(float)(heldCameraZ+origin.z)),heldCameraRotation);
            manager.cameraVelocity=Vector3.zero;manager.allowInputs=false;manager.CheckOriginShift();ApplyLens(manager,heldCameraFov);
            if(!holdingSubject)Status="Subject ended. Holding the last camera view for two replay seconds.";
            holdingSubject=true;return;
        }
        if(loss==CameraSubjectLoss.Release)
        {
            EnterFreeCamera();Status="Subject ended. Free camera keeps the final view; select another subject when ready.";return;
        }
        ReleaseCamera();
        Status=Project.rig==5?"The selected aircraft's attached cockpit viewpoint is unavailable. Choose another aircraft or Free camera.":Project.rig==3?"Wingtip needs a live aircraft. Select one on the map or in Camera targets.":Project.rig==4?
            "Missile follow needs a live missile.":"The camera target is absent or its recorded lifetime ended. Select another target.";
    }
}
