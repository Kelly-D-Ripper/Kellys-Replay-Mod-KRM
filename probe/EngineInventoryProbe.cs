using System;
using System.Linq;
using System.Collections.Generic;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class EngineInventoryProbe
{
    public static void Run(UnitController controller,Action<string> log)
    {
        if(Environment.GetEnvironmentVariable("REPLAY_ENGINE_ALL_INVENTORY")=="1")
        {
            foreach(var item in Encyclopedia.Lookup.Where(p=>p.Value is AircraftDefinition && p.Value.unitPrefab!=null))
            {
                var engines=item.Value.unitPrefab.GetComponentsInChildren<MonoBehaviour>(true)
                    .Where(e=>e is PropFan || e is Turbofan || e is Turbojet || e is TurbineEngine).ToArray();
                foreach(var engine in engines)
                {
                    var component=engine as Component;
                    var sounds=component==null?Array.Empty<AudioSource>():component.GetComponentsInChildren<AudioSource>(true);
                    log("ENGINE TYPE key="+item.Key+" name="+item.Value.unitName+" engine="+engine.GetType().Name+" sounds="+string.Join(",",sounds.Select(s=>s.name+":"+(s.clip==null?"none":s.clip.name)+":"+s.loop)));
                }
            }
            return;
        }
        var entry=Encyclopedia.Lookup.First(p=>p.Value is AircraftDefinition&&p.Value.unitName.IndexOf("Brawler",StringComparison.OrdinalIgnoreCase)>=0);
        const uint id=889333;
        controller.Execute(new SpawnEvent{Time=0,unitId=id,ownerId=id,jsonKey=entry.Key,factionName="no faction",pos=new GlobalPosition(1000,3000,1000),rotation=Quaternion.identity,isAircraft=true,weapons=new List<WeaponMount>()});
        try
        {
            var aircraft=controller.FindActor(id) as Aircraft;if(aircraft==null)throw new Exception("Brawler fixture unavailable");
            log("ENGINE INVENTORY "+entry.Key+" engines="+aircraft.engines.Count);
            foreach(var engine in aircraft.engines.Where(e=>e!=null))
            {
                log("ENGINE INVENTORY type="+engine.GetType().FullName+" rpm="+engine.GetRPM()+" callbacks="+string.Join(",",engine.GetType().GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly).Where(m=>m.Name.IndexOf("Animate",StringComparison.OrdinalIgnoreCase)>=0||m.Name=="FixedUpdate"||m.Name=="Update").Select(m=>m.Name)));
                if(engine is Component component)foreach(var source in component.GetComponentsInChildren<AudioSource>(true))log("ENGINE AUDIO source="+source.name+" clip="+(source.clip==null?"none":source.clip.name)+" loop="+source.loop);
            }
        }
        finally{controller.DespawnUnit(id);}
    }
}
