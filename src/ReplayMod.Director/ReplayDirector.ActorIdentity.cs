using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Director;

public partial class ReplayDirector
{
    public bool ShowPlayerNames=>Project.showPlayerNames;
    public void SetShowPlayerNames(bool show)=>Project.showPlayerNames=show;
    public bool TryGetCaptionActor(out uint id)
    {
        id=0;var player=ReplayManager.i?.Playback;
        if(player==null||ReplayMod.Misc.SceneHelper.IsTransitionPending||!ActorCaptionPolicy.Ready(ReplayRuntime.IsPlayback,Clean,player.IsSeeking,player.IsBuffering,
            player.IsReloading,ShowPlayerNames,holdingSubject))return false;
        var camera=SceneSingleton<CameraStateManager>.i;if(camera==null||camera.mainCamera==null)return false;
        Unit subject;
        if(CameraKeysEnabled)
        {
            if(!cameraOwned||!ActorCaptionPolicy.TryAttachedTarget(Project.cameras,player.currentVirtualTime,out id))return false;
            subject=ResolveCameraActor(id);
        }
        else if(Project.rig>0)
        {if(!cameraOwned)return false;id=Project.target;subject=ResolveCameraActor(id);}
        else
        {
            // A selected aircraft can remain in followingUnit during a native
            // Free view. Selection alone is never a spectating caption.
            if(cameraOwned||RailActive||camera.currentState==null||camera.currentState is CameraFreeState)return false;
            subject=camera.followingUnit;
            if(!player.TryRecordedId(subject,out id))return false;
        }
        return subject is Aircraft&&player.TryRecordedId(subject,out var actual)&&actual==id;
    }
}
