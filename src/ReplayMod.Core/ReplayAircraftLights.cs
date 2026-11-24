using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

internal sealed class ReplayAircraftLights
{
    internal sealed class Lamp
    {
        internal string key;
        internal NavLights.NavLight native;
        internal bool observed;
        internal AircraftLampControl control;
        internal bool usable;
        internal bool recorded;
    }
    internal readonly Aircraft aircraft;
    internal readonly Lamp[] lamps;
    private readonly Dictionary<string,Lamp> lookup;
    internal ReplayAircraftLights(Aircraft aircraft)
    {
        this.aircraft=aircraft;var managers=new HashSet<NavLights>();
        void Include(GameObject root){foreach(var manager in root.GetComponentsInChildren<NavLights>(true))if(manager!=null&&manager.aircraft==aircraft)managers.Add(manager);}
        if(aircraft.partLookup!=null)foreach(var part in aircraft.partLookup)if(part!=null)Include(part.gameObject);Include(aircraft.gameObject);
        var selected=new List<Lamp>();
        foreach(var manager in managers.OrderBy(manager=>Key(manager,aircraft),StringComparer.Ordinal))
        {
            if(manager.navLights==null)continue;string prefix=Key(manager,aircraft);
            for(int i=0;i<manager.navLights.Length&&selected.Count<AircraftLightingPolicy.MaxLights;i++)
            {var light=manager.navLights[i];if(light==null)continue;selected.Add(new Lamp {key=prefix+":"+i,native=light,observed=light.isOn,control=(AircraftLampControl)light.controlState,usable=Usable(light)});}
        }
        lamps=selected.ToArray();lookup=lamps.ToDictionary(lamp=>lamp.key,StringComparer.Ordinal);
    }
    private static string Key(NavLights manager,Aircraft aircraft)
    {
        var part=manager.GetComponentInParent<UnitPart>();Transform root=part!=null?part.transform:aircraft.transform;
        var path=new List<string>();var current=manager.transform;
        while(current!=null&&current!=root&&path.Count<64)
        {int duplicate=0;if(current.parent!=null)for(int i=0;i<current.GetSiblingIndex();i++)if(current.parent.GetChild(i).name==current.name)duplicate++;path.Add(current.name+"#"+duplicate);current=current.parent;}
        path.Reverse();int slot=Array.IndexOf(manager.GetComponents<NavLights>(),manager);
        return(part!=null?part.id.ToString("D3"):"root")+"/"+string.Join("/",path)+":"+slot;
    }
    private static bool Usable(NavLights.NavLight light)
    {
        if(light.part!=null&&(light.part.IsDetached()||light.part.fragmented||!light.part.gameObject.activeInHierarchy))return false;
        if(light.gear!=null&&light.gear.attachedPart!=null)
        {
            var part=light.gear.attachedPart;if(part.IsDetached()||part.fragmented||!part.gameObject.activeInHierarchy)return false;
            if(light.gear.gearHinge==null||light.gear.gearHinge.parent==null)return false;
        }
        return true;
    }
    internal void Capture(AircraftLightingEvent value)
    {
        value.unitId=aircraft.persistentID.Id;value.keys=new string[lamps.Length];value.flags=new byte[lamps.Length];value.controls=new byte[lamps.Length];
        for(int i=0;i<lamps.Length;i++)
        {
            var light=lamps[i].native;value.keys[i]=lamps[i].key;bool visible=light.objects!=null&&light.objects.Any(item=>item!=null&&item.activeSelf);
            if(light.objects==null||light.objects.Length==0)visible=light.renderer!=null&&light.renderer.sharedMaterial==light.litMaterial;
            value.flags[i]=(byte)((visible?AircraftLampFlags.On:0)|(Usable(light)?AircraftLampFlags.Usable:0)|(light.gear!=null?AircraftLampFlags.Gear:0));value.controls[i]=(byte)light.controlState;
        }
    }
    internal void Read(AircraftLightingEvent value)
    {
        for(int i=0;i<value.keys.Length;i++)if(lookup.TryGetValue(value.keys[i],out var lamp))
        {lamp.recorded=true;lamp.observed=(value.flags[i]&(byte)AircraftLampFlags.On)!=0;lamp.usable=(value.flags[i]&(byte)AircraftLampFlags.Usable)!=0;lamp.control=(AircraftLampControl)value.controls[i];}
    }
    internal void Apply(int navMode,int landingMode)
    {
        bool gearOn=aircraft.gearState==LandingGear.GearState.Extending||aircraft.gearState==LandingGear.GearState.LockedExtended;
        foreach(var lamp in lamps)
        {
            var light=lamp.native;bool gear=light.gear!=null;bool observed=lamp.recorded?lamp.observed:light.isOn;
            var control=lamp.recorded?lamp.control:(AircraftLampControl)light.controlState;
            // Legacy files lack manual control state. The prefab/native state is
            // the fallback; never infer that every airborne light should be off.
            if(!lamp.recorded&&gear&&control==AircraftLampControl.Auto)observed=gearOn;
            bool enabled=AircraftLightingPolicy.Resolve(observed,(lamp.recorded?lamp.usable:true)&&Usable(light),gear,control,gearOn,gear?landingMode:navMode);
            if(light.renderer!=null){var material=enabled?light.litMaterial:light.baseMaterial;if(light.renderer.sharedMaterial!=material)light.renderer.sharedMaterial=material;}
            if(light.objects!=null)foreach(var obj in light.objects)if(obj!=null&&obj.activeSelf!=enabled)obj.SetActive(enabled);
        }
    }
}
