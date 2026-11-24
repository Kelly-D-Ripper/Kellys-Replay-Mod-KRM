using System.Collections.Generic;
using System.Linq;
using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class UnitController
{
    private sealed class PlaybackAircraftVisuals
    {
        public ReplayAircraftVisuals binding;
        public ReplayAircraftVisuals.Channel[] channels;
        public string[] names;
        public uint layout;
        public int[] applyOrder;
        public readonly AircraftVisualTimeline samples=new AircraftVisualTimeline();
        public bool hasSample;
        public bool hasDisplayed;
    }
    private readonly Dictionary<uint,PlaybackAircraftVisuals> _aircraftVisuals=new Dictionary<uint,PlaybackAircraftVisuals>();
    private readonly Dictionary<uint,bool> _recordedGear=new Dictionary<uint,bool>();
    private double _lastAircraftVisualTime;
    private PlaybackAircraftVisuals AircraftVisuals(uint id,Aircraft aircraft)
    {
        if(!_aircraftVisuals.TryGetValue(id,out var state))_aircraftVisuals[id]=state=new PlaybackAircraftVisuals();
        if(state.binding==null&&aircraft.partLookup!=null&&aircraft.partLookup.Count>0)state.binding=new ReplayAircraftVisuals(aircraft);
        return state;
    }
    public bool OwnsReplayAircraft(Aircraft aircraft)
    {
        return aircraft!=null&&_reverseUnitMap.TryGetValue(aircraft.persistentID,out var id)&&TryGetUnit(id,out _,out var current)&&ReferenceEquals(current,aircraft);
    }
    public bool HasRecordedAircraftVisuals(Aircraft aircraft)
    {
        return OwnsReplayAircraft(aircraft)&&_reverseUnitMap.TryGetValue(aircraft.persistentID,out var id)&&_aircraftVisuals.TryGetValue(id,out var state)&&state.hasDisplayed;
    }
    public void Execute(AircraftVisualLayoutEvent e)
    {
        if(!AcceptSample(e.unitId,e.Time)||!TryGetUnit(e.unitId,out _,out var unit)||!(unit is Aircraft aircraft))return;
        var state=AircraftVisuals(e.unitId,aircraft);
        if(state.layout!=e.layoutId){state.samples.Clear();state.hasSample=false;state.hasDisplayed=false;}
        state.samples.RetainLayout(e.layoutId,e.channels.Length);state.hasSample=state.samples.Count>0;
        state.layout=e.layoutId;state.names=(string[])e.channels.Clone();state.channels=null;ResolveAircraftVisuals(state);
    }
    public void Execute(AircraftVisualStateEvent e)
    {
        if(!AcceptSample(e.unitId,e.Time)||!TryGetUnit(e.unitId,out _,out var unit)||!(unit is Aircraft aircraft))return;
        var state=AircraftVisuals(e.unitId,aircraft);
        // Future interpolation may be delivered before its same-time layout
        // action is due. Own that sample now; resolve it when layout arrives.
        if(state.names==null)
        {
            if(state.layout!=e.layoutId)state.samples.Clear();
            state.layout=e.layoutId;
        }
        else if(state.layout!=e.layoutId||state.names.Length!=e.poses.Length)return;
        state.samples.Add(e);state.hasSample=true;
    }
    private void ApplyRecordedGear(SetGearEvent e)
    {
        _recordedGear[e.unitId]=e.deployed;
        if(!TryGetUnit(e.unitId,out _,out var unit)||!(unit is Aircraft aircraft))return;
        if(e.Instant||e.Time==0)
        {
            aircraft.NetworkgearDeployed=e.deployed;
            aircraft.SetGear(e.deployed?LandingGear.GearState.LockedExtended:LandingGear.GearState.LockedRetracted);
        }
        else aircraft.SetGear(e.deployed);
    }
    private void UpdateAircraftVisuals(double time)
    {
        double delta=System.Math.Max(0,System.Math.Min(0.25,time-_lastAircraftVisualTime));_lastAircraftVisualTime=time;
        foreach(var pair in _unitMap)
        {
            if(!TryGetUnit(pair.Key,out _,out var unit)||!(unit is Aircraft aircraft))continue;
            var state=AircraftVisuals(pair.Key,aircraft);
            if(state.binding==null)continue;
            ResolveAircraftVisuals(state);
            if(!ApplyRecordedAircraftVisuals(state,aircraft,time))
            {
                if(_recordedGear.TryGetValue(pair.Key,out bool deployed)&&aircraft.gearDeployed!=deployed)
                {aircraft.NetworkgearDeployed=deployed;aircraft.SetGear(deployed?LandingGear.GearState.LockedExtended:LandingGear.GearState.LockedRetracted);}
                state.binding.ApplyLegacySweep();state.binding.ApplyLegacyTilt();state.binding.ApplyLegacyGear(delta);state.binding.ApplyLegacyTailHooks();
            }
        }
    }
    // Called again after normal native/mod LateUpdates. It reapplies the same
    // recorded frame and deliberately never advances fallback wheel rotation,
    // native gear coroutines, or the replay clock.
    public void RefreshAircraftVisualsLate(double time)
    {
        foreach(var pair in _aircraftVisuals)
        {
            var state=pair.Value;
            if(!state.hasSample||state.binding==null||!TryGetUnit(pair.Key,out _,out var unit)||!(unit is Aircraft aircraft))continue;
            ApplyRecordedAircraftVisuals(state,aircraft,time);
        }
        RefreshReplayLights();
    }
    private static bool ApplyRecordedAircraftVisuals(PlaybackAircraftVisuals state,Aircraft aircraft,double time)
    {
        if(!state.hasSample||state.channels==null||!state.samples.Locate(time,out var previous,out var next,out var fraction))return false;
        state.hasDisplayed=true;
        // Discrete state belongs to the preceding sample, not lookahead.
        aircraft.gearDeployed=previous.gearDeployed;aircraft.gearState=(LandingGear.GearState)previous.gearState;
        foreach(int i in state.applyOrder)state.channels[i]?.Apply(previous.poses[i],next.poses[i],fraction);
        state.binding.UpdateTiltLinkages();return true;
    }
    private void ForgetAircraftVisuals(uint id){_aircraftVisuals.Remove(id);_recordedGear.Remove(id);}
    private void ClearAircraftVisuals(){_aircraftVisuals.Clear();_recordedGear.Clear();_lastAircraftVisualTime=0;}
    private static void ResolveAircraftVisuals(PlaybackAircraftVisuals state)
    {
        if(state.binding==null||state.names==null||state.channels!=null)return;
        state.channels=state.binding.Resolve(state.names);
        state.applyOrder=Enumerable.Range(0,state.channels.Length).OrderBy(i=>Depth(state.channels[i]?.target)).ToArray();
        int missing=0;foreach(var channel in state.channels)if(channel==null)missing++;
        if(missing>0)Plugin.logger.LogWarning("Replay aircraft visual channels unavailable: "+missing+" of "+state.channels.Length+"; check the recorded aircraft/mod version");
    }
    private static int Depth(Transform transform){int depth=0;while(transform!=null&&depth<128){depth++;transform=transform.parent;}return depth;}
}
