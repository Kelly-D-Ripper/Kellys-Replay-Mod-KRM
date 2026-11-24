using System;
using System.Collections.Generic;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class FramingProbe
{
    public static void Run(UnitController controller,Action<string> log)
    {
        var primary=controller.FindActor(777) as Aircraft;
        var director=ReplayDirector.i;var camera=SceneSingleton<CameraStateManager>.i;
        uint secondId=888999;
        controller.Execute(new SpawnEvent {Time=0,unitId=secondId,ownerId=secondId,jsonKey=primary.definition.jsonKey,
            factionName="no faction",pos=new GlobalPosition(300,3000,0),rotation=Quaternion.identity,isAircraft=true,weapons=new List<WeaponMount>()});
        var second=controller.FindActor(secondId);
        try
        {
            director.SelectCameraTarget(777);director.SelectSecondaryCameraTarget(secondId);
            director.Project.fov=40;director.Project.smoothing=0;director.Project.distance=35;
            if(!director.SelectCameraRig(6))throw new Exception("Pair rig unavailable");
            director.UpdateCamera(3);
            CheckFrame(camera.mainCamera,primary.transform.position);CheckFrame(camera.mainCamera,second.transform.position);
            var offset=camera.transform.position-(primary.transform.position+second.transform.position)*.5f;
            director.UpdateCamera(3);
            var offset2=camera.transform.position-(primary.transform.position+second.transform.position)*.5f;
            if(Vector3.Distance(offset,offset2)>.01f)throw new Exception("Pair framing drifts while paused");
            director.SelectCameraRig(7);director.Project.followWithAircraft=false;director.UpdateCamera(3);
            var before=(camera.transform.position-primary.transform.position).normalized;
            var original=primary.transform.rotation;primary.transform.rotation=Quaternion.Euler(30,90,40);
            director.UpdateCamera(3.02);
            if(Vector3.Angle(before,camera.transform.position-primary.transform.position)>.05f)throw new Exception("Fixed world angle rotated with aircraft");
            director.Project.followWithAircraft=true;director.ResetCameraSmoothing();director.UpdateCamera(3.04);
            var local=primary.transform.InverseTransformDirection(camera.transform.position-primary.transform.position).normalized;
            primary.transform.rotation=Quaternion.Euler(-20,160,20);director.UpdateCamera(3.06);
            if(Vector3.Angle(local,primary.transform.InverseTransformDirection(camera.transform.position-primary.transform.position))>.05f)throw new Exception("Attached follow angle did not track rotation");
            primary.transform.rotation=original;
            director.Save();director.Load();
            if(director.Project.rig!=7||!director.Project.followWithAircraft||director.Project.secondTarget!=secondId)throw new Exception("Camera settings not retained by project save/load");
            director.EnterFreeCamera();if(!camera.allowInputs)throw new Exception("Free camera inputs not restored");
            log("FRAMING PASS pair centres in viewport; paused stable; world and aircraft fixed-angle modes; project round-trip; free input restored");
        }
        finally{director.EnterFreeCamera();controller.DespawnUnit(secondId);}
    }
    private static void CheckFrame(UnityEngine.Camera camera,Vector3 point)
    {
        var p=camera.WorldToViewportPoint(point);
        if(p.z<=0||p.x<0||p.x>1||p.y<0||p.y>1)throw new Exception("Pair subject outside viewport: "+p);
    }
}
