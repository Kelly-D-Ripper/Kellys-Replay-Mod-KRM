using System;
using System.Collections.Generic;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class Recorder
{
    private sealed class ObservedCountermeasure
    {
        internal uint id,unitId;
        internal Component source;
        internal Aircraft aircraft;
        internal ReplayCountermeasureAddress address;
        internal double birth;
        internal float initialAge;
        internal CountermeasureSnapshot last;
    }
    private readonly Dictionary<Component,ObservedCountermeasure> _recordedCountermeasures=new Dictionary<Component,ObservedCountermeasure>();
    private readonly List<double> _countermeasureTails=new List<double>();
    private readonly List<Component> _expiredCountermeasures=new List<Component>();
    private readonly HashSet<string> _countermeasureCaptureWarnings=new HashSet<string>();
    private uint _nextCountermeasureId;
    private double _lastCountermeasureCapture=double.NegativeInfinity;
    public void RecordFlareLaunch(IRFlare flare,Aircraft aircraft,Transform point)=>BeginCountermeasure(flare,aircraft,point,CountermeasureKind.Flare);
    public void RecordChaffLaunch(RadarChaff chaff,Aircraft aircraft,Transform point)=>BeginCountermeasure(chaff,aircraft,point,CountermeasureKind.Chaff);
    private void PruneCountermeasureTails()
    {_countermeasureTails.RemoveAll(expiry=>expiry<=_virtualTime);}
    private void CountermeasureCaptureWarning(string key,string message)
    {CaptureFailureCount++;if(_countermeasureCaptureWarnings.Count<1024&&_countermeasureCaptureWarnings.Add(key))Plugin.logger.LogWarning(message);}
    private void BeginCountermeasure(Component source,Aircraft aircraft,Transform point,CountermeasureKind kind)
    {
        if(!_acceptEvents||!SessionValid||source==null||!IsTrackedOrPending(aircraft)||_recordedCountermeasures.ContainsKey(source))return;
        PruneCountermeasureTails();
        if(_recordedCountermeasures.Count+_countermeasureTails.Count>=CountermeasureTimeline.MaxActive||_nextCountermeasureId==uint.MaxValue)
        {CountermeasureCaptureWarning("capacity","Countermeasure capture reached its bounded active/trail limit; excess launches are omitted and the recording continues.");return;}
        try
        {
            if(!ReplayCountermeasureIdentity.Capture(aircraft,point,kind,source,out var address))
            {CountermeasureCaptureWarning(aircraft.definition?.jsonKey+":"+kind,"Skipped a countermeasure whose native prefab could not be identified: "+aircraft.definition?.jsonKey);return;}
            float total=kind==CountermeasureKind.Flare?address.prefab.GetComponent<IRFlare>().burnTime:address.prefab.GetComponent<RadarChaff>().chaffTime;
            float remaining=Remaining(source);
            var tracked=new ObservedCountermeasure {id=++_nextCountermeasureId,unitId=aircraft.persistentID.Id,source=source,aircraft=aircraft,address=address,birth=_virtualTime,
                initialAge=point==null?Math.Max(0,total-Math.Max(0,remaining)):0};
            var value=CaptureCountermeasure(tracked,CountermeasurePhase.Launch);address.CopyTo(value);
            if(!value.HasValidPayload()){ReplayEventFactory.Return(value);CountermeasureCaptureWarning("invalid:"+address.unitKey,"Skipped invalid native countermeasure launch data.");return;}
            tracked.last=value.Snapshot;if(QueueEventForUnit(aircraft,value))_recordedCountermeasures.Add(source,tracked);
        }
        catch(Exception error){CountermeasureCaptureWarning("launch:"+aircraft.definition?.jsonKey,"Countermeasure capture failed safely: "+error.Message);}
    }
    private static float Remaining(Component source)=>source is IRFlare flare?flare.burnTime:source is RadarChaff chaff?chaff.chaffTime:0;
    private CountermeasureEvent CaptureCountermeasure(ObservedCountermeasure tracked,CountermeasurePhase phase)
    {
        var value=ReplayEventFactory.GetEvent<CountermeasureEvent>();
        try
        {
            value.countermeasureId=tracked.id;value.unitId=tracked.unitId;value.Time=_virtualTime;value.phase=phase;
            value.tailSeconds=tracked.address.kind==CountermeasureKind.Flare?CountermeasureTimeline.TrailSeconds:1;
            value.age=(float)Math.Max(0,_virtualTime-tracked.birth)+tracked.initialAge;
            if(tracked.source!=null)
            {
                value.position=tracked.source.transform.GlobalPosition();value.rotation=tracked.source.transform.rotation;
                value.velocity=tracked.source is IRFlare flare?flare.velocity:((RadarChaff)tracked.source).velocity;
                value.remaining=Math.Max(0,Remaining(tracked.source));
            }
            else{value.position=tracked.last.position;value.rotation=tracked.last.rotation;value.velocity=tracked.last.velocity;value.remaining=0;}
            if(phase==CountermeasurePhase.End){value.remaining=0;value.endAge=value.age;}
            return value;
        }
        catch{ReplayEventFactory.Return(value);throw;}
    }
    public void RecordCountermeasureEnd(Component source)
    {
        if(!_acceptEvents||!SessionValid||ReferenceEquals(source,null)||!_recordedCountermeasures.TryGetValue(source,out var tracked))return;
        try
        {
            var value=CaptureCountermeasure(tracked,CountermeasurePhase.End);
            if(!value.HasValidPayload())
            {
                // Keep the last finite observed pose if the native object was
                // destroyed/corrupted before its final sample was collected.
                ReplayEventFactory.Return(value);value=ReplayEventFactory.GetEvent<CountermeasureEvent>();
                value.countermeasureId=tracked.id;value.phase=CountermeasurePhase.End;value.SetSnapshot(tracked.last);
                value.age+=(float)Math.Max(0,_virtualTime-tracked.last.time);value.Time=_virtualTime;value.remaining=0;value.endAge=value.age;
                value.tailSeconds=tracked.address.kind==CountermeasureKind.Flare?CountermeasureTimeline.TrailSeconds:1;
                CountermeasureCaptureWarning("end","Used the last valid countermeasure pose after invalid native end data.");
            }
            if(value.HasValidPayload())QueueEventForUnit(tracked.aircraft,value);else ReplayEventFactory.Return(value);
        }
        catch(Exception error){CountermeasureCaptureWarning("end-exception","Countermeasure end capture failed safely: "+error.Message);}
        finally
        {
            float tail=tracked.address.kind==CountermeasureKind.Flare?CountermeasureTimeline.TrailSeconds:1;
            _countermeasureTails.Add(Math.Max(_virtualTime+tail,tracked.last.time+tracked.last.remaining+tail));
            _recordedCountermeasures.Remove(source);
        }
    }
    private void CaptureCountermeasuresLate()
    {
        PruneCountermeasureTails();
        if(_virtualTime-_lastCountermeasureCapture<ReplayManager.UpdateInputsInterval)return;
        _lastCountermeasureCapture=_virtualTime;_expiredCountermeasures.Clear();
        foreach(var pair in _recordedCountermeasures)
        {
            try
            {
                if(pair.Key==null||Remaining(pair.Key)<=0){_expiredCountermeasures.Add(pair.Key);continue;}
                var value=CaptureCountermeasure(pair.Value,CountermeasurePhase.Sample);
                if(!value.HasValidPayload()){ReplayEventFactory.Return(value);CountermeasureCaptureWarning("sample","Skipped invalid native countermeasure trajectory data.");_expiredCountermeasures.Add(pair.Key);continue;}
                pair.Value.last=value.Snapshot;QueueEventForUnit(pair.Value.aircraft,value);
            }
            catch(Exception error){CountermeasureCaptureWarning("sample-exception","Countermeasure trajectory capture failed safely: "+error.Message);_expiredCountermeasures.Add(pair.Key);}
        }
        foreach(var source in _expiredCountermeasures)RecordCountermeasureEnd(source);
    }
    private void CaptureExistingCountermeasures()
    {
        // One bounded discovery at recording start; ongoing capture observes the
        // actual local launch on both host and client, with no authority filter.
        foreach(var flare in UnityEngine.Object.FindObjectsOfType<IRFlare>())if(flare!=null&&flare.burnTime>0)BeginCountermeasure(flare,flare.aircraft,null,CountermeasureKind.Flare);
        foreach(var chaff in UnityEngine.Object.FindObjectsOfType<RadarChaff>())if(chaff!=null&&chaff.chaffTime>0)BeginCountermeasure(chaff,chaff.aircraft,null,CountermeasureKind.Chaff);
    }
    private void ForgetPendingCountermeasures(Unit unit)
    {
        if(ReferenceEquals(unit,null))return;
        var remove=new List<Component>();
        foreach(var pair in _recordedCountermeasures)if(ReferenceEquals(pair.Value.aircraft,unit))remove.Add(pair.Key);
        foreach(var source in remove)_recordedCountermeasures.Remove(source);
        if(unit is Aircraft aircraft)ReplayCountermeasureIdentity.Forget(aircraft);
    }
    private void ClearRecordingCountermeasures()
    {_recordedCountermeasures.Clear();_countermeasureTails.Clear();_expiredCountermeasures.Clear();_countermeasureCaptureWarnings.Clear();_nextCountermeasureId=0;_lastCountermeasureCapture=double.NegativeInfinity;ReplayCountermeasureIdentity.Clear();}
}
