using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

// Local-only native probe; never included in the tester plugin package.
public static class CopilotProbe
{
    public static void Run(UnitController controller,Action<string> log)
    {
        uint id=988777;
        var playback=ReplayManager.i.Playback;
        var director=ReplayDirector.i;
        var lifetimes=(CameraActorLifetimeIndex)typeof(Player).GetField("_cameraLifetimes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(playback);
        uint originalTarget=director.Project.target;int originalSeat=director.Project.cockpitSeat;
        bool found=false;
        foreach(var entry in Encyclopedia.Lookup.Where(p=>p.Value is AircraftDefinition&&p.Value.unitPrefab!=null))
        {
            var prefab=entry.Value.unitPrefab.GetComponent<Aircraft>();
            if(prefab?.pilots==null||prefab.pilots.Length<2)continue;
            try
            {
                controller.Execute(new SpawnEvent {Time=0,unitId=id,ownerId=id,jsonKey=entry.Key,factionName="no faction",pos=new GlobalPosition(100,3000,0),rotation=Quaternion.identity,isAircraft=true,weapons=new List<WeaponMount>()});
                var actor=controller.FindActor(id) as Aircraft;
                if(actor==null)throw new Exception("Multi-seat native aircraft did not spawn: "+entry.Key);
                lifetimes.Spawn(id,0,9900000);
                bool pilot=ReplayCockpitSeats.TryEye(actor,0,out var eye0,out _);
                bool copilot=ReplayCockpitSeats.TryEye(actor,1,out var eye1,out _);
                log("copilot candidate="+entry.Key+" pilots="+actor.pilots.Length+" primary="+pilot+" secondary="+copilot+" distance="+Vector3.Distance(eye0,eye1));
                if(!pilot||!copilot)
                {
                    foreach(var seat in actor.pilots)if(seat!=null&&seat.skinnedMeshRenderer!=null)
                        log("copilot anchors="+string.Join(",",seat.skinnedMeshRenderer.bones.Where(b=>b!=null&&b.name.IndexOf("head",StringComparison.OrdinalIgnoreCase)>=0).Select(b=>b.name)));
                    continue;
                }
                if(Vector3.Distance(eye0,eye1)<.1f)throw new Exception("Copilot viewpoint overlaps primary seat");
                if(!director.SelectCameraTarget(id)||!director.SelectCockpitSeat(1)||director.Project.cockpitSeat!=1)throw new Exception("Copilot camera selection failed: "+director.Status);
                ReplayInteriorPresentation.Update(playback,id,actor,true,true,true,true);
                if(!ReplayInteriorPresentation.TryGetCockpitEye(playback,id,actor,out var chosen,out _,1)||Vector3.Distance(chosen,eye1)>.001f)throw new Exception("Cockpit camera did not resolve selected seat");
                bool ejected=actor.pilots[1].ejected;
                try {actor.pilots[1].ejected=true;if(ReplayCockpitSeats.TryEye(actor,1,out _,out _))throw new Exception("Ejected copilot seat remained available");}
                finally {actor.pilots[1].ejected=ejected;}
                if(ReplayCockpitSeats.TryEye(actor,actor.pilots.Length,out _,out _))throw new Exception("Out-of-range seat unexpectedly resolved");
                log("copilot PASS native seat selection, distinct eye, ejection and bounds guards");found=true;break;
            }
            finally
            {
                director.EnterFreeCamera();ReplayInteriorPresentation.Clear();controller.DespawnUnit(id);lifetimes.Despawn(id,playback.currentVirtualTime);
                if(originalTarget!=0)director.SelectCameraTarget(originalTarget);
                director.Project.cockpitSeat=originalSeat;
                id++;
            }
        }
        if(!found)throw new Exception("No native multi-seat aircraft exposed a usable copilot head anchor; inspect copilot candidate logs.");
    }
}

