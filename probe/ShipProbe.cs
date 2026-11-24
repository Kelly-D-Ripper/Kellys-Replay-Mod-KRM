using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class ShipProbe
{
    public static void Run(UnitController controller,Action<string> log)
    {
        var entry=Encyclopedia.Lookup.First(p=>p.Value is ShipDefinition&&p.Value.unitName.IndexOf("Argus",StringComparison.OrdinalIgnoreCase)>=0);
        const uint id=889111;
        controller.Execute(new SpawnEvent{Time=0,unitId=id,ownerId=id,jsonKey=entry.Key,factionName="no faction",pos=new GlobalPosition(1000,3000,1000),rotation=Quaternion.identity,weapons=new List<WeaponMount>()});
        var ship=controller.FindActor(id);
        try{
            if(ship==null)throw new Exception("Argus fixture did not spawn");
            var guns=ship.partLookup.Where(p=>p!=null).SelectMany(p=>p.GetComponentsInChildren<Gun>(true)).Where(g=>g!=null&&g.attachedUnit==ship).Distinct().ToArray();
            log("ARGUS guns "+string.Join(", ",guns.Select(g=>g.info?.weaponName??g.name)));
            var gun=guns.FirstOrDefault(g=>(g.info?.weaponName??"").IndexOf("127",StringComparison.OrdinalIgnoreCase)>=0);
            if(gun==null)throw new Exception("Argus 127mm gun was not found in spawned fixture");
            var station=ship.weaponStations.First(s=>s!=null&&s.Turrets!=null&&s.Turrets.Any(t=>t!=null));
            var turret=station.Turrets.First(t=>t!=null);
            controller.Execute(new UpdateTurretEvent{Time=3,attachedUnitId=id,weaponStationIdx=station.Number,turretIdx=turret.turretIndex,elevationAngle=12,traverseAngle=38});
            controller.MoveTurrets(3);
            if(Math.Abs(turret.elevationAngle-12)>.01f||Math.Abs(turret.traverseAngle-38)>.01f)throw new Exception("Argus turret sample did not reach the recorded station and turret");
            var shot=new GunBulletEvent{unitId=id,Time=3,life=2,active=true,velocity=new Vector3(0,0,500),position=gun.transform.position.ToGlobalPosition(),reliability=1,tracerSize=1,color=Color.white};
            var identity=typeof(UnitController).Assembly.GetType("ReplayMod.Core.ReplayGunIdentity");
            bool captured=(bool)identity.GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{ship,gun,shot});
            if(!captured)throw new Exception("Argus gun identity was not recordable");
            controller.Execute(shot);
            if(gun.bulletSim==null||gun.bulletSim.bullets.Count==0)throw new Exception("Argus recorded projectile was not recreated");
            var bullet=gun.bulletSim.bullets[gun.bulletSim.bullets.Count-1];
            if(Vector3.Distance(bullet.position.ToLocalPosition(),shot.position.ToLocalPosition())>.01f)throw new Exception("Argus projectile origin differs from recorded position");
            log("ARGUS PASS native ship turret by recorded IDs, gun identity and recorded projectile recreation at recorded origin; simulated events only");
        }finally{controller.DespawnUnit(id);}
    }
}
