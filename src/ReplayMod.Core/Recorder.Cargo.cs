using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class Recorder
{
    private sealed class RecordedCargo {internal ReplayCargoMounts binding;internal byte[] previous;}
    private readonly Dictionary<uint,RecordedCargo> _recordedCargo=new Dictionary<uint,RecordedCargo>();
    private readonly Dictionary<uint,double> _cargoBindingRetry=new Dictionary<uint,double>();
    private readonly HashSet<uint> _cargoCaptureWarnings=new HashSet<uint>();
    private void RecordCargoMounts(Aircraft aircraft)
    {
        uint id=aircraft.persistentID.Id;
        CargoMountStateEvent e=null;
        try
        {
            if(_recordedCargo.TryGetValue(id,out var old)&&!old.binding.IsValid)_recordedCargo.Remove(id);
            if(!_recordedCargo.TryGetValue(id,out var cargo))
            {
                if(_cargoBindingRetry.TryGetValue(id,out double retry)&&_virtualTime<retry)return;
                var binding=new ReplayCargoMounts(aircraft);
                if(binding.Count==0){_cargoBindingRetry[id]=_virtualTime+.25;return;} // loadout setup may complete on a later frame
                _cargoBindingRetry.Remove(id);
                _recordedCargo[id]=cargo=new RecordedCargo{binding=binding};
            }
            e=ReplayEventFactory.GetEvent<CargoMountStateEvent>();e.unitId=id;e.mounts=cargo.binding.Capture();
            e.Time=0;var encoded=RollingReplayBuffer.Encode(e);
            if(cargo.previous!=null&&cargo.previous.SequenceEqual(encoded)){ReplayEventFactory.Return(e);return;}
            cargo.previous=encoded;e.Time=_virtualTime;var submitted=e;e=null;QueueEvent(submitted);
        }
        catch(Exception error)
        {
            if(e!=null)ReplayEventFactory.Return(e);CaptureFailureCount++;
            _recordedCargo.Remove(id);_cargoBindingRetry[id]=_virtualTime+.25;
            if(_cargoCaptureWarnings.Add(id))Plugin.logger.LogWarning("Cargo mount capture unavailable for "+aircraft.definition?.jsonKey+": "+error);
        }
    }
    private void ForgetRecordingCargo(uint id){_recordedCargo.Remove(id);_cargoBindingRetry.Remove(id);_cargoCaptureWarnings.Remove(id);}
    private void ClearRecordingCargo(){_recordedCargo.Clear();_cargoBindingRetry.Clear();_cargoCaptureWarnings.Clear();}
}
