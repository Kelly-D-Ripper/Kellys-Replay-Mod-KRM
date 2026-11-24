using System;
using System.Linq;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

internal static class ReplayCargoParachutes
{
    internal static GameObject Prefab(Unit unit)=>unit is GroundVehicle vehicle?vehicle.parachuteSystem:unit is Container container?container.parachuteSystem:null;
    internal static CargoDeploymentSystem Find(Unit unit)=>unit==null?null:unit.GetComponentsInChildren<CargoDeploymentSystem>(true).FirstOrDefault(s=>s!=null&&s.attachedUnit==unit);
    internal static void StopNative(CargoDeploymentSystem system)
    {
        if(system==null)return;
        system.enabled=false;
        if(system.listParachutes!=null)foreach(var chute in system.listParachutes)if(chute!=null)chute.enabled=false;
    }
    internal static CargoParachutePose[] Capture(CargoDeploymentSystem system,CargoParachutePose[] previous)
    {
        if(system?.listParachutes==null||system.listParachutes.Count>64)throw new InvalidOperationException("Invalid cargo parachute layout");
        var result=new CargoParachutePose[system.listParachutes.Count];
        for(int i=0;i<result.Length;i++)
        {
            var chute=system.listParachutes[i];
            string key=chute!=null?i+":"+chute.name:null;
            bool hasPrevious=previous!=null&&previous.Length==result.Length&&!string.IsNullOrEmpty(previous[i].key)&&(key==null||previous[i].key==key);
            if((chute==null||chute.canopy==null||chute.lines==null)&&!hasPrevious)
                throw new InvalidOperationException("Cargo parachute presentation is unavailable before its first complete sample");
            var pose=hasPrevious?previous[i]:new CargoParachutePose{key=key};
            if(chute!=null&&chute.canopy!=null)
            {
                var canopy=chute.canopy.transform;pose.canopyActive=chute.canopy.activeSelf;
                pose.canopyPosition=canopy.position.ToGlobalPosition();pose.canopyRotation=canopy.rotation;pose.canopyScale=canopy.localScale;
            }
            else pose.canopyActive=false;
            if(chute!=null&&chute.lines!=null)
            {
                var lines=chute.lines.transform;pose.linesActive=chute.lines.activeSelf;
                pose.linesPosition=lines.position.ToGlobalPosition();pose.linesRotation=lines.rotation;pose.linesScale=lines.localScale;
            }
            else pose.linesActive=false;
            result[i]=pose;
        }
        return result;
    }
    internal static CargoDeploymentSystem Ensure(Unit unit)
    {
        var system=Find(unit);if(system!=null){StopNative(system);return system;}
        var prefab=Prefab(unit);if(prefab==null)return null;
        var instance=UnityEngine.Object.Instantiate(prefab,unit.transform);
        system=instance.GetComponent<CargoDeploymentSystem>();
        if(system==null){UnityEngine.Object.Destroy(instance);return null;}
        system.Initialize(unit);StopNative(system);return system;
    }
    internal static bool Apply(Unit unit,CargoParachuteStateEvent a,CargoParachuteStateEvent b,float fraction)
    {
        if(unit==null||a==null)return false;
        var system=Find(unit);
        if(!a.present)
        {
            if(system!=null)
            {
                StopNative(system);
                if(system.listParachutes!=null)foreach(var p in system.listParachutes)if(p!=null){if(p.canopy!=null)p.canopy.SetActive(false);if(p.lines!=null)p.lines.SetActive(false);}
                system.gameObject.SetActive(false);
            }
            return true;
        }
        system=system!=null?system:Ensure(unit);if(system?.listParachutes==null||system.listParachutes.Count!=a.parachutes.Length)return false;
        StopNative(system);
        for(int i=0;i<a.parachutes.Length;i++)
        {
            var chute=system.listParachutes[i];if(chute==null||chute.canopy==null||chute.lines==null||a.parachutes[i].key!=i+":"+chute.name)return false;
        }
        if(!system.gameObject.activeSelf)system.gameObject.SetActive(true);
        for(int i=0;i<a.parachutes.Length;i++)
        {
            var chute=system.listParachutes[i];var from=a.parachutes[i];var to=b.parachutes[i];
            var canopy=chute.canopy.transform;var lines=chute.lines.transform;
            if(from.canopyActive&&canopy.parent!=null)canopy.SetParent(null,true);
            canopy.SetPositionAndRotation(Vector3.Lerp(from.canopyPosition.ToLocalPosition(),to.canopyPosition.ToLocalPosition(),fraction),Quaternion.Slerp(from.canopyRotation,to.canopyRotation,fraction));
            canopy.localScale=Vector3.Lerp(from.canopyScale,to.canopyScale,fraction);
            lines.SetPositionAndRotation(Vector3.Lerp(from.linesPosition.ToLocalPosition(),to.linesPosition.ToLocalPosition(),fraction),Quaternion.Slerp(from.linesRotation,to.linesRotation,fraction));
            lines.localScale=Vector3.Lerp(from.linesScale,to.linesScale,fraction);
            if(chute.canopy.activeSelf!=from.canopyActive)chute.canopy.SetActive(from.canopyActive);
            if(chute.lines.activeSelf!=from.linesActive)chute.lines.SetActive(from.linesActive);
        }
        return true;
    }
}
