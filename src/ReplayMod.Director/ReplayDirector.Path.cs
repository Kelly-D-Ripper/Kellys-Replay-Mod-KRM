using System;
using System.Linq;
using NuclearOption.SavedMission;
using ReplayMod.Core;
using ReplayMod.Camera;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Director;

public partial class ReplayDirector
{
    private readonly CameraTakeClock take=new CameraTakeClock();
    private Player takePlayer;
    private bool takeOriginalClean;
    public bool TakeActive=>take.Active;
    public bool CameraTakeHoldsPlayback=>take.HoldsPlayback&&ReferenceEquals(takePlayer,ReplayManager.i?.Playback);
    public int PathPointCount=>Project.cameras.Count;
    public bool PathAttached=>PathPointCount>0?Project.cameras[0].relative:Project.pathAttached;
    public bool PathSmooth=>Project.smoothCameraPath;
    public float PathSmoothness=>Project.cameraPathSmoothness;
    public bool StopAtPathEnd=>Project.stopAtCameraPathEnd;
    public float PathEndHoldSeconds=>Project.cameraPathEndHold;
    public bool ShowCameraPath=>Project.showCameraPath;
    public bool PathAnimatesRotation=>Project.animatePathRotation;
    public bool PathAnimatesFov=>Project.animatePathFov;
    public void SetShowCameraPath(bool show)=>Project.showCameraPath=show;
    public void SetPathAnimatesRotation(bool animate){CancelCameraTake();Project.animatePathRotation=animate;}
    public void SetPathAnimatesFov(bool animate){CancelCameraTake();Project.animatePathFov=animate;}
    public bool SampleCameraPath(double time,out CameraPathSample sample)=>CameraPathSampler.TrySample(Project.cameras,time,PathSmooth,PathSmoothness,Resolve,out sample);
    public string TakeStatus=>!TakeActive?PathPointCount+" path points":take.Phase==CameraTakePhase.Countdown?
        "Starting in "+Math.Ceiling(take.Remaining(Time.realtimeSinceStartup))+" seconds":take.Phase==CameraTakePhase.Preparing?
        "Preparing the first path frame":take.Phase==CameraTakePhase.EndHold?"Holding the final frame":"Camera take running";

