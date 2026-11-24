using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class Recorder
{
    private sealed class RecordedAircraftParts
    {
        public UnitPart[] parts;
        public AircraftPartPose[] last;
        public AircraftPartMotionSnapshot published;
        public double observedTime;
        public bool active,failed;
    }
    private readonly Dictionary<uint,RecordedAircraftParts> _recordedAircraftParts=new Dictionary<uint,RecordedAircraftParts>();
    private readonly ReplayPartDetachTracker _observedPartDetaches=new ReplayPartDetachTracker();
    private readonly HashSet<uint> _aircraftLifecycleWarnings=new HashSet<uint>();
    private bool IsTrackedLifecycleUnit(Unit unit)=>_acceptEvents&&SessionValid&&unit!=null&&_unitsOnTrack.TryGetValue(unit.persistentID,out var tracked)&&ReferenceEquals(unit,tracked);
    private RecordedAircraftParts EnsureRecordedAircraftParts(Aircraft aircraft)
    {
        uint id=aircraft.persistentID.Id;
        if(_recordedAircraftParts.TryGetValue(id,out var state))return state;
        if(aircraft.partLookup==null||aircraft.partLookup.Count==0)return null;
        var parts=aircraft.partLookup.Where(part=>part!=null).GroupBy(part=>part.id).Select(group=>group.First()).OrderBy(part=>part.id).Take(AircraftPartMotionSnapshot.MaxParts).ToArray();
        if(parts.Length==0)return null;
        var last=parts.Select(part=>new AircraftPartPose {partId=part.id,position=part.transform.position.ToGlobalPosition(),rotation=part.transform.rotation}).ToArray();
        _recordedAircraftParts[id]=state=new RecordedAircraftParts {parts=parts,last=last};return state;
    }
    private RecordedAircraftParts TryEnsureRecordedAircraftParts(Aircraft aircraft)
    {
        try{return EnsureRecordedAircraftParts(aircraft);}
        catch(Exception error)
        {
            if(_aircraftLifecycleWarnings.Add(aircraft.persistentID.Id))Plugin.logger.LogWarning("Aircraft lifecycle binding unavailable: "+error);
            return null;
        }
    }
    // A sender's DetachPart command and the received UnitPart.Detach both pass
    // here. Observe once per actual actor/part lifetime, including remote peers.
    private bool NoteObservedPartDetach(Unit unit,byte partId)
    {
        if(!IsTrackedLifecycleUnit(unit)||!_observedPartDetaches.Observe(unit.persistentID.Id,partId))return false;
        if(unit is Aircraft aircraft)
        {var state=TryEnsureRecordedAircraftParts(aircraft);if(state!=null)state.active=true;}
        return true;
    }
    public void RecordReceivedPartDetach(Unit unit,byte partId)
    {
        try{if(IsTrackedLifecycleUnit(unit))HandlePartDetach(unit,partId);}
        catch(Exception error){Plugin.logger.LogWarning("Received part observation failed without changing native detach: "+error);}
    }
    private void NoteAircraftEjection(Aircraft aircraft)
    {
        if(!IsTrackedLifecycleUnit(aircraft))return;
        var state=TryEnsureRecordedAircraftParts(aircraft);if(state!=null)state.active=true;
    }
    private void RecordInitialAircraftPartMotion(Unit unit)
    {
        if(!(unit is Aircraft aircraft)||!IsTrackedLifecycleUnit(unit))return;
        // A recording can begin with a wreck already present. Its first pose
        // must describe that world at time zero, before the regular .05s motion
        // interval is due. Pristine aircraft need no extra part-state allocation.
        bool required=aircraft.disabled||aircraft.HasEjected();
        if(!required&&aircraft.partLookup!=null)
            for(int i=0;i<aircraft.partLookup.Count;i++)
            {
                var part=aircraft.partLookup[i];
                if(part!=null&&(part.hitPoints!=100f||part.IsDetached()||part.fragmented||
                    (part.attachInfo!=null&&part.attachInfo.detachedFromParentPart)))
                {required=true;break;}
            }
        if(!required)return;
        var state=TryEnsureRecordedAircraftParts(aircraft);
        if(state==null||state.published!=null)return;
        state.active=true;
        RecordAircraftPartMotion(aircraft,state);
    }
    private void RecordObservedAircraftMotion(Aircraft aircraft)
    {
        if(!IsTrackedLifecycleUnit(aircraft))return;
        if(_lastPositionUpdateTime.TryGetValue(aircraft.persistentID,out var previous)&&_virtualTime-previous<ReplayManager.UpdateInputsInterval)return;
        QueueObservedAircraftPose(aircraft);
        RecordAircraftPartMotion(aircraft,TryEnsureRecordedAircraftParts(aircraft));
    }
    private void RecordFinalAircraftMotion(Unit unit)
    {
        if(!(unit is Aircraft aircraft)||!IsTrackedLifecycleUnit(unit))return;
        QueueObservedAircraftPose(aircraft);RecordAircraftPartMotion(aircraft,TryEnsureRecordedAircraftParts(aircraft));
    }
    private void QueueObservedAircraftPose(Aircraft aircraft)
    {
        var pose=ReplayEventFactory.GetEvent<UpdatePositionEvent>();pose.unitId=aircraft.persistentID.Id;pose.Time=_virtualTime;
        pose.position=aircraft.transform.position.ToGlobalPosition();pose.rotation=aircraft.transform.rotation;
        pose.velocity=aircraft.rb!=null?aircraft.rb.velocity:Vector3.zero;
        if(!AircraftPartMotionSnapshot.Valid(new AircraftPartPose {position=pose.position,rotation=pose.rotation})||
            !AircraftVisualSnapshot.Finite(pose.velocity.x)||!AircraftVisualSnapshot.Finite(pose.velocity.y)||!AircraftVisualSnapshot.Finite(pose.velocity.z))
        {
            ReplayEventFactory.Return(pose);CaptureFailureCount++;
            if(_aircraftLifecycleWarnings.Add(aircraft.persistentID.Id))Plugin.logger.LogWarning("Invalid observed aircraft world pose skipped for "+aircraft.persistentID.Id);
            return;
        }
        QueueEvent(pose);
        _lastPositionUpdateTime[aircraft.persistentID]=_virtualTime;
    }
    private void RecordAircraftPartMotion(Aircraft aircraft,RecordedAircraftParts state)
    {
        if(state==null||state.failed)return;
        try
        {
            state.active|=aircraft.disabled||aircraft.HasEjected();
            for(int i=0;i<state.parts.Length;i++)
            {
                var part=state.parts[i];var sample=state.last[i];sample.flags=AircraftPartFlags.None;
                if(part!=null)
                {
                    if(part.gameObject.activeInHierarchy)sample.flags|=AircraftPartFlags.Present;
                    if(part.IsDetached()||(part.attachInfo!=null&&part.attachInfo.detachedFromParentPart))sample.flags|=AircraftPartFlags.Detached;
                    if(part.fragmented)sample.flags|=AircraftPartFlags.Fragmented;
                    sample.position=part.transform.position.ToGlobalPosition();sample.rotation=part.transform.rotation;
                    state.active|=(sample.flags&(AircraftPartFlags.Detached|AircraftPartFlags.Fragmented))!=0;
                }
                if(AircraftPartMotionSnapshot.Valid(sample))state.last[i]=sample;
            }
            if(!state.active){state.observedTime=_virtualTime;return;}
            var current=new AircraftPartMotionSnapshot {time=_virtualTime,parts=(AircraftPartPose[])state.last.Clone()};
            if(state.published==null||state.published.Changed(current))
            {
                current.blendSeconds=state.published==null?0:(float)Math.Max(0,Math.Min(3600,current.time-state.observedTime));
                var e=ReplayEventFactory.GetEvent<AircraftPartMotionEvent>();e.unitId=aircraft.persistentID.Id;current.CopyTo(e);QueueEvent(e);state.published=current;
            }
            state.observedTime=_virtualTime;
        }
        catch(Exception error)
        {
            state.failed=true;Plugin.logger.LogWarning("Aircraft part motion capture unavailable for "+aircraft.definition?.jsonKey+": "+error);
        }
    }
    private void ForgetRecordingAircraftLifecycle(uint unitId){_recordedAircraftParts.Remove(unitId);_observedPartDetaches.Forget(unitId);_aircraftLifecycleWarnings.Remove(unitId);}
    private void ClearRecordingAircraftLifecycle(){_recordedAircraftParts.Clear();_observedPartDetaches.Clear();_aircraftLifecycleWarnings.Clear();}
}
