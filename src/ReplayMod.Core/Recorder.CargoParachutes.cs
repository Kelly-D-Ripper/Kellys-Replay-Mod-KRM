using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class Recorder
{
    private readonly Dictionary<uint,byte[]> _lastCargoParachutes=new Dictionary<uint,byte[]>();
    private readonly Dictionary<uint,CargoParachutePose[]> _lastCargoParachutePoses=new Dictionary<uint,CargoParachutePose[]>();
    private readonly Dictionary<uint,double> _cargoParachuteTimes=new Dictionary<uint,double>();
    private readonly Dictionary<uint,double> _cargoParachuteSearchTimes=new Dictionary<uint,double>();
    private readonly Dictionary<uint,CargoDeploymentSystem> _cargoParachuteSystems=new Dictionary<uint,CargoDeploymentSystem>();
    private readonly HashSet<uint> _cargoParachuteWarnings=new HashSet<uint>();
    private void RecordCargoParachutes(Unit unit)
    {
        if(unit==null||ReplayCargoParachutes.Prefab(unit)==null)return;
        uint id=unit.persistentID.Id;if(_cargoParachuteWarnings.Contains(id))return;
        if(_cargoParachuteTimes.TryGetValue(id,out double last)&&_virtualTime-last<ReplayManager.UpdateInputsInterval)return;
        _cargoParachuteTimes[id]=_virtualTime;CargoParachuteStateEvent e=null;
        try
        {
            _cargoParachuteSystems.TryGetValue(id,out var system);
            if(system==null&&(!_cargoParachuteSearchTimes.TryGetValue(id,out double search)||_virtualTime>=search))
            {
                system=ReplayCargoParachutes.Find(unit);_cargoParachuteSystems[id]=system;
                _cargoParachuteSearchTimes[id]=_virtualTime+.25;
            }
            bool present=system!=null&&system.gameObject.activeInHierarchy;
            e=ReplayEventFactory.GetEvent<CargoParachuteStateEvent>();e.unitId=id;e.present=present;
            _lastCargoParachutePoses.TryGetValue(id,out var previousPoses);
            e.parachutes=present?ReplayCargoParachutes.Capture(system,previousPoses):Array.Empty<CargoParachutePose>();
            if(present)_lastCargoParachutePoses[id]=e.parachutes;else _lastCargoParachutePoses.Remove(id);
            e.Time=0;var encoded=RollingReplayBuffer.Encode(e);
            if(_lastCargoParachutes.TryGetValue(id,out var previous)&&previous.SequenceEqual(encoded)){ReplayEventFactory.Return(e);return;}
            _lastCargoParachutes[id]=encoded;e.Time=_virtualTime;var submitted=e;e=null;QueueEvent(submitted);
        }
        catch(Exception error)
        {
            if(e!=null)ReplayEventFactory.Return(e);CaptureFailureCount++;
            if(_cargoParachuteWarnings.Add(id))Plugin.logger.LogWarning("Cargo parachute capture unavailable for "+unit.definition?.jsonKey+": "+error);
        }
    }
    private void ForgetRecordingCargoParachutes(uint id){_lastCargoParachutes.Remove(id);_lastCargoParachutePoses.Remove(id);_cargoParachuteTimes.Remove(id);_cargoParachuteSearchTimes.Remove(id);_cargoParachuteSystems.Remove(id);_cargoParachuteWarnings.Remove(id);}
    private void ClearRecordingCargoParachutes(){_lastCargoParachutes.Clear();_lastCargoParachutePoses.Clear();_cargoParachuteTimes.Clear();_cargoParachuteSearchTimes.Clear();_cargoParachuteSystems.Clear();_cargoParachuteWarnings.Clear();}
}
