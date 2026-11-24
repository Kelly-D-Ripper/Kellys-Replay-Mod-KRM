using System;
using System.Collections.Generic;
using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

internal sealed class ReplayCountermeasureAddress
{
    internal CountermeasureKind kind;
    internal ushort slot;
    internal string unitKey,prefabName;
    internal GameObject prefab;
    internal void CopyTo(CountermeasureEvent value)
    {value.kind=kind;value.prefabSlot=slot;value.unitKey=unitKey;value.prefabName=prefabName;}
}

internal static class ReplayCountermeasureIdentity
{
    private sealed class Binding {internal Transform point;internal ReplayCountermeasureAddress address;}
    private static readonly Dictionary<Aircraft,List<Binding>> captured=new Dictionary<Aircraft,List<Binding>>();
    private static GameObject[] Prefabs(GameObject root,CountermeasureKind kind)
    {
        if(root==null)return Array.Empty<GameObject>();
        if(kind==CountermeasureKind.Flare)
        {var ejectors=root.GetComponentsInChildren<FlareEjector>(true);var result=new GameObject[ejectors.Length];for(int i=0;i<result.Length;i++)result[i]=ejectors[i].flarePrefab;return result;}
        else
        {var ejectors=root.GetComponentsInChildren<ChaffEjector>(true);var result=new GameObject[ejectors.Length];for(int i=0;i<result.Length;i++)result[i]=ejectors[i].chaffPrefab;return result;}
    }
    private static List<Binding> Bind(Aircraft aircraft)
    {
        if(captured.TryGetValue(aircraft,out var existing))return existing;
        var result=new List<Binding>();var seen=new HashSet<Countermeasure>();
        var flares=Prefabs(aircraft.definition?.unitPrefab,CountermeasureKind.Flare);var chaff=Prefabs(aircraft.definition?.unitPrefab,CountermeasureKind.Chaff);
        void Include(GameObject root)
        {
            if(root==null)return;
            foreach(var ejector in root.GetComponentsInChildren<Countermeasure>(true))
            {
                if(ejector==null||ejector.aircraft!=aircraft||!seen.Add(ejector))continue;
                var kind=ejector is FlareEjector?CountermeasureKind.Flare:CountermeasureKind.Chaff;
                GameObject prefab=ejector is FlareEjector flare?flare.flarePrefab:ejector is ChaffEjector chaffEjector?chaffEjector.chaffPrefab:null;
                if(prefab==null)continue;
                int slot=Array.IndexOf(kind==CountermeasureKind.Flare?flares:chaff,prefab);
                if(slot<0||slot>=CountermeasureTimeline.MaxPrefabSlots)continue;
                var address=new ReplayCountermeasureAddress {kind=kind,slot=(ushort)slot,unitKey=aircraft.definition.jsonKey,prefabName=prefab.name,prefab=prefab};
                if(ejector is FlareEjector ir&&ir.ejectionPoints!=null)
                {foreach(var point in ir.ejectionPoints)if(point?.transform!=null)result.Add(new Binding {point=point.transform,address=address});}
                else if(ejector is ChaffEjector radar&&radar.ejectionPoints!=null)
                {foreach(var point in radar.ejectionPoints)if(point?.transform!=null)result.Add(new Binding {point=point.transform,address=address});}
            }
        }
        // Complex-physics aircraft detach their native parts from the root.
        if(aircraft.partLookup!=null)foreach(var part in aircraft.partLookup)if(part!=null)Include(part.gameObject);
        Include(aircraft.gameObject);captured[aircraft]=result;return result;
    }
    internal static bool Capture(Aircraft aircraft,Transform point,CountermeasureKind kind,Component instance,out ReplayCountermeasureAddress address)
    {
        address=null;if(aircraft==null||aircraft.definition==null||instance==null)return false;
        var bindings=Bind(aircraft);
        foreach(var binding in bindings)if(binding.address.kind==kind&&point!=null&&binding.point==point){address=binding.address;return true;}
        if(point!=null)return false;
        // Recording may start while a native flare is already in flight. The
        // local spawner retains its prefab name; do not choose an arbitrary FX.
        string name=instance.gameObject.name;
        foreach(var binding in bindings)
        {
            if(binding.address.kind!=kind||!(name==binding.address.prefabName||name==binding.address.prefabName+"(Clone)"))continue;
            if(address!=null&&address.prefab!=binding.address.prefab){address=null;return false;}
            address=binding.address;
        }
        return address!=null;
    }
    internal static GameObject Resolve(CountermeasureEvent value)
    {
        if(!Encyclopedia.Lookup.TryGetValue(value.unitKey,out var definition)||definition?.unitPrefab==null)return null;
        var candidates=Prefabs(definition.unitPrefab,value.kind);
        if(value.prefabSlot>=candidates.Length)return null;
        var prefab=candidates[value.prefabSlot];
        if(prefab==null||!StringComparer.Ordinal.Equals(prefab.name,value.prefabName))return null;
        if(value.kind==CountermeasureKind.Flare&&prefab.GetComponent<IRFlare>()==null)return null;
        if(value.kind==CountermeasureKind.Chaff&&prefab.GetComponent<RadarChaff>()==null)return null;
        return prefab;
    }
    internal static void Forget(Aircraft aircraft){if(!ReferenceEquals(aircraft,null))captured.Remove(aircraft);}
    internal static void Clear()=>captured.Clear();
}
