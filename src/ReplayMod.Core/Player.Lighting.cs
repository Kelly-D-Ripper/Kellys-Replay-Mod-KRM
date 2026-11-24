namespace ReplayMod.Core;

public partial class Player
{
    public void RefreshPlaybackLights(Aircraft aircraft=null)
    {
        if(!ReplayRuntime.IsPlayback||_faulted||_unitCotroller==null)return;
        if(aircraft==null)_unitCotroller.RefreshReplayLights();
        else _unitCotroller.RefreshReplayLightsFor(aircraft);
    }
}
