using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Director;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

// Runs in the isolated replay probe, on the main thread. No gameplay spawn or
// damage callbacks are used; the recording baseline below is a test fixture.
public static class LightingProbe
{
    private const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    public static void Run(UnitController controller,Action<string> log)
    {
        var player=ReplayManager.i?.Playback;
        if(!ReplayRuntime.IsPlayback||player==null||!ReferenceEquals(typeof(ReplayMod.Core.Player).GetField("_unitCotroller",Members).GetValue(player),controller))
            throw new InvalidOperationException("Lighting probe requires the active replay controller.");
        var pair=controller.Actors().FirstOrDefault(item=>
        {
            if(!(item.Value is Aircraft a))return false;
            var native=a.GetComponentsInChildren<NavLights>(true).Where(n=>n.aircraft==a&&n.navLights!=null).SelectMany(n=>n.navLights).Where(n=>n!=null).ToArray();
            return native.Any(n=>n.gear==null)&&native.Any(n=>n.gear!=null);
        });
        if(!(pair.Value is Aircraft aircraft))throw new InvalidOperationException("Lighting fixture has no aircraft with native lamps.");
        var project=ReplayDirector.i.Project;
        int oldNav=project.lightMode,oldLanding=project.landingLightMode;bool oldAll=project.lightsAll;uint oldTarget=project.lightTarget;
        project.lightMode=project.landingLightMode=0;project.lightsAll=true;player.RefreshPlaybackLights();

        // Capture through the same stable-address binding used by the recorder,
        // but retain direct native lamps for independent material/object checks.
        var bindingType=typeof(ReplayManager).Assembly.GetType("ReplayMod.Core.ReplayAircraftLights",true);
        var binding=Activator.CreateInstance(bindingType,Members,null,new object[]{aircraft},null);
        var capture=bindingType.GetMethod("Capture",Members);
        var baseline=new AircraftLightingEvent{Time=player.currentVirtualTime};capture.Invoke(binding,new object[]{baseline});baseline.unitId=pair.Key;
        var entries=((IEnumerable)bindingType.GetField("lamps",Members).GetValue(binding)).Cast<object>().ToArray();
        var lamps=entries.Select(item=>(NavLights.NavLight)item.GetType().GetField("native",Members).GetValue(item)).ToArray();
        var oldNativeOn=lamps.Select(lamp=>lamp.isOn).ToArray();
        int navCount=lamps.Count(lamp=>lamp.gear==null),landingCount=lamps.Count(lamp=>lamp.gear!=null);
        if(navCount==0||landingCount==0)throw new InvalidOperationException("Lighting probe needs both navigation and gear-associated lamps; aircraft="+aircraft.definition.jsonKey);
        var fixture=new AircraftLightingEvent{unitId=pair.Key,Time=baseline.Time,keys=(string[])baseline.keys.Clone(),flags=(byte[])baseline.flags.Clone(),controls=(byte[])baseline.controls.Clone()};
        for(int i=0;i<fixture.flags.Length;i++)
        {
            if((fixture.flags[i]&(byte)AircraftLampFlags.Usable)==0)throw new InvalidOperationException("Lighting probe requires an intact aircraft.");
            fixture.flags[i]|=(byte)AircraftLampFlags.On;
            fixture.controls[i]=(byte)AircraftLampControl.ForceOn;
        }
        var customObject=new GameObject("Lighting probe custom studio lamp");
        var custom=customObject.AddComponent<Light>();custom.type=LightType.Point;custom.enabled=true;custom.intensity=2.5f;custom.color=Color.cyan;
        try
        {
            controller.Execute(fixture);
            project.lightMode=project.landingLightMode=2;player.RefreshPlaybackLights();
            foreach(var lamp in lamps)CheckLamp(lamp,false,"blackout");
            foreach(var lamp in lamps)lamp.Toggle(true);
            foreach(var lamp in lamps)CheckLamp(lamp,false,"native toggle correction");

            project.lightMode=1;project.landingLightMode=2;player.RefreshPlaybackLights();
            foreach(var lamp in lamps)CheckLamp(lamp,lamp.gear==null,"navigation only");
            project.lightMode=2;project.landingLightMode=1;player.RefreshPlaybackLights();
            foreach(var lamp in lamps)CheckLamp(lamp,lamp.gear!=null,"landing only");

            project.lightMode=project.landingLightMode=0;player.RefreshPlaybackLights();
            foreach(var lamp in lamps)CheckLamp(lamp,true,"recorded fixture restored");
            if(!customObject.activeSelf||!custom.enabled||custom.intensity!=2.5f||custom.color!=Color.cyan)
                throw new Exception("Aircraft blackout changed an unrelated custom light.");
            log("LIGHTING PASS aircraft="+aircraft.definition.jsonKey+" navigation="+navCount+" landing="+landingCount+"; materials/objects off, native Toggle corrected, groups independent, recorded baseline restored, custom light untouched");
        }
        finally
        {
            controller.Execute(baseline);
            project.lightMode=oldNav;project.landingLightMode=oldLanding;project.lightsAll=oldAll;project.lightTarget=oldTarget;
            for(int i=0;i<lamps.Length;i++)lamps[i].Toggle(oldNativeOn[i]);
            player.RefreshPlaybackLights();UnityEngine.Object.Destroy(customObject);
        }
    }
    private static void CheckLamp(NavLights.NavLight lamp,bool on,string stage)
    {
        if(lamp.renderer!=null&&lamp.renderer.sharedMaterial!=(on?lamp.litMaterial:lamp.baseMaterial))
            throw new Exception(stage+": emissive lens material does not match expected light state.");
        if(lamp.objects!=null&&lamp.objects.Any(obj=>obj!=null&&obj.activeSelf!=on))
            throw new Exception(stage+": native lamp object does not match expected light state.");
    }
}
