using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Director;

public partial class ReplayDirector
{
    public void SetReduceCloseClipping(bool value)
    {Project.reduceCloseClipping=value;UpdateCameraVisibility();Status=value?"Close-up clipping reduced. Outer hulls can still be invisible from inside.":"Normal camera clipping restored.";}
    public void SetDetailedCockpit(bool value)
    {Project.detailedCockpit=value;UpdateCameraVisibility();Status=value?"Detailed cockpit enabled for the selected aircraft, where its model is available.":"Normal cockpit rendering restored.";}
    public void SetSeatedPilots(bool value)
    {Project.seatedPilots=value;UpdateCameraVisibility();Status=value?"Existing seated pilots enabled for exterior shots. Cockpit-eye views hide them for a clear view.":"Normal pilot rendering restored.";}
    public void SetFlightInstruments(bool value)
    {Project.flightInstruments=value;UpdateCameraVisibility();Status=value?"Flight instruments use recorded flight data. Tactical and target-video history is unavailable.":"Replay flight instruments disabled.";}
    private void UpdateCameraVisibility()
    {
        var player=ReplayManager.i?.Playback;
        if(!ReplayRuntime.IsPlayback||player==null||!player.enabled||player.IsSeeking)
        {RestoreCameraVisibility();return;}
        var aircraft=ResolveCameraActor(Project.target) as Aircraft;
        if(EjectionCameraActive)
        {ReplayInteriorPresentation.Clear();ReplayCloseCameraVisibility.SetEnabled(Project.reduceCloseClipping);ReplayCloseCameraVisibility.Update(SceneSingleton<CameraStateManager>.i);return;}
        bool eyeView=Project.rig==5&&!CameraKeysEnabled&&!RailActive;
        if(Project.detailedCockpit)
        {
            var manager=SceneSingleton<CameraStateManager>.i;
            if(manager!=null&&ReplayInteriorPresentation.TryGetCockpitEye(player,Project.target,aircraft,out var eye,out _,Project.cockpitSeat)&&
                Vector3.Distance(manager.transform.position,eye)<=.15f)eyeView=true;
        }
        ReplayInteriorPresentation.Update(player,Project.target,aircraft,Project.detailedCockpit,Project.seatedPilots,eyeView,Project.flightInstruments);
        ReplayCloseCameraVisibility.SetEnabled(Project.reduceCloseClipping);
        ReplayCloseCameraVisibility.Update(SceneSingleton<CameraStateManager>.i);
    }
    private static void RestoreCameraVisibility()
    {ReplayDirector.i?.RestoreEjectionCamera();ReplayInteriorPresentation.Clear();ReplayCloseCameraVisibility.Clear();}
    public bool PlaceCameraInCockpit()
    {
        if(!ReplayRuntime.IsPlayback||IsBusy)return false;
        ConsumeNativeCameraTarget();var aircraft=ResolveCameraActor(Project.target) as Aircraft;
        var manager=SceneSingleton<CameraStateManager>.i;
        if(manager==null||aircraft==null||!ReplayInteriorPresentation.TryGetCockpitEye(ReplayManager.i.Playback,Project.target,aircraft,out var position,out var rotation,Project.cockpitSeat))
        {Status="Select a live aircraft with an attached cockpit viewpoint first.";return false;}
        float lens=CurrentCameraFov;
        EnterFreeCamera();ReplayManager.i.Playback.Pause();
        Project.detailedCockpit=true;Project.reduceCloseClipping=true;
        manager.transform.SetPositionAndRotation(position,rotation);manager.CheckOriginShift();RestoreCameraLens(lens);
        UpdateCameraVisibility();Status="Paused at the cockpit seat. Look around in Free camera, or add attached path points.";return true;
    }
}

