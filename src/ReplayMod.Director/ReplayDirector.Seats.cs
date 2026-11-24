using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Director;

public partial class ReplayDirector
{
    public int CockpitSeatCount=>ReplayCockpitSeats.SeatCount(ResolveCameraActor(Project.target) as Aircraft);
    public static string CockpitSeatName(int seat)=>seat==0?"Pilot":seat==1?"Copilot":"Crew seat "+(seat+1);
    public bool CockpitSeatAvailable(int seat)
    {
        var aircraft=ResolveCameraActor(Project.target) as Aircraft;
        return ReplayInteriorPresentation.TryGetCockpitEye(ReplayManager.i?.Playback,Project.target,aircraft,out _,out _,seat);
    }
    public bool SelectCockpitSeat(int seat)
    {
        if(!ReplayRuntime.IsPlayback||IsBusy||!CockpitSeatAvailable(seat))
        {Status="This seat has no available attached crew viewpoint at this moment.";return false;}
        int previous=Project.cockpitSeat;Project.cockpitSeat=seat;
        if(!SelectCameraRig(5)){Project.cockpitSeat=previous;return false;}
        RestoreEjectionCamera();ResetCameraSmoothing();
        Status="Following the "+CockpitSeatName(seat).ToLowerInvariant()+" viewpoint.";
        return true;
    }
}
