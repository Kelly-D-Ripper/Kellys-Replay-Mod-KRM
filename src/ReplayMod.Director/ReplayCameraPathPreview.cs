using UnityEngine;

namespace ReplayMod.Director;

// Owned screen guides; no world objects, colliders or materials are created.
// Sample counts remain bounded even for a large saved project.
public sealed class ReplayCameraPathPreview : MonoBehaviour
{
    private GUIStyle labels;
    private void OnGUI()
    {
        var director=ReplayDirector.i;
        if(Event.current==null||Event.current.type!=EventType.Repaint||!Core.ReplayRuntime.IsPlayback||director==null||director.Clean||director.IsBusy||!director.ShowCameraPath||director.PathPointCount==0)return;
        var camera=SceneSingleton<CameraStateManager>.i?.mainCamera;if(camera==null)return;
        var keys=director.Project.cameras;
        var previousColor=GUI.color;var previousMatrix=GUI.matrix;int previousDepth=GUI.depth;
        try
        {
            GUI.depth=100;GUI.color=new Color(.25f,.85f,.9f,.9f);
            if(labels==null)labels=new GUIStyle(GUI.skin.label){fontSize=13};labels.normal.textColor=GUI.color;
            double start=keys[0].time,end=keys[keys.Count-1].time;
            int segments=Mathf.Min(192,Mathf.Max(1,(keys.Count-1)*12));
            if(director.SampleCameraPath(start,out var last))
                for(int i=1;i<=segments;i++)
                    if(director.SampleCameraPath(start+(end-start)*i/segments,out var next))
                    {Line(camera,last.position,next.position);last=next;}
            int stride=Mathf.Max(1,Mathf.CeilToInt(keys.Count/64f));
            for(int i=0;i<keys.Count;i+=stride)
            {
                if(!director.SampleCameraPath(keys[i].time,out var point))continue;
                var screen=camera.WorldToScreenPoint(point.position);
                if(screen.z<=camera.nearClipPlane)continue;
                float x=screen.x,y=Screen.height-screen.y;
                if(x< -100||y< -100||x>Screen.width+100||y>Screen.height+100)continue;
                GUI.DrawTexture(new Rect(x-4,y-4,8,1),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x-4,y+3,8,1),Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x-4,y-4,1,8),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x+3,y-4,1,8),Texture2D.whiteTexture);
                GUI.Label(new Rect(x+7,y-16,90,24),"C"+(i+1),labels);
                Line(camera,point.position,point.position+point.rotation*Vector3.forward*Mathf.Clamp(director.Project.distance*.5f,5,100));
            }
        }
        finally{GUI.color=previousColor;GUI.matrix=previousMatrix;GUI.depth=previousDepth;}
    }
    private static void Line(UnityEngine.Camera camera,Vector3 a,Vector3 b)
    {
        var p=camera.WorldToScreenPoint(a);var q=camera.WorldToScreenPoint(b);
        if(p.z<=camera.nearClipPlane||q.z<=camera.nearClipPlane)return;
        var start=new Vector2(p.x,Screen.height-p.y);var finish=new Vector2(q.x,Screen.height-q.y);
        float length=Vector2.Distance(start,finish);if(float.IsNaN(length)||float.IsInfinity(length)||length>1e7)return;
        var original=GUI.matrix;
        try{GUIUtility.RotateAroundPivot(Mathf.Atan2(finish.y-start.y,finish.x-start.x)*Mathf.Rad2Deg,start);GUI.DrawTexture(new Rect(start.x,start.y,length,1),Texture2D.whiteTexture);}
        finally{GUI.matrix=original;}
    }
}
