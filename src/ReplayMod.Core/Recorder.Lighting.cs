using System;
using System.Collections.Generic;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class Recorder
{
    private readonly Dictionary<uint,ReplayAircraftLights> _recordingLights=new Dictionary<uint,ReplayAircraftLights>();
    private readonly HashSet<uint> _recordingLightFailures=new HashSet<uint>();
    private void RecordAircraftLights(Aircraft aircraft)
    {
        uint id=aircraft.persistentID.Id;if(_recordingLightFailures.Contains(id))return;
        AircraftLightingEvent value=null;
        try
        {
            if(!_recordingLights.TryGetValue(id,out var lights))_recordingLights[id]=lights=new ReplayAircraftLights(aircraft);
            value=ReplayEventFactory.GetEvent<AircraftLightingEvent>();lights.Capture(value);
            if(!value.HasValidPayload())throw new InvalidOperationException("Invalid native lamp layout");
            var queued=value;value=null;QueueVfxState(queued,id+":lighting");
        }
        catch(Exception error){if(value!=null)ReplayEventFactory.Return(value);CaptureFailureCount++;if(_recordingLightFailures.Add(id))Plugin.logger.LogWarning("Aircraft lighting capture unavailable for "+id+": "+error.Message);}
    }
    private void ForgetRecordingLights(uint id){_recordingLights.Remove(id);_recordingLightFailures.Remove(id);}
    private void ClearRecordingLights(){_recordingLights.Clear();_recordingLightFailures.Clear();}
}
