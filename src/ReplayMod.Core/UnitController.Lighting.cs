using System.Collections.Generic;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class UnitController
{
    private readonly Dictionary<uint,ReplayAircraftLights> _playbackLights=new Dictionary<uint,ReplayAircraftLights>();
    private readonly Dictionary<uint,AircraftLightingEvent> _pendingPlaybackLights=new Dictionary<uint,AircraftLightingEvent>();
    private ReplayAircraftLights PlaybackLights(uint id,Aircraft aircraft)
    {
        if(_playbackLights.TryGetValue(id,out var lights))return lights;
        if(aircraft.partLookup==null||aircraft.partLookup.Count==0)return null;
        lights=new ReplayAircraftLights(aircraft);_playbackLights[id]=lights;return lights;
    }
    public void Execute(AircraftLightingEvent value)
    {
        if(!value.HasValidPayload()||!AcceptSample(value.unitId,value.Time)||!(FindActor(value.unitId)is Aircraft aircraft))return;
        // Own arrays after the event is returned to its producer pool. Native
        // lamp initialization can complete one frame after replay spawn.
        _pendingPlaybackLights[value.unitId]=new AircraftLightingEvent {unitId=value.unitId,Time=value.Time,keys=(string[])value.keys.Clone(),flags=(byte[])value.flags.Clone(),controls=(byte[])value.controls.Clone()};
        PlaybackLights(value.unitId,aircraft)?.Read(value);
    }
    public void RefreshReplayLights()
    {
        foreach(var pair in _unitMap)
        {
            if(FindActor(pair.Key)is Aircraft aircraft)ApplyPlaybackLights(pair.Key,aircraft);
        }
    }
    public void RefreshReplayLightsFor(Aircraft aircraft)
    {
        // Native gear callbacks can toggle the lamps between the normal replay
        // update and rendering. Restrict this correction to our current actor.
        if(aircraft==null||!_reverseUnitMap.TryGetValue(aircraft.persistentID,out uint id)||!ReferenceEquals(FindActor(id),aircraft))return;
        ApplyPlaybackLights(id,aircraft);
    }
    private void ApplyPlaybackLights(uint id,Aircraft aircraft)
    {
        var lights=PlaybackLights(id,aircraft);if(lights==null)return;
        if(_pendingPlaybackLights.TryGetValue(id,out var pending)){lights.Read(pending);_pendingPlaybackLights.Remove(id);}
        var project=ReplayMod.Director.ReplayDirector.i?.Project;
        bool selected=project!=null&&(project.lightsAll||project.lightTarget==id);
        lights.Apply(selected?project.lightMode:0,selected?project.landingLightMode:0);
    }
    private void ForgetPlaybackLights(uint id){_playbackLights.Remove(id);_pendingPlaybackLights.Remove(id);}
    private void ClearPlaybackLights(){_playbackLights.Clear();_pendingPlaybackLights.Clear();}
}
