using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class Recorder
{
    private sealed class FuelFlameUnit
    {internal Unit unit;internal double birth;internal FuelTank[] tanks;internal FuelTank[] prefabTanks;}
    private sealed class ObservedFuelFlame
    {
        internal uint id,unitId;internal byte partId;internal double ownerBirth,born,observed,published;
        internal ushort slot;internal string unitKey,prefabName;internal FuelTank tank;internal UnitPart part;internal Unit owner;
        internal GameObject source;internal DamageParticles damage;internal float initialAge;internal FuelFlameSnapshot last;
    }
    private readonly Dictionary<uint,FuelFlameUnit> _fuelFlameUnits=new Dictionary<uint,FuelFlameUnit>();
    private readonly Dictionary<uint,ObservedFuelFlame> _recordedFuelFlames=new Dictionary<uint,ObservedFuelFlame>();
    private readonly Dictionary<FuelTank,uint> _fuelFlamesByTank=new Dictionary<FuelTank,uint>();
    private readonly HashSet<GameObject> _invalidFuelFlames=new HashSet<GameObject>();
    private readonly HashSet<string> _fuelFlameWarnings=new HashSet<string>();
    private readonly List<uint> _endedFuelFlames=new List<uint>();
    private uint _nextFuelFlameId;private double _lastFuelFlameCapture=double.NegativeInfinity;
    private void EnsureFuelFlameUnit(Unit unit)
    {
        if(!(unit is Aircraft)||unit.definition?.unitPrefab==null||_fuelFlameUnits.ContainsKey(unit.persistentID.Id))return;
        var found=new HashSet<FuelTank>();
        void Include(GameObject root){foreach(var tank in root.GetComponentsInChildren<FuelTank>(true))if(tank!=null)found.Add(tank);}
        if(unit.partLookup!=null)foreach(var part in unit.partLookup)if(part!=null)Include(part.gameObject);Include(unit.gameObject);
        _fuelFlameUnits[unit.persistentID.Id]=new FuelFlameUnit {unit=unit,birth=_virtualTime,tanks=found.ToArray(),prefabTanks=unit.definition.unitPrefab.GetComponentsInChildren<FuelTank>(true)};
    }
    private void FuelFlameWarning(string key,string message)
    {CaptureFailureCount++;if(_fuelFlameWarnings.Count<512&&_fuelFlameWarnings.Add(key))Plugin.logger.LogWarning(message);}
    private void BeginFuelFlame(FuelFlameUnit unit,FuelTank tank,GameObject source)
    {
        if(_invalidFuelFlames.Contains(source)||_recordedFuelFlames.Count>=FuelFlameTimeline.MaxActive||_nextFuelFlameId==uint.MaxValue)
        {if(_recordedFuelFlames.Count>=FuelFlameTimeline.MaxActive)FuelFlameWarning("capacity","Recorded fuel-fire limit reached; excess effects are omitted and recording continues.");return;}
        try
        {
            var damage=source.GetComponent<DamageParticles>();if(damage==null||tank.part==null)throw new InvalidOperationException("Missing native fuel fire binding");
            int slot=Array.FindIndex(unit.prefabTanks,candidate=>candidate!=null&&candidate.fireEffect==tank.fireEffect);
            if(slot<0||slot>=1024)throw new InvalidOperationException("Native fuel fire prefab is not part of recorded aircraft definition");
            var tracked=new ObservedFuelFlame {id=++_nextFuelFlameId,unitId=unit.unit.persistentID.Id,ownerBirth=unit.birth,partId=tank.part.id,part=tank.part,owner=unit.unit,tank=tank,source=source,damage=damage,
                born=_virtualTime,initialAge=Math.Max(0,damage.time),slot=(ushort)slot,unitKey=unit.unit.definition.jsonKey,prefabName=tank.fireEffect.name};
            var value=CaptureFuelFlame(tracked,FuelFlamePhase.Begin);value.unitId=tracked.unitId;value.partId=tracked.partId;value.ownerBirth=tracked.ownerBirth;value.prefabSlot=tracked.slot;value.unitKey=tracked.unitKey;value.prefabName=tracked.prefabName;
            if(!value.HasValidPayload()){ReplayEventFactory.Return(value);throw new InvalidOperationException("Invalid observed native fuel fire");}
            tracked.last=value.snapshot.Copy();tracked.observed=tracked.published=_virtualTime;
            _recordedFuelFlames.Add(tracked.id,tracked);_fuelFlamesByTank[tank]=tracked.id;QueueEvent(value);
        }
        catch(Exception error)
        {if(_invalidFuelFlames.Count<FuelFlameTimeline.MaxActive)_invalidFuelFlames.Add(source);FuelFlameWarning("begin:"+unit.unit.definition?.jsonKey,"Fuel fire capture unavailable: "+error.Message);}
    }
    private FuelFlameStateEvent CaptureFuelFlame(ObservedFuelFlame tracked,FuelFlamePhase phase)
    {
        var value=ReplayEventFactory.GetEvent<FuelFlameStateEvent>();
        try
        {
            value.flameId=tracked.id;value.phase=phase;value.Time=_virtualTime;
            if(phase==FuelFlamePhase.End)return value;
            var transform=tracked.source.transform;var damage=tracked.damage;var systems=damage.systemBehaviours;
            if(systems==null||systems.Length>FuelFlameTimeline.MaxSystems)throw new InvalidOperationException("Fuel fire particle layout exceeds limit");
            bool attached=tracked.part!=null&&tracked.owner!=null&&IsTracked(tracked.owner)&&transform.IsChildOf(tracked.part.transform);
            var snapshot=new FuelFlameSnapshot {time=_virtualTime,attached=attached,position=transform.GlobalPosition(),rotation=transform.rotation,
                localPosition=attached?tracked.part.transform.InverseTransformPoint(transform.position):Vector3.zero,
                localRotation=attached?Quaternion.Inverse(tracked.part.transform.rotation)*transform.rotation:Quaternion.identity,
                age=tracked.initialAge+(float)Math.Max(0,_virtualTime-tracked.born),speed=Math.Max(0,damage.speed),lightIntensity=damage.fireLight!=null&&damage.fireLight.enabled?Math.Max(0,damage.fireLight.intensity):0,
                blendSeconds=phase==FuelFlamePhase.Begin?0:(float)Math.Max(0,Math.Min(3600,_virtualTime-tracked.observed)),opacity=new float[systems.Length]};
            for(int i=0;i<systems.Length;i++)
            {var behavior=systems[i];snapshot.opacity[i]=behavior.fadeOpacity;if(behavior.system!=null&&behavior.system.isEmitting&&behavior.system.gameObject.activeInHierarchy)snapshot.emittingMask|=1u<<i;}
            value.snapshot=snapshot;return value;
        }
        catch{ReplayEventFactory.Return(value);throw;}
    }
    private void EndFuelFlame(uint id)
    {
        if(!_recordedFuelFlames.TryGetValue(id,out var tracked))return;
        var value=ReplayEventFactory.GetEvent<FuelFlameStateEvent>();value.flameId=id;value.phase=FuelFlamePhase.End;value.Time=_virtualTime;QueueEvent(value);
        _recordedFuelFlames.Remove(id);if(!ReferenceEquals(tracked.tank,null)&&_fuelFlamesByTank.TryGetValue(tracked.tank,out uint current)&&current==id)_fuelFlamesByTank.Remove(tracked.tank);
    }
    private void CaptureFuelFlamesLate()
    {
        if(_virtualTime-_lastFuelFlameCapture<ReplayManager.UpdateInputsInterval)return;_lastFuelFlameCapture=_virtualTime;
        _endedFuelFlames.Clear();
        foreach(var pair in _recordedFuelFlames)if(pair.Value.source==null||pair.Value.damage==null||!pair.Value.source.activeInHierarchy)_endedFuelFlames.Add(pair.Key);
        foreach(uint id in _endedFuelFlames)EndFuelFlame(id);
        _invalidFuelFlames.RemoveWhere(source=>source==null);
        foreach(var unit in _fuelFlameUnits.Values)
        {
            if(unit.unit==null)continue;
            foreach(var tank in unit.tanks)
            {
                if(tank==null||tank.fireEffectSpawn==null||!tank.fireEffectSpawn.activeInHierarchy)continue;
                if(_fuelFlamesByTank.TryGetValue(tank,out uint existing)&&_recordedFuelFlames.TryGetValue(existing,out var tracked))
                {if(tracked.source==tank.fireEffectSpawn)continue;EndFuelFlame(existing);}
                BeginFuelFlame(unit,tank,tank.fireEffectSpawn);
            }
        }
        _endedFuelFlames.Clear();
        foreach(var pair in _recordedFuelFlames)
        {
            FuelFlameStateEvent value=null;
            try
            {
                var tracked=pair.Value;value=CaptureFuelFlame(tracked,FuelFlamePhase.Sample);
                if(!value.HasValidPayload())throw new InvalidOperationException("Invalid native fire presentation state");
                tracked.observed=_virtualTime;
                if(!FuelFlameTimeline.Changed(tracked.last,value.snapshot)&&_virtualTime-tracked.published<1){ReplayEventFactory.Return(value);value=null;continue;}
                tracked.last=value.snapshot.Copy();tracked.published=_virtualTime;var queued=value;value=null;QueueEvent(queued);
            }
            catch(Exception error)
            {if(value!=null)ReplayEventFactory.Return(value);_endedFuelFlames.Add(pair.Key);if(_invalidFuelFlames.Count<FuelFlameTimeline.MaxActive)_invalidFuelFlames.Add(pair.Value.source);FuelFlameWarning("sample","Fuel fire presentation capture failed safely: "+error.Message);}
        }
        foreach(uint id in _endedFuelFlames)EndFuelFlame(id);
    }
    private void ForgetFuelFlameUnit(uint id)=>_fuelFlameUnits.Remove(id);
    private void ClearRecordingFuelFlames()
    {_fuelFlameUnits.Clear();_recordedFuelFlames.Clear();_fuelFlamesByTank.Clear();_invalidFuelFlames.Clear();_fuelFlameWarnings.Clear();_endedFuelFlames.Clear();_nextFuelFlameId=0;_lastFuelFlameCapture=double.NegativeInfinity;}
}
