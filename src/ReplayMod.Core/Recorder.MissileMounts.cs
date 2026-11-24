using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class Recorder
{
    private sealed class RecordedMissileMounts {internal ReplayMissileMounts binding;internal byte[] previous;}
    private readonly Dictionary<uint,RecordedMissileMounts> _recordedMissileMounts=new Dictionary<uint,RecordedMissileMounts>();
    private readonly Dictionary<uint,double> _missileMountBindingRetry=new Dictionary<uint,double>();
    private readonly HashSet<uint> _missileMountCaptureWarnings=new HashSet<uint>();
    // Sampled with aircraft visuals and written only on change, so a rail slide or
    // bay drop is captured while it moves and idle stores cost nothing.
    private void RecordMissileMounts(Aircraft aircraft)
    {
        uint id=aircraft.persistentID.Id;
        MissileMountStateEvent e=null;
        try
        {
            if(_recordedMissileMounts.TryGetValue(id,out var old)&&!old.binding.IsValid)_recordedMissileMounts.Remove(id);
            if(!_recordedMissileMounts.TryGetValue(id,out var stores))
            {
                if(_missileMountBindingRetry.TryGetValue(id,out double retry)&&_virtualTime<retry)return;
                var binding=new ReplayMissileMounts(aircraft);
                if(binding.Count==0){_missileMountBindingRetry[id]=_virtualTime+.25;return;} // loadout setup may complete on a later frame
                _missileMountBindingRetry.Remove(id);
                _recordedMissileMounts[id]=stores=new RecordedMissileMounts{binding=binding};
            }
            e=ReplayEventFactory.GetEvent<MissileMountStateEvent>();e.unitId=id;e.mounts=stores.binding.Capture();
            e.Time=0;var encoded=RollingReplayBuffer.Encode(e);
            if(stores.previous!=null&&stores.previous.SequenceEqual(encoded)){ReplayEventFactory.Return(e);return;}
            stores.previous=encoded;e.Time=_virtualTime;var submitted=e;e=null;QueueEvent(submitted);
        }
        catch(Exception error)
        {
            if(e!=null)ReplayEventFactory.Return(e);CaptureFailureCount++;
            _recordedMissileMounts.Remove(id);_missileMountBindingRetry[id]=_virtualTime+.25;
            if(_missileMountCaptureWarnings.Add(id))Plugin.logger.LogWarning("Missile mount capture unavailable for "+aircraft.definition?.jsonKey+": "+error);
        }
    }
    private void ForgetRecordingMissileMounts(uint id){_recordedMissileMounts.Remove(id);_missileMountBindingRetry.Remove(id);_missileMountCaptureWarnings.Remove(id);}
    private void ClearRecordingMissileMounts(){_recordedMissileMounts.Clear();_missileMountBindingRetry.Clear();_missileMountCaptureWarnings.Clear();}
}
