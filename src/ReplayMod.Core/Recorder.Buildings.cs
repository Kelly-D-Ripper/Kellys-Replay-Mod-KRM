using System;
using System.Collections.Generic;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class Recorder
{
    private sealed class RecordedBuilding
    { internal Building actor; internal BuildingStateSnapshot published; internal bool failed; internal float duration; }
    private readonly Dictionary<uint,RecordedBuilding> _recordedBuildings=new Dictionary<uint,RecordedBuilding>();
    private double _lastBuildingCapture=-1;
    public void ObserveInitializedBuilding(Building building)
    {
        if(!_acceptEvents||!SessionValid||building==null||_unitsOnTrack.ContainsKey(building.persistentID))return;
        // Native map-hidden buildings skip UnitRegistry.RegisterUnit entirely.
        // This initialization callback observes just that one new native actor.
        HandleUnitRegister(building,building.persistentID);
    }
    public void ObserveDisabledBuilding(Building building)
    {
        if(!IsTracked(building)||!SessionValid)return;
        if(_recordedBuildings.TryGetValue(building.persistentID.Id,out var binding))RecordBuildingState(binding);
    }
    private void RecordBuildingActor(Unit unit)
    {
        if(!(unit is Building building))return;
        var prefab=building.definition?.unitPrefab?.GetComponent<Building>();
        var binding=new RecordedBuilding {actor=building,duration=prefab!=null?prefab.collapseTime:building.collapseTime};_recordedBuildings[unit.persistentID.Id]=binding;
        RecordBuildingState(binding);
    }
    private void RecordBuildingStates()
    {
        if(_lastBuildingCapture>=0&&_virtualTime-_lastBuildingCapture<.1)return;
        _lastBuildingCapture=_virtualTime;
        foreach(var binding in _recordedBuildings.Values)
            if(!binding.failed&&IsTracked(binding.actor))RecordBuildingState(binding);
    }
    private void RecordBuildingState(RecordedBuilding binding)
    {
        if(binding.failed||binding.actor==null)return;
        try
        {
            var building=binding.actor;
            float duration=binding.duration;
            if(!BuildingStateSnapshot.Finite(duration)||duration<0||duration>120)throw new InvalidOperationException("Unsupported building collapse duration");
            if(binding.published?.Matches(building.disabled,duration,building.startPosition,building.startRotation)==true)return;
            var sample=new BuildingStateSnapshot {time=_virtualTime,disabled=building.disabled,duration=duration,
                anchor=building.startPosition,rotation=building.startRotation};
            if(sample.disabled)
            {
                float remaining=Math.Max(0,Math.Min(duration,building.collapseTime));
                sample.collapseStartTime=_virtualTime-(duration-remaining);
            }
            var e=ReplayEventFactory.GetEvent<BuildingStateEvent>();e.unitId=building.persistentID.Id;sample.CopyTo(e);
            if(!e.HasValidPayload()){ReplayEventFactory.Return(e);throw new InvalidOperationException("Invalid building foundation or collapse clock");}
            QueueEvent(e);binding.published=sample;
        }
        catch(Exception error)
        {
            binding.failed=true;CaptureFailureCount++;
            Plugin.logger.LogWarning("Replay building presentation capture unavailable for "+binding.actor.persistentID.Id+": "+error.Message);
        }
    }
    private void ForgetRecordingBuilding(Unit unit)
    {if(!ReferenceEquals(unit,null))_recordedBuildings.Remove(unit.persistentID.Id);}
    private void ClearRecordingBuildings(){_recordedBuildings.Clear();_lastBuildingCapture=-1;}
}
