using System;
using System.Linq;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class ShipInventoryProbe
{
    public static void Run(UnitController controller,Action<string> log)
    {
        uint id=889300;int count=0;
        foreach(var entry in Encyclopedia.Lookup.Where(x=>x.Value is ShipDefinition&&x.Value.unitPrefab!=null).ToArray())
        {
            id++;
            try
            {
                controller.Execute(new SpawnEvent{Time=0,unitId=id,ownerId=id,jsonKey=entry.Key,factionName="no faction",pos=new GlobalPosition(1000,3000,1000),rotation=Quaternion.identity});
                var ship=controller.FindActor(id);
                if(ship==null){log("SHIP INVENTORY unavailable "+entry.Key);continue;}
                var guns=ship.GetComponentsInChildren<Gun>(true).Where(g=>g!=null&&g.attachedUnit==ship).Distinct().ToArray();
                foreach(var gun in guns)
                    log("SHIP INVENTORY "+entry.Key+" gun="+(gun.info?.weaponName??gun.name)+" guided="+(gun.guidedProjectile==null?"none":gun.guidedProjectile.jsonKey)+" muzzles="+(gun.muzzles?.Length??0)+" station="+(gun.weaponStation?.Number.ToString()??"none")+" turret="+(gun.weaponStation?.GetTurret()==null?"none":"yes"));
                count+=guns.Length;
            }
            finally{controller.DespawnUnit(id);}
        }
        log("SHIP INVENTORY total guns="+count);
    }
}
