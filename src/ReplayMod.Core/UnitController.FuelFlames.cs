using System;
using System.Collections.Generic;
using ReplayMod.Events.ConcreteEvents;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Core;

public partial class UnitController
{
    private sealed class PlayedFuelFlame {internal FuelFlameTimeline timeline;internal ReplayFuelFlameVisual visual;internal bool unavailable;internal Unit owner;}
    private readonly Dictionary<uint,PlayedFuelFlame> _fuelFlames=new Dictionary<uint,PlayedFuelFlame>();
    private readonly List<uint> _endedPlaybackFuelFlames=new List<uint>();
    private readonly HashSet<string> _fuelFlamePlaybackWarnings=new HashSet<string>();
    public void Execute(FuelFlameStateEvent value)
    {
        if(!value.HasValidPayload())return;
        if(value.phase==FuelFlamePhase.Begin)
        {
            if(_fuelFlames.TryGetValue(value.flameId,out var previous)){previous.visual?.Dispose();_fuelFlames.Remove(value.flameId);}
            if(_fuelFlames.Count>=FuelFlameTimeline.MaxActive)return;
            Unit owner=null;
            if(value.snapshot.attached&&_spawnTimes.TryGetValue(value.unitId,out double birth)&&Math.Abs(birth-Math.Max(0,value.ownerBirth))<.00001&&TryGetUnit(value.unitId,out _,out var actor))owner=actor;
            _fuelFlames[value.flameId]=new PlayedFuelFlame {timeline=new FuelFlameTimeline(value),owner=owner};
        }
        else if(_fuelFlames.TryGetValue(value.flameId,out var current))
        {
            current.timeline.Add(value);
            if(value.phase==FuelFlamePhase.End){current.visual?.Dispose();_fuelFlames.Remove(value.flameId);}
        }
    }
    private static GameObject ResolveFuelFlamePrefab(FuelFlameStateEvent value)
    {
        if(!Encyclopedia.Lookup.TryGetValue(value.unitKey,out var definition)||definition?.unitPrefab==null)return null;
        var candidates=definition.unitPrefab.GetComponentsInChildren<FuelTank>(true);if(value.prefabSlot>=candidates.Length)return null;
        var prefab=candidates[value.prefabSlot].fireEffect;return prefab!=null&&StringComparer.Ordinal.Equals(prefab.name,value.prefabName)?prefab:null;
    }
    public void RefreshFuelFlames(double time)
    {
        foreach(var pair in _fuelFlames)
        {
            var current=pair.Value;if(current.unavailable||!current.timeline.TryAt(time,out var state))continue;
            try
            {
                var metadata=current.timeline.metadata;
                if(current.visual==null)
                {var prefab=ResolveFuelFlamePrefab(metadata);if(prefab==null)throw new InvalidOperationException("Native fire prefab absent or changed");current.visual=new ReplayFuelFlameVisual(prefab,state,pair.Key);}
                Transform anchor=null;
                if(state.attached&&current.owner!=null&&_spawnTimes.TryGetValue(metadata.unitId,out double birth)&&Math.Abs(birth-Math.Max(0,metadata.ownerBirth))<.00001&&TryGetUnit(metadata.unitId,out _,out var actor)&&ReferenceEquals(actor,current.owner))
                    anchor=FindRecordedAircraftPart(actor,metadata.partId)?.transform;
                current.visual.Apply(state,anchor);
            }
            catch(Exception error)
            {current.visual?.Dispose();current.visual=null;current.unavailable=true;string key=current.timeline.metadata.unitKey+":"+current.timeline.metadata.prefabName;if(_fuelFlamePlaybackWarnings.Count<512&&_fuelFlamePlaybackWarnings.Add(key))Plugin.logger.LogWarning("Recorded fuel fire unavailable: "+key+" / "+error.Message);}
        }
    }
    private void ClearFuelFlames()
    {foreach(var current in _fuelFlames.Values)current.visual?.Dispose();_fuelFlames.Clear();_endedPlaybackFuelFlames.Clear();_fuelFlamePlaybackWarnings.Clear();}
}
