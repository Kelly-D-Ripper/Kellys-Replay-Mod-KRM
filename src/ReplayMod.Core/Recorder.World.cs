using System;
using System.Collections.Generic;
using System.Text;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class Recorder
{
    private sealed class RecordedHangar
    {
        public Hangar hangar;
        public string path;
        public HangarDoorSnapshot published;
        public double observedTime;
        public bool captureFailed;
    }
    private readonly Dictionary<uint,List<RecordedHangar>> _recordedHangars=new Dictionary<uint,List<RecordedHangar>>();
    private int _recordedHangarCount;
    private double _lastDoorCapture=-1,_lastEnvironmentCapture=-1;
    private readonly HashSet<uint> _worldActorCaptureErrors=new HashSet<uint>();
    private LevelInfo _worldCaptureLevel;
    private int _worldEnvironmentCaptureErrors;
    private double _nextEnvironmentRetry;
    private void RecordWorldActor(Unit unit)
    {
        RecordBuildingActor(unit);
        if(unit==null || _recordedHangars.ContainsKey(unit.persistentID.Id))return;
        Hangar[] hangars;
        try {hangars=unit.GetComponentsInChildren<Hangar>(true);}
        catch(Exception error)
        {WarnWorldActor(unit.persistentID.Id,error);return;}
        var tracked=new List<RecordedHangar>();
        foreach(var hangar in hangars)
        {
            if(_recordedHangarCount>=ReplayWorldPlayback.MaximumHangars || hangar==null || !ReferenceEquals(hangar.attachedUnit,unit))continue;
            string path;
            try
            {
                path=ReplayWorldPlayback.HangarPath(unit,hangar);
                if(string.IsNullOrEmpty(path)||Encoding.UTF8.GetByteCount(path)>WorldStateSnapshot.MaxPathBytes)throw new InvalidOperationException("Unsupported hangar hierarchy");
            }
            catch(Exception error){WarnWorldActor(unit.persistentID.Id,error);continue;}
            var binding=new RecordedHangar {hangar=hangar,path=path,observedTime=_virtualTime};
            tracked.Add(binding);_recordedHangarCount++;RecordHangar(unit.persistentID.Id,binding);
        }
        if(tracked.Count>0)_recordedHangars.Add(unit.persistentID.Id,tracked);
    }
    private void RecordHangar(uint id,RecordedHangar binding)
    {
        if(binding.hangar==null||binding.captureFailed)return;
        HangarDoorSnapshot snapshot;
        try {snapshot=ReplayWorldPlayback.CaptureDoor(binding.hangar,_virtualTime);}
        catch(Exception error){binding.captureFailed=true;WarnWorldActor(id,error);return;}
        if(snapshot.Changed(binding.published))
        {
            snapshot.blendSeconds=binding.published==null?0:(float)System.Math.Max(0,System.Math.Min(3600,_virtualTime-binding.observedTime));
            var e=ReplayEventFactory.GetEvent<HangarDoorStateEvent>();e.unitId=id;e.hangarPath=binding.path;snapshot.CopyTo(e);
            if(HangarDoorSnapshot.Valid(e)){QueueEvent(e);binding.published=snapshot;}
            else {ReplayEventFactory.Return(e);binding.captureFailed=true;WarnWorldActor(id,new InvalidOperationException("Invalid hangar motion state"));}
        }
        binding.observedTime=_virtualTime;
    }
    private void RecordWorldState()
    {
        if(!_acceptEvents || !SessionValid)return;
        RecordBuildingStates();
        if(_lastDoorCapture<0 || _virtualTime-_lastDoorCapture>=.05)
        {
            _lastDoorCapture=_virtualTime;
            foreach(var pair in _recordedHangars)
                foreach(var binding in pair.Value)
                    if(binding.hangar!=null && IsTracked(binding.hangar.attachedUnit) && binding.hangar.attachedUnit.persistentID.Id==pair.Key)
                        RecordHangar(pair.Key,binding);
        }
        if(_lastEnvironmentCapture>=0 && _virtualTime-_lastEnvironmentCapture<.25)return;
        var level=NetworkSceneSingleton<LevelInfo>.i;if(level==null)return;
        if(!ReferenceEquals(level,_worldCaptureLevel)){_worldCaptureLevel=level;_worldEnvironmentCaptureErrors=0;_nextEnvironmentRetry=0;}
        if(_worldEnvironmentCaptureErrors>=3||_virtualTime<_nextEnvironmentRetry)return;
        WorldStateSnapshot environment;
        try
        {
            environment=ReplayWorldPlayback.CaptureEnvironment(level,_virtualTime);
            if(!environment.Valid())throw new InvalidOperationException("Invalid environment state");
        }
        catch(Exception error)
        {
            _worldEnvironmentCaptureErrors++;_nextEnvironmentRetry=_virtualTime+1;
            if(_worldEnvironmentCaptureErrors==1||_worldEnvironmentCaptureErrors==3)
                Plugin.logger.LogWarning("Replay environment capture unavailable"+(_worldEnvironmentCaptureErrors==3?"; stopped retrying for this scene":"; retrying")+": "+error.Message);
            return;
        }
        var state=ReplayEventFactory.GetEvent<WorldEnvironmentStateEvent>();environment.CopyTo(state);QueueEvent(state);_lastEnvironmentCapture=_virtualTime;
    }
    private void ForgetRecordingWorldActor(Unit unit)
    {
        if(unit==null)return;
        ForgetRecordingBuilding(unit);
        if(_recordedHangars.TryGetValue(unit.persistentID.Id,out var bindings))_recordedHangarCount-=bindings.Count;
        _recordedHangars.Remove(unit.persistentID.Id);
        _worldActorCaptureErrors.Remove(unit.persistentID.Id);
    }
    private void ClearRecordingWorld()
    {ClearRecordingBuildings();_recordedHangars.Clear();_worldActorCaptureErrors.Clear();_recordedHangarCount=0;_lastDoorCapture=_lastEnvironmentCapture=-1;_worldCaptureLevel=null;_worldEnvironmentCaptureErrors=0;_nextEnvironmentRetry=0;}
    private void WarnWorldActor(uint id,Exception error)
    {
        if(_worldActorCaptureErrors.Count<ReplayWorldPlayback.MaximumHangars && _worldActorCaptureErrors.Add(id))
            Plugin.logger.LogWarning("Replay hangar capture unavailable for actor "+id+": "+error.Message);
    }
}
