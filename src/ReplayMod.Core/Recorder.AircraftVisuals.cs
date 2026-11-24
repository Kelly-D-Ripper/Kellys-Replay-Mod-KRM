using System.Collections.Generic;
using System.Linq;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class Recorder
{
    private sealed class RecordedAircraftVisuals
    {
        public ReplayAircraftVisuals binding;
        public readonly AircraftVisualChangeTracker changes=new AircraftVisualChangeTracker();
    }
    private readonly Dictionary<uint,RecordedAircraftVisuals> _recordedAircraftVisuals=new Dictionary<uint,RecordedAircraftVisuals>();
    private readonly HashSet<uint> _unsupportedAircraftVisuals=new HashSet<uint>();
    private readonly Dictionary<uint,double> _lastAircraftVisualCaptureTimes=new Dictionary<uint,double>();
    private void LateUpdate()
    {
        if(!_acceptEvents||_cts==null||_cts.IsCancellationRequested||!SessionValid)return;
        // Recorder runs after normal native presentation scripts. The tracked
        // unit table is already cached; there are no scene scans in this pass.
        foreach(var pair in _unitsOnTrack)
        {
            if(pair.Value is GroundVehicle||pair.Value is Container)RecordCargoParachutes(pair.Value);
            if(pair.Value is Missile missile && missile!=null)
            {
                RecordObservedMissileMotion(missile);
                continue;
            }
            if(!(pair.Value is Aircraft aircraft)||aircraft==null)continue;
            uint id=aircraft.persistentID.Id;
            if(_lastAircraftVisualCaptureTimes.TryGetValue(id,out var last)&&_virtualTime-last<ReplayManager.UpdateInputsInterval)continue;
            RecordObservedAircraftMotion(aircraft);
            RecordAircraftVisuals(aircraft);_lastAircraftVisualCaptureTimes[id]=_virtualTime;
            RecordAircraftLights(aircraft);
            RecordCargoMounts(aircraft);
            RecordMissileMounts(aircraft);
        }
        CaptureCountermeasuresLate();
        CaptureFuelFlamesLate();
        CaptureActorIdentitiesLate();
        CaptureEjectionEyesLate();
    }
    private void RecordAircraftVisuals(Aircraft aircraft)
    {
        if(aircraft==null||_unsupportedAircraftVisuals.Contains(aircraft.persistentID.Id))return;
        try {CaptureAircraftVisuals(aircraft);}
        catch(System.Exception error)
        {
            _unsupportedAircraftVisuals.Add(aircraft.persistentID.Id);
            Plugin.logger.LogWarning("Aircraft visual capture unavailable for "+aircraft.definition?.jsonKey+": "+error);
        }
    }
    private void CaptureAircraftVisuals(Aircraft aircraft)
    {
        if(aircraft==null||aircraft.partLookup==null||aircraft.partLookup.Count==0||aircraft.gearState==LandingGear.GearState.Uninitialized)return;
        uint id=aircraft.persistentID.Id;
        if(!_recordedAircraftVisuals.TryGetValue(id,out var tracked))
        {
            var binding=new ReplayAircraftVisuals(aircraft);
            _recordedAircraftVisuals[id]=tracked=new RecordedAircraftVisuals {binding=binding};
            var layout=ReplayEventFactory.GetEvent<AircraftVisualLayoutEvent>();layout.unitId=id;layout.Time=_virtualTime;layout.layoutId=binding.layoutId;
            layout.channels=binding.channels.Select(channel=>channel.key).ToArray();QueueEvent(layout);
        }
        tracked.changes.Observe(tracked.binding.Capture(_virtualTime),snapshot=>QueueAircraftVisualState(id,snapshot));
    }
    private void QueueAircraftVisualState(uint id,AircraftVisualSnapshot snapshot)
    {
        var state=ReplayEventFactory.GetEvent<AircraftVisualStateEvent>();state.unitId=id;state.Time=snapshot.time;state.layoutId=snapshot.layoutId;
        state.gearDeployed=snapshot.gearDeployed;state.gearState=snapshot.gearState;state.blendSeconds=snapshot.blendSeconds;state.poses=(AircraftVisualPose[])snapshot.poses.Clone();QueueEvent(state);
    }
    private void ForgetRecordingAircraftVisuals(uint id){_recordedAircraftVisuals.Remove(id);_unsupportedAircraftVisuals.Remove(id);_lastAircraftVisualCaptureTimes.Remove(id);}
    private void ClearRecordingAircraftVisuals(){_recordedAircraftVisuals.Clear();_unsupportedAircraftVisuals.Clear();_lastAircraftVisualCaptureTimes.Clear();}
}
