using System.Collections.Generic;
using System.Linq;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class UnitController
{
    private readonly Dictionary<uint,ReplayMissileMounts> _missileMountBindings=new Dictionary<uint,ReplayMissileMounts>();
    private readonly Dictionary<uint,MissileMountStateEvent> _pendingMissileMounts=new Dictionary<uint,MissileMountStateEvent>();
    public void Execute(MissileMountStateEvent e)
    {
        if(!AcceptSample(e.unitId,e.Time)||!(FindActor(e.unitId) is Aircraft aircraft))return;
        _pendingMissileMounts[e.unitId]=new MissileMountStateEvent{unitId=e.unitId,Time=e.Time,mounts=(CargoMountPose[])e.mounts.Clone()};
        TryApplyMissileMounts(e.unitId,aircraft);
    }
    // Loadout mounts can initialize a frame after the spawn; keep the latest
    // state pending until the binding matches it.
    private void TryApplyMissileMounts(uint id,Aircraft aircraft)
    {
        if(!_pendingMissileMounts.TryGetValue(id,out var state))return;
        if(!_missileMountBindings.TryGetValue(id,out var binding)||binding.Count==0||!binding.IsValid)
            _missileMountBindings[id]=binding=new ReplayMissileMounts(aircraft);
        if(binding.Count>0&&binding.Apply(state.mounts))_pendingMissileMounts.Remove(id);
    }
    private void FlushMissileMounts()
    {
        foreach(var id in _pendingMissileMounts.Keys.ToArray())
            if(FindActor(id) is Aircraft aircraft)TryApplyMissileMounts(id,aircraft);
    }
    private void ForgetMissileMounts(uint id){_missileMountBindings.Remove(id);_pendingMissileMounts.Remove(id);}
    private void ClearMissileMounts(){_missileMountBindings.Clear();_pendingMissileMounts.Clear();}
}