    public bool SetPathAttached(bool attached)
    {
        if(PathPointCount>0&&PathAttached!=attached){Status="This path already contains points. Clear it before changing attachment mode; saved points are kept.";return false;}
        CancelCameraTake();Project.pathAttached=attached;Status=attached?"Attached path: select a unit, then position the camera and add points.":"Detached path: points stay in the world.";return true;
    }
    public void SetPathSmooth(bool smooth){CancelCameraTake();Project.smoothCameraPath=smooth;ResetCameraSmoothing();}
    public void SetPathSmoothness(float amount)
    {
        if(!DirectorProject.Finite(amount))return;
        CancelCameraTake();Project.cameraPathSmoothness=Mathf.Clamp(amount,0,1);ResetCameraSmoothing();
    }
    public void SetStopAtPathEnd(bool stop){CancelCameraTake();Project.stopAtCameraPathEnd=stop;}
    public void SetPathEndHoldSeconds(float seconds)
    {if(!DirectorProject.Finite(seconds))return;CancelCameraTake();Project.cameraPathEndHold=Mathf.Clamp(seconds,0,10);}
    public bool CapturePathPoint()
    {
        if(!ReplayRuntime.IsPlayback||IsBusy||TakeActive)return false;
        var manager=SceneSingleton<CameraStateManager>.i;
        if(manager?.mainCamera==null)return false;
        bool attached=PathAttached;uint target=attached&&PathPointCount>0?Project.cameras[0].target:Project.target;
        if(Project.cameras.Any(k=>k.relative!=attached||(attached&&k.target!=target)))
        {Status="This saved path mixes subjects or attachment modes. Keep using its advanced tools, or clear it to start a new path.";return false;}
        var actor=attached?ResolveCameraActor(target):null;
        if(attached&&actor==null){Status="Select a live unit before adding an attached path point.";return false;}
        double time=ReplayManager.i.GetCurrentVirtualTime();
        double duration=ReplayManager.i.GetDuration();
        if(!DirectorProject.TimeValid(time,duration))
        {Status="Seek to a valid replay time before adding a path point.";return false;}
        manager.GetCameraPosition(out var pose);var position=pose.Position.ToLocalPosition();var rotation=pose.Rotation;
        if(attached){position=actor.transform.InverseTransformPoint(position);rotation=Quaternion.Inverse(actor.transform.rotation)*rotation;}
        else {var global=position.ToGlobalPosition();position=new Vector3(global.x,global.y,global.z);}
        var point=new CameraKey {time=time,x=position.x,y=position.y,z=position.z,qx=rotation.x,qy=rotation.y,qz=rotation.z,qw=rotation.w,
            fov=CurrentCameraFov,easing=0,relative=attached,target=target};
        var existing=Project.cameras.FirstOrDefault(key=>key.time==time);
        if(existing!=null)point.segmentMode=existing.segmentMode;
        if(!DirectorProject.TryStoreCameraPoint(Project.cameras,point,duration,out int index,out bool replaced))
        {Status="Could not save this point. Check the path times and the 2000-point limit.";return false;}
        ResetCameraSmoothing();
        Status="Path point "+(index+1)+(replaced?" updated at ":" added at ")+time.ToString("0.000")+"s. Uses the current replay time.";return true;
    }
    public bool SetPathPointTime(int index,double time)
    {
        if(IsBusy||index<0||index>=PathPointCount||!DirectorProject.TimeValid(time,ReplayManager.i?.GetDuration()??0))return false;
        if((index>0&&time<=Project.cameras[index-1].time)||(index+1<PathPointCount&&time>=Project.cameras[index+1].time))
        {Status="Point times must increase from first to last.";return false;}
        CancelCameraTake();Project.cameras[index].time=time;ResetCameraSmoothing();Status="Point time saved.";return true;
    }
    public async void GoToPathPoint(CameraKey point)
    {
        if(!ReplayRuntime.IsPlayback||IsBusy||point==null||!Project.cameras.Contains(point))return;
        var player=ReplayManager.i?.Playback;if(player==null||!player.enabled)return;
        double time=point.time;
        CancelCameraTake();StopRail();player.Pause();Status="Opening camera point…";
        try
        {
            await player.TimelineJump(time,0,false);
            if(player!=ReplayManager.i?.Playback||!player.enabled||player.IsSeeking||Math.Abs(player.currentVirtualTime-time)>.001||!Project.cameras.Contains(point)||point.time!=time)return;
            if(!Resolve(point,out var position,out var rotation))
            {Status="This attached point's subject is unavailable at its saved time.";return;}
            var manager=SceneSingleton<CameraStateManager>.i;if(manager?.mainCamera==null)return;
            EnterFreeCamera();
            manager.SetCameraPosition(new PositionRotation {Position=position.ToGlobalPosition(),Rotation=rotation});
            manager.transform.SetPositionAndRotation(position,rotation);manager.cameraVelocity=Vector3.zero;manager.CheckOriginShift();
            OwnFreeCameraRoll(manager,CameraRollMath.Normalize(rotation.eulerAngles.z));
            Project.autoFrameSubject=false;RestoreCameraLens(point.fov);
            Status="Camera point restored. Move the camera and choose Add point here to update it.";
        }
        catch(Exception error){Status="Could not open camera point: "+error.Message;Plugin.logger.LogError(error);}
    }
    public bool RemovePathPoint(CameraKey point)
    {
        if(IsBusy||point==null||!Project.cameras.Contains(point))return false;
        CancelCameraTake();Project.cameras.Remove(point);
        if(PathPointCount<2&&CameraKeysEnabled)EnterFreeCamera();
        ResetCameraSmoothing();Status="Camera point deleted.";return true;
    }
    public void RemoveLastPathPoint()
    {CancelCameraTake();if(PathPointCount>0)Project.cameras.RemoveAt(PathPointCount-1);if(PathPointCount<2)CameraKeysEnabled=false;Status="Last path point removed.";}
    public void ClearCameraPath()
    {CancelCameraTake();CameraKeysEnabled=false;Project.cameras.Clear();ReleaseCamera();Status="Camera path cleared. Legacy spline points are kept separately.";}
    public bool PlayCameraPath()
    {
        if(IsBusy||PathPointCount<2){Status="Add at least two path points before playing a path.";return false;}
        StopRail();CameraManager.i?.StopCamFlight();Project.rig=0;CameraKeysEnabled=true;ResetRollOwnership();ResetCameraSmoothing();return true;
    }
    public async void PreviewCameraPath()
    {
        if(!ReplayRuntime.IsPlayback || IsBusy || PathPointCount<2) {Status="Add at least two path points before previewing.";return;}
        var player=ReplayManager.i.Playback;
        if(player==null || !player.enabled)return;
        double start=Project.cameras[0].time;
        try
        {
            player.Pause();
            await player.TimelineJump(start,0,false);
            if(player!=ReplayManager.i?.Playback || !player.enabled || player.IsSeeking || Math.Abs(player.currentVirtualTime-start)>.001) return;
            if(PlayCameraPath()) {UpdateCamera(start);player.Resume();Status="Preview playing. Choose Stop preview to move the camera.";}
        }
        catch(Exception error) {Status="Preview could not start: "+error.Message;Plugin.logger.LogError(error);}
    }
    public void StopCameraPathPreview()
    {
        if(!CameraKeysEnabled || TakeActive)return;
        EnterFreeCamera();Status="Preview stopped. You can move the camera again.";
    }
    public void SetPathSegmentMode(CameraKey point,int mode)
    {
        if(point==null || !Project.cameras.Contains(point) || mode<0 || mode>3 || IsBusy)return;
        CancelCameraTake();point.segmentMode=mode;ResetCameraSmoothing();
    }
    public async void StartCameraTake()
    {
        if(!ReplayRuntime.IsPlayback||IsBusy||PathPointCount<2||!Project.Validate(ReplayManager.i.GetDuration()))
        {Status="Add two valid path points before starting a camera take.";return;}
        CancelCameraTake();
        var player=ReplayManager.i.Playback;takePlayer=player;takeOriginalClean=Clean;
        long revision=take.Prepare();CameraKeysEnabled=false;StopRail();player.Pause();Status="Preparing camera take…";
        try
        {
            await player.TimelineJump(Project.cameras[0].time,Project.preRoll,false);
            if(!take.IsCurrent(revision)||player!=ReplayManager.i?.Playback||player?.enabled!=true)return;
            if(!PlayCameraPath())throw new InvalidOperationException(Status);
            player.PlaybackEndTime=StopAtPathEnd?Project.cameras[PathPointCount-1].time:(double?)null;
            UpdateCamera(player.currentVirtualTime);
            if(!take.Ready(revision,Time.realtimeSinceStartup,5))return;
            SetClean(true);Status="Camera take starts in five seconds. Studio key cancels.";
        }
        catch(Exception error)
        {if(take.IsCurrent(revision)){CancelCameraTake();Status="Camera take could not start: "+error.Message;Plugin.logger.LogError(error);}}
    }
    public void CancelCameraTake()
    {
        if(!take.Active)return;
        take.Cancel();CameraKeysEnabled=false;
        if(takePlayer!=null&&ReferenceEquals(takePlayer,ReplayManager.i?.Playback)){takePlayer.PlaybackEndTime=null;takePlayer.Pause();}
        takePlayer=null;SetClean(takeOriginalClean);EnterFreeCamera();Status="Camera take stopped. Tools restored.";
    }
    private void UpdateCameraTake()
    {
        if(!take.Active)return;
        var player=ReplayManager.i?.Playback;
        if(player==null||!ReferenceEquals(player,takePlayer)||!player.enabled||PathPointCount<2)
        {CancelCameraTake();return;}
        if(take.Phase==CameraTakePhase.Preparing)return;
        if(player.IsSeeking){CancelCameraTake();return;}
        if(PathAttached&&ResolveCameraActor(Project.cameras[0].target)==null){CancelCameraTake();Status="Camera take stopped: attached target is absent.";return;}
        if(!Clean){CancelCameraTake();return;}
        double end=Project.cameras[PathPointCount-1].time;
        switch(take.Tick(Time.realtimeSinceStartup,player.currentVirtualTime>=end,StopAtPathEnd,PathEndHoldSeconds))
        {
            case CameraTakeAction.StartPlayback:player.Resume();Status="Camera take running.";break;
            case CameraTakeAction.HoldEnd:
                player.Pause();UpdateCamera(end);break;
            case CameraTakeAction.Finish:
                player.PlaybackEndTime=null;takePlayer=null;SetClean(takeOriginalClean);EnterFreeCamera();Status="Camera take complete. Tools restored.";break;
        }
    }
}
