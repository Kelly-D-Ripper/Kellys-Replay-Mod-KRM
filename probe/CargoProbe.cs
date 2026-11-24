using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class CargoProbe
{
    public static async UniTask Run(UnitController controller,Action<string> log)
    {
        var defs=Encyclopedia.Lookup.Where(p=>p.Value is AircraftDefinition&&p.Value.unitName.IndexOf("Tarantula",StringComparison.OrdinalIgnoreCase)>=0).ToArray();
        if(defs.Length==0)throw new Exception("Tarantula aircraft definition unavailable");
        var entry=defs[0];var def=(AircraftDefinition)entry.Value;
        var loadouts=def.aircraftParameters.loadouts;
        log("CARGO Tarantula="+entry.Key+" default loadouts="+loadouts.Count);
        for(int i=0;i<loadouts.Count;i++)log("CARGO loadout "+i+": "+string.Join(",",loadouts[i].weapons.Select(w=>w==null?"none":w.jsonKey+"(cargo="+w.Cargo+")")));
        var selection=loadouts.FirstOrDefault(l=>l!=null&&l.weapons.Any(w=>w!=null&&w.prefab!=null&&w.prefab.GetComponentInChildren<MountedCargo>(true)!=null));
        if(selection==null)
        {
            var prefab=def.unitPrefab.GetComponent<Aircraft>();var sets=prefab.weaponManager.hardpointSets;
            for(int i=0;i<sets.Length;i++)log("CARGO hardpoint "+i+" "+sets[i].name+": "+string.Join(",",sets[i].weaponOptions.Where(w=>w!=null&&w.prefab!=null&&w.prefab.GetComponentInChildren<MountedCargo>(true)!=null).Select(w=>w.jsonKey)));
            int slot=Array.FindIndex(sets,s=>s.weaponOptions.Any(w=>w!=null&&w.prefab!=null&&w.prefab.GetComponentInChildren<MountedCargo>(true)!=null));
            if(slot<0)throw new Exception("No valid Tarantula cargo hardpoint");
            selection=new NuclearOption.SavedMission.Loadout();for(int i=0;i<sets.Length;i++)selection.weapons.Add(null);
            selection.weapons[slot]=sets[slot].weaponOptions.First(w=>w!=null&&w.prefab!=null&&w.prefab.GetComponentInChildren<MountedCargo>(true)!=null);
            log("CARGO test mount="+selection.weapons[slot].jsonKey+" hardpoint="+slot);
        }
        const uint id=889112;
        controller.Execute(new SpawnEvent{Time=0,unitId=id,ownerId=id,jsonKey=entry.Key,factionName="no faction",pos=new GlobalPosition(1000,3000,1000),rotation=Quaternion.identity,isAircraft=true,weapons=new List<WeaponMount>(selection.weapons)});
        try
        {
            await UniTask.Delay(1000,true);
            var craft=controller.FindActor(id) as Aircraft;if(craft==null)throw new Exception("Tarantula fixture missing");
            var mounts=craft.GetComponentsInChildren<MountedCargo>(true);
            log("CARGO replay spawned mounts="+mounts.Length+" stations="+craft.weaponStations.Count);
            foreach(var m in mounts)log("CARGO mount="+m.name+" active="+m.gameObject.activeSelf+" ammo="+m.ammo+" fired="+m.fired+" unit="+m.cargo?.unitName);
            foreach(var station in craft.weaponStations.Where(s=>s!=null&&s.Weapons!=null&&s.Weapons.Any(w=>w is MountedCargo)))log("CARGO station="+station.Number+" weapons="+station.Weapons.Count+" current="+(craft.weaponManager.currentWeaponStation==station));
            var cargo=mounts.FirstOrDefault();if(cargo==null)return;
            var targetStation=craft.weaponStations.First(s=>s!=null&&s.Weapons!=null&&s.Weapons.Contains(cargo));
            craft.weaponManager.SetActiveStation(targetStation.Number);
            var bindingType=typeof(UnitController).Assembly.GetType("ReplayMod.Core.ReplayCargoMounts");
            var ctor=bindingType.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(Aircraft)},null);
            var binding=ctor.Invoke(new object[]{craft});var capture=bindingType.GetMethod("Capture",BindingFlags.Instance|BindingFlags.NonPublic);
            var loaded=(CargoMountPose[])capture.Invoke(binding,null);if(loaded.Length!=1)throw new Exception("Cargo mount binding not found");
            var start=cargo.transform.localPosition;
            cargo.transform.localPosition=start+new Vector3(0,0,-2);cargo.fired=true;cargo.ammo=0;cargo.gameObject.SetActive(false);
            var dropped=(CargoMountPose[])capture.Invoke(binding,null);
            controller.Execute(new CargoMountStateEvent{unitId=id,Time=3,mounts=loaded});
            if(!cargo.gameObject.activeSelf||cargo.fired||cargo.ammo!=1||Vector3.Distance(cargo.transform.localPosition,start)>.001f)throw new Exception("Loaded cargo was not restored");
            byte prior=ReplayRuntime.FormatMajor;ReplayRuntime.FormatMajor=10;
            try{controller.WeaponManagerFire(new WeaponManagerFireEvent{unitId=id,Time=3});if(cargo.fired)throw new Exception("Recorded action fired a live cargo launch");}
            finally{ReplayRuntime.FormatMajor=prior;}
            controller.Execute(new CargoMountStateEvent{unitId=id,Time=3,mounts=dropped});
            if(cargo.gameObject.activeSelf||!cargo.fired||cargo.ammo!=0)throw new Exception("Released mount remained visible");
            const uint vehicleId=889113;
            byte originalFormat=ReplayRuntime.FormatMajor;ReplayRuntime.FormatMajor=10;
            try{
                controller.Execute(new SpawnEvent{Time=3,unitId=vehicleId,ownerId=vehicleId,jsonKey=cargo.cargo.jsonKey,factionName="no faction",pos=new GlobalPosition(1000,2900,1000),rotation=Quaternion.identity,weapons=new List<WeaponMount>()});
                if(!(controller.FindActor(vehicleId) is GroundVehicle vehicle))throw new Exception("Recorded released vehicle missing");
                if(cargo.gameObject.activeSelf)throw new Exception("Mounted cargo duplicate remained with carrier");
                var chuteType=typeof(UnitController).Assembly.GetType("ReplayMod.Core.ReplayCargoParachutes");
                var ensure=chuteType.GetMethod("Ensure",BindingFlags.Static|BindingFlags.NonPublic);
                var captureChute=chuteType.GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic);
                var system=(CargoDeploymentSystem)ensure.Invoke(null,new object[]{vehicle});
                if(system==null||system.listParachutes.Count==0)throw new Exception("APC parachute prefab unavailable");
                var chute=system.listParachutes[0];chute.canopy.SetActive(true);chute.lines.SetActive(true);
                chute.canopy.transform.position=vehicle.transform.position+new Vector3(4,30,2);
                var states=(CargoParachutePose[])captureChute.Invoke(null,new object[]{system,null});
                var expected=states[0].canopyPosition.ToLocalPosition();chute.canopy.SetActive(false);chute.lines.SetActive(false);
                controller.Execute(new CargoParachuteStateEvent{unitId=vehicleId,Time=3,present=true,parachutes=states});controller.Update(3);
                if(!chute.canopy.activeSelf||chute.enabled||Vector3.Distance(chute.canopy.transform.position,expected)>.01f)throw new Exception("Recorded parachute pose did not restore");
                controller.Execute(new CargoParachuteStateEvent{unitId=vehicleId,Time=4,present=false});controller.Update(4);
                if(chute.canopy.activeSelf||system.gameObject.activeSelf)throw new Exception("Released parachute did not disappear");
                controller.Update(3);if(!chute.canopy.activeSelf||Vector3.Distance(chute.canopy.transform.position,expected)>.01f)throw new Exception("Parachute rewind did not restore its pose");
                log("CARGO CHUTE PASS actual APC native rig recorded pose restored, physics disabled, removal and rewind; synthetic state events");
            }finally{controller.DespawnUnit(vehicleId);ReplayRuntime.FormatMajor=originalFormat;}
            controller.Execute(new CargoMountStateEvent{unitId=id,Time=3,mounts=loaded});
            if(!cargo.gameObject.activeSelf||cargo.fired)throw new Exception("Rewind could not restore cargo");
            log("CARGO MOUNT PASS actual Tarantula/APC loadout restored before/after release, separate vehicle spawn, rewind and replay native-launch guard; synthetic state events");
        }
        finally{controller.DespawnUnit(id);}
    }
}
