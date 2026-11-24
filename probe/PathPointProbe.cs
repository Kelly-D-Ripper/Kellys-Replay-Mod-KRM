using System;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;

public static class PathPointProbe
{
    public static async UniTask Run(Player player,Action<string> log)
    {
        var director=ReplayDirector.i;var camera=SceneSingleton<CameraStateManager>.i;
        director.EnterFreeCamera();director.Project.cameras.Clear();
        var actor=player.FindActor(777);director.SelectCameraTarget(777);
        var global=(actor.transform.position+new Vector3(20,10,30)).ToGlobalPosition();
        var rotation=Quaternion.Euler(12,45,25);
        var first=new CameraKey{time=2,x=global.x,y=global.y,z=global.z,qx=rotation.x,qy=rotation.y,qz=rotation.z,qw=rotation.w,fov=31};
        var middle=new CameraKey{time=3,x=global.x,y=global.y,z=global.z,qx=rotation.x,qy=rotation.y,qz=rotation.z,qw=rotation.w,fov=37};
        var last=new CameraKey{time=4,x=global.x,y=global.y,z=global.z,qx=rotation.x,qy=rotation.y,qz=rotation.z,qw=rotation.w,fov=45};
        director.Project.cameras.Add(first);director.Project.cameras.Add(middle);director.Project.cameras.Add(last);
        director.Project.animatePathRotation=false;director.Project.animatePathFov=false;
        director.GoToPathPoint(middle);await Ready(director);
        camera=SceneSingleton<CameraStateManager>.i;
        log("PATH detached restored actor="+(player.FindActor(777)!=null)+" status="+director.Status);
        if(Math.Abs(player.currentVirtualTime-3)>.001||Vector3.Distance(camera.transform.position,new GlobalPosition(global.x,global.y,global.z).ToLocalPosition())>.1f||Quaternion.Angle(camera.transform.rotation,rotation)>.1f||Math.Abs(director.CurrentCameraFov-37)>.1f)throw new Exception("Detached point pose or lens not restored");
        var attached=new CameraKey{time=3,x=12,y=4,z=-8,qx=rotation.x,qy=rotation.y,qz=rotation.z,qw=rotation.w,fov=52,relative=true,target=777};
        director.Project.cameras[1]=attached;director.GoToPathPoint(attached);await Ready(director);
        camera=SceneSingleton<CameraStateManager>.i;
        actor=player.FindActor(777);
        log("PATH attached restored actor="+(actor!=null)+" status="+director.Status);
        if(actor==null)throw new Exception("Attached subject absent after seek");
        if(Vector3.Distance(camera.transform.position,actor.transform.TransformPoint(new Vector3(12,4,-8)))>.1f||Quaternion.Angle(camera.transform.rotation,actor.transform.rotation*rotation)>.1f||Math.Abs(director.CurrentCameraFov-52)>.1f)throw new Exception("Attached point pose or lens not restored");
        if(!director.RemovePathPoint(attached)||director.Project.cameras.Count!=2||!ReferenceEquals(director.Project.cameras[0],first)||!ReferenceEquals(director.Project.cameras[1],last))throw new Exception("Wrong point removed");
        log("PATH PASS detached and attached seek restore position, roll, orientation and FOV; specific middle deletion");
    }
    private static async UniTask Ready(ReplayDirector director)
    {
        float end=Time.realtimeSinceStartup+30;
        while(!director.Status.StartsWith("Camera point restored")){if(Time.realtimeSinceStartup>end)throw new Exception("Point restore timed out: "+director.Status);await UniTask.NextFrame();}
        await UniTask.NextFrame();
    }
}
