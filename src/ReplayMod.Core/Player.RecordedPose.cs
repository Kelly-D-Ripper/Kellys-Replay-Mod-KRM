using ReplayMod.Data;

namespace ReplayMod.Core;

public partial class Player
{
    // Replay bodies are kinematic. Their native Rigidbody velocity is not a
    // telemetry source; use the current actor lifetime's recording instead.
    public bool TryGetRecordedPose(Unit unit,out PositionSnapshot pose)
    {pose=default;return _unitCotroller!=null&&_unitCotroller.TryGetRecordedPose(unit,currentVirtualTime,out pose);}
}

public partial class UnitController
{
    public bool TryGetRecordedPose(Unit unit,double time,out PositionSnapshot pose)
    {
        pose=default;
        if(unit==null||!ReplayMod.Director.DirectorProject.Finite(time)||!_reverseUnitMap.TryGetValue(unit.persistentID,out uint id)||
            !TryGetUnit(id,out _,out var current)||!ReferenceEquals(current,unit)||!_spawnTimes.TryGetValue(id,out double birth)||time<birth||
            !_unitSnapshots.TryGetValue(id,out var samples)||!SnapshotHistory.Locate(samples,time,PositionTime,out int left,out int right,out _))return false;
        pose=ReplayPoseInterpolation.At(samples[left],samples[right],time);return true;
    }
}
