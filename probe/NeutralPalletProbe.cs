using System;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class NeutralPalletProbe
{
    public static void Run(UnitController controller,Action<string> log)
    {
        const uint id=889227;
        if(!Encyclopedia.Lookup.ContainsKey("MunitionsPallet1"))throw new Exception("Native munitions pallet missing from test game");
        var spawn=new SpawnEvent{Time=0,unitId=id,ownerId=id,jsonKey="MunitionsPallet1",factionName="no faction",pos=new GlobalPosition(1000,3000,1000),rotation=Quaternion.identity};
        controller.Execute(spawn);
        try
        {
            if(!(controller.FindActor(id) is Container))throw new Exception("Neutral munitions pallet did not spawn");
            log("NEUTRAL PALLET PASS native MunitionsPallet1 spawned with no faction");
        }
        finally {controller.DespawnUnit(id);}
    }
}
