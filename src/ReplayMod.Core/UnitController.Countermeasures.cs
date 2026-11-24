using System;
using System.Collections.Generic;
using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class UnitController
{
    private sealed class PlayedCountermeasure
    {internal CountermeasureTimeline timeline;internal ReplayCountermeasureVisual visual;internal bool unavailable;}
    private readonly Dictionary<uint,PlayedCountermeasure> _countermeasures=new Dictionary<uint,PlayedCountermeasure>();
    private readonly Dictionary<string,GameObject> _countermeasurePrefabs=new Dictionary<string,GameObject>();
    private readonly HashSet<string> _countermeasurePlaybackWarnings=new HashSet<string>();
    private readonly List<uint> _expiredPlaybackCountermeasures=new List<uint>();
    public void Execute(CountermeasureEvent value)
    {
        if(!value.HasValidPayload())return;
        if(value.phase==CountermeasurePhase.Launch)
        {
            ExpireReplayCountermeasures(value.Time);
            if(_countermeasures.TryGetValue(value.countermeasureId,out var previous)){previous.visual?.Dispose();_countermeasures.Remove(value.countermeasureId);}
            if(_countermeasures.Count>=CountermeasureTimeline.MaxActive)
            {CountermeasurePlaybackWarning("capacity","Replay countermeasure limit reached; extra visual omitted.");return;}
            _countermeasures[value.countermeasureId]=new PlayedCountermeasure {timeline=new CountermeasureTimeline(value,true)};
        }
        else if(_countermeasures.TryGetValue(value.countermeasureId,out var current))current.timeline.Add(value);
    }
    private void CountermeasurePlaybackWarning(string key,string message)
    {if(_countermeasurePlaybackWarnings.Count<1024&&_countermeasurePlaybackWarnings.Add(key))Plugin.logger.LogWarning(message);}
    private void UpdateReplayCountermeasures(double time)
    {
        _expiredPlaybackCountermeasures.Clear();
        foreach(var pair in _countermeasures)
        {
            var current=pair.Value;
            if(time>=current.timeline.ExpiresAt){current.visual?.Dispose();_expiredPlaybackCountermeasures.Add(pair.Key);continue;}
            if(!current.timeline.TryAt(time,out _)||current.unavailable)continue;
            try
            {
                if(current.visual==null)
                {
                    var metadata=current.timeline.metadata;string key=metadata.unitKey+":"+(byte)metadata.kind+":"+metadata.prefabSlot+":"+metadata.prefabName;
                    if(!_countermeasurePrefabs.TryGetValue(key,out var prefab))
                    {prefab=ReplayCountermeasureIdentity.Resolve(metadata);if(prefab!=null&&_countermeasurePrefabs.Count<1024)_countermeasurePrefabs[key]=prefab;}
                    if(prefab==null)
                    {current.unavailable=true;CountermeasurePlaybackWarning(key,"Replay countermeasure prefab unavailable or changed: "+metadata.unitKey+" / "+metadata.prefabName);continue;}
                    current.visual=new ReplayCountermeasureVisual(prefab,current.timeline);
                }
                current.visual.Update(current.timeline,time);
            }
            catch(Exception error)
            {current.visual?.Dispose();current.visual=null;current.unavailable=true;CountermeasurePlaybackWarning("visual:"+current.timeline.metadata.unitKey,"Replay countermeasure presentation failed safely: "+error.Message);}
        }
        foreach(uint id in _expiredPlaybackCountermeasures)_countermeasures.Remove(id);
    }
    private void ExpireReplayCountermeasures(double time)
    {
        _expiredPlaybackCountermeasures.Clear();
        foreach(var pair in _countermeasures)
            if(pair.Value.timeline.ExpiresAt<=time){pair.Value.visual?.Dispose();_expiredPlaybackCountermeasures.Add(pair.Key);}
        foreach(uint id in _expiredPlaybackCountermeasures)_countermeasures.Remove(id);
    }
    private void ClearReplayCountermeasures()
    {
        foreach(var current in _countermeasures.Values)current.visual?.Dispose();
        _countermeasures.Clear();_countermeasurePrefabs.Clear();_countermeasurePlaybackWarnings.Clear();_expiredPlaybackCountermeasures.Clear();
    }
}
