using ReplayMod.Core;

namespace ReplayMod.Director;

public partial class ReplayDirector
{
    private bool ApplyMotionSmoothing()
    {
        var playback=ReplayManager.i?.Playback;if(playback==null)return true;
        bool densityLimited=playback.MotionSmoothingDensityLimited;
        if(densityLimited)
        {
            Project.motionSmoothing=0;
            Status="Motion smoothing turned off: this recording is too dense for its preview window. Original movement restored.";
        }
        playback.SetMotionSmoothing(Project.motionSmoothing,Project.target,Project.motionSmoothingScope==0);
        return !densityLimited;
    }
    public void SetMotionSmoothing(int preset)
    {
        if(!ReplayRuntime.IsPlayback||IsBusy||preset<0||preset>3)return;
        Project.motionSmoothing=preset;if(!ApplyMotionSmoothing())return;ResetCameraSmoothing();
        Status=preset==0?"Aircraft motion: original recorded movement.":"Aircraft motion smoothing enabled. Save project to keep this setting.";
    }
    public void SetMotionSmoothingScope(int scope)
    {
        if(!ReplayRuntime.IsPlayback||IsBusy||scope<0||scope>1)return;
        Project.motionSmoothingScope=scope;ApplyMotionSmoothing();ResetCameraSmoothing();
    }
}
