using System.Collections.Generic;
using System.Linq;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class UnitController
{
    private readonly Dictionary<uint,ReplayCargoMounts> _cargoMounts=new Dictionary<uint,ReplayCargoMounts>();
    private readonly Dictionary<uint,CargoMountStateEvent> _pendingCargoMounts=new Dictionary<uint,CargoMountStateEvent>();
    public void Execute(CargoMountStateEvent e)
    {
        if(!AcceptSample(e.unitId,e.Time)||!(FindActor(e.unitId) is Aircraft aircraft))return;
        _pendingCargoMounts[e.unitId]=new CargoMountStateEvent{unitId=e.unitId,Time=e.Time,mounts=(CargoMountPose[])e.mounts.Clone()};
        TryApplyCargoMounts(e.unitId,aircraft);
    }
    private void TryApplyCargoMounts(uint id,Aircraft aircraft)
    {
        if(!_pendingCargoMounts.TryGetValue(id,out var state))return;
        if(!_cargoMounts.TryGetValue(id,out var binding)||binding.Count==0||!binding.IsValid)
            _cargoMounts[id]=binding=new ReplayCargoMounts(aircraft);
        if(binding.Count>0&&binding.Apply(state.mounts))_pendingCargoMounts.Remove(id);
    }
    private void FlushCargoMounts()
    {
        foreach(var id in _pendingCargoMounts.Keys.ToArray())
            if(FindActor(id) is Aircraft aircraft)TryApplyCargoMounts(id,aircraft);
    }
    private void ForgetCargoMounts(uint id){_cargoMounts.Remove(id);_pendingCargoMounts.Remove(id);}
    private void ClearCargoMounts(){_cargoMounts.Clear();_pendingCargoMounts.Clear();}
}
