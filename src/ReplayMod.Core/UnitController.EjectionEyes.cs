using System.Collections.Generic;
using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class UnitController
{
    private readonly Dictionary<uint,PilotEyeTimeline> _pilotEyeTimelines=new Dictionary<uint,PilotEyeTimeline>();
    public void Execute(PilotEjectionStateEvent value)
    {
        if(!value.HasValidPayload()||!AcceptSample(value.unitId,value.Time))return;
        if(!_pilotEyeTimelines.TryGetValue(value.unitId,out var timeline))_pilotEyeTimelines[value.unitId]=timeline=new PilotEyeTimeline();
        timeline.Add(value);
    }
    public bool TryActorBirth(uint id,out double birth)=>_spawnTimes.TryGetValue(id,out birth);
    public bool TryEjectionSourceBirth(uint aircraft,double time,out double birth)
    {
        birth=double.NaN;
        foreach(var pair in _pilotEyeTimelines)
            if(pair.Value.Aircraft==aircraft&&pair.Value.Seat==0&&TryGetUnit(pair.Key,out _,out _)&&pair.Value.TryAt(time,out _))
            {if(!double.IsNaN(birth)&&(birth!=pair.Value.SourceBirth||birth<0))return false;birth=pair.Value.SourceBirth;}
        return !double.IsNaN(birth);
    }
    public bool TryEjectionEye(uint aircraft,double birth,double time,out Unit carrier,out PositionSnapshot eye)
    {
        carrier=null;eye=default;
        foreach(var pair in _pilotEyeTimelines)
            if(pair.Value.Aircraft==aircraft&&pair.Value.SourceBirth==birth&&pair.Value.Seat==0&&TryGetUnit(pair.Key,out _,out var actor)&&pair.Value.TryAt(time,out var pose))
            {if(carrier!=null){carrier=null;eye=default;return false;}carrier=actor;eye=pose;}
        return carrier!=null;
    }
    private void ForgetEjectionEye(uint id)=>_pilotEyeTimelines.Remove(id);
    private void ClearEjectionEyes()=>_pilotEyeTimelines.Clear();
}

public partial class Player
{
    public bool TryActorBirth(uint id,out double birth){birth=0;return _unitCotroller!=null&&_unitCotroller.TryActorBirth(id,out birth);}
    public bool TryEjectionSourceBirth(uint id,out double birth){birth=double.NaN;return _unitCotroller!=null&&_unitCotroller.TryEjectionSourceBirth(id,currentVirtualTime,out birth);}
    public bool TryEjectionEye(uint aircraft,double birth,out Unit carrier,out PositionSnapshot eye)
    {carrier=null;eye=default;return _unitCotroller!=null&&_unitCotroller.TryEjectionEye(aircraft,birth,currentVirtualTime,out carrier,out eye);}
}
