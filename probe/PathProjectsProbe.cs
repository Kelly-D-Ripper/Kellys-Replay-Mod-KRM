using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;

public static class PathProjectsProbe
{
    static async UniTask Open(string name)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);
        await ReplayManager.i.StartPlaying(name);
        if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("Failed to open project replay "+name);
        ReplayManager.i.Playback.Pause();
    }
    static CameraKey Point(double time,float x,Quaternion rotation,float fov,bool relative=false,uint target=0)=>
        new CameraKey{time=time,x=x,y=123,z=456,qx=rotation.x,qy=rotation.y,qz=rotation.z,qw=rotation.w,fov=fov,relative=relative,target=target,segmentMode=2};
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static async UniTask Restore(ReplayDirector director,CameraKey point)
    {
        director.GoToPathPoint(point);float until=Time.realtimeSinceStartup+30;
        while(!director.Status.StartsWith("Camera point restored"))
        {if(Time.realtimeSinceStartup>until)throw new Exception("Saved point restore failed: "+director.Status);await UniTask.NextFrame();}
        await UniTask.NextFrame();
    }
    public static async UniTask Run(string first,string second,Action<string> log)
    {
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays");string sideA=Path.Combine(folder,first)+".director.json",sideB=Path.Combine(folder,second)+".director.json";
        Quaternion roll=Quaternion.Euler(13,27,41);
        await Open(first);var d=ReplayDirector.i;
        d.Project.cameras.Clear();d.Project.cameras.Add(Point(2,11,roll,37));d.Project.cameras.Add(Point(4,22,roll,52));
        d.Project.animatePathFov=true;d.Project.smoothCameraPath=true;d.Project.cameraPathSmoothness=.8f;d.Project.studioLights.Add(new StudioLightSettings{name="rim",intensity=9});d.Project.environment.hour=19;d.Save();
        Require(d.Status=="Camera project saved."&&File.Exists(sideA),"First project save failed: "+d.Status);
        byte[] smallProject=File.ReadAllBytes(sideA);for(int i=0;i<6000;i++)d.Project.bookmarks.Add(new Bookmark{time=0,name=new string('測',120)});
        d.Save();Require(d.Status.StartsWith("Save failed:")&&File.ReadAllBytes(sideA).SequenceEqual(smallProject),"Oversized project replaced the last valid sidecar");d.Project.bookmarks.Clear();
        await Open(second);d=ReplayDirector.i;Require(d.Project.cameras.Count==0,"First replay path leaked into second replay");
        d.Project.cameras.Add(Point(3,77,Quaternion.identity,65,true,777));d.Save();Require(d.Status=="Camera project saved."&&File.Exists(sideB),"Second project save failed: "+d.Status);
        await Open(first);d=ReplayDirector.i;Require(d.Project.cameras.Count==2&&d.Project.cameras[0].x==11&&d.Project.cameras[1].x==22,"First replay points changed after switching");
        Require(d.Project.studioLights.Count==1&&d.Project.studioLights[0].name=="rim"&&d.Project.studioLights[0].intensity==9&&d.Project.environment.hour==19,"Studio light or environment setting was not restored");
        var k=d.Project.cameras[0];Require(Math.Abs(k.fov-37)<.001&&Quaternion.Angle(new Quaternion(k.qx,k.qy,k.qz,k.qw),roll)<.01f&&k.segmentMode==2&&d.Project.animatePathFov&&Math.Abs(d.Project.cameraPathSmoothness-.8f)<.001,"First replay camera settings not restored");
        await Restore(d,k);var camera=SceneSingleton<CameraStateManager>.i;
        Require(Vector3.Distance(camera.transform.position,new GlobalPosition(k.x,k.y,k.z).ToLocalPosition())<.1f&&Quaternion.Angle(camera.transform.rotation,roll)<.1f&&Math.Abs(d.CurrentCameraFov-37)<.1f,"Reopened detached camera point did not restore its pose and FOV");
        Require(d.RemovePathPoint(k),"Could not delete specific saved point");d.Save();Require(d.Status=="Camera project saved.","Delete save failed: "+d.Status);
        await Open(second);d=ReplayDirector.i;Require(d.Project.cameras.Count==1&&d.Project.cameras[0].relative&&d.Project.cameras[0].target==777&&d.Project.cameras[0].x==77&&d.Project.cameras[0].fov==65,"Second attached point was not isolated");
        k=d.Project.cameras[0];await Restore(d,k);camera=SceneSingleton<CameraStateManager>.i;var actor=ReplayManager.i.Playback.FindActor(777);
        Require(actor!=null&&Vector3.Distance(camera.transform.position,actor.transform.TransformPoint(new Vector3(k.x,k.y,k.z)))<.1f&&Quaternion.Angle(camera.transform.rotation,actor.transform.rotation)<.1f&&Math.Abs(d.CurrentCameraFov-65)<.1f,"Reopened attached camera point did not restore its pose and FOV");
        await Open(first);d=ReplayDirector.i;Require(d.Project.cameras.Count==1&&d.Project.cameras[0].x==22,"Specific point deletion was not persisted");
        await ReplayManager.i.SwitchState(ModStates.Idle);File.Copy(sideA,sideB,true);byte[] mismatched=File.ReadAllBytes(sideB);
        await ReplayManager.i.StartPlaying(second);d=ReplayDirector.i;Require(d.Status.StartsWith("Load failed:")&&d.Project.cameras.Count==0,"Foreign replay project was not rejected");
        await ReplayManager.i.SwitchState(ModStates.Idle);Require(File.ReadAllBytes(sideB).SequenceEqual(mismatched),"Rejected project was overwritten on close");
        log("PROJECTS PASS two replay-specific projects persisted detached/attached point pose, roll, FOV, studio lights and settings and restored both cameras; individual deletion survived reopen; oversized saves and fingerprint mismatch preserved sidecars");
    }
}
