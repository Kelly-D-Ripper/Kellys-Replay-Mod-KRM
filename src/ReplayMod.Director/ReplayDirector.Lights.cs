using ReplayMod.Core;
using ReplayMod.UI;
using ReplayMod.Misc;
using UnityEngine;

namespace ReplayMod.Director;

public partial class ReplayDirector
{
    private readonly ReplayStudioLights studioLights=new ReplayStudioLights();
    private bool studioLightProjectReady;
    private int selectedStudioLight=-1;
    public int SelectedStudioLightIndex=>selectedStudioLight>=0&&selectedStudioLight<Project.studioLights.Count?selectedStudioLight:-1;
    public StudioLightSettings SelectedStudioLight=>SelectedStudioLightIndex>=0?Project.studioLights[SelectedStudioLightIndex]:null;
    public int StudioLightCount=>Project.studioLights.Count;
    public bool StudioLightEditorVisible{get;private set;}
    public bool StudioLightMarkers{get;private set;}=true;
    public bool StudioLightDragging=>studioLights.Dragging;
    public bool StudioLightPointerCaptured=>studioLights.PointerCaptured(SceneSingleton<CameraStateManager>.i?.mainCamera,
        StudioLightCanDrag,TrailerSuiteUi.Instance?.PointerOverUi==true||TrailerSuiteUi.Instance?.HasInputFocus==true);
    public bool StudioLightDynamicLightsAvailable{get{ReplayStudioLightCapabilities.Refresh();return ReplayStudioLightCapabilities.DynamicLightsAvailable;}}
    public bool StudioLightShadowsAvailable{get{ReplayStudioLightCapabilities.Refresh();return ReplayStudioLightCapabilities.ShadowsAvailable;}}
    public bool StudioLightSoftShadowsAvailable{get{ReplayStudioLightCapabilities.Refresh();return ReplayStudioLightCapabilities.SoftShadowsAvailable;}}
    public bool StudioLightCookiesAvailable{get{ReplayStudioLightCapabilities.Refresh();return ReplayStudioLightCapabilities.CookiesAvailable;}}
    public string StudioLightCapabilities{get{ReplayStudioLightCapabilities.Refresh();return ReplayStudioLightCapabilities.Summary;}}
    private bool CanRenderStudioLights=>studioLightProjectReady&&isActiveAndEnabled&&replayPath!=null&&ReplayRuntime.IsPlayback&&!GameManager.IsHeadless&&!SceneHelper.IsTransitionPending&&
        ReplayManager.i?.IsTransitioning!=true&&ReplayManager.i?.Playback!=null&&ReplayManager.i.Playback.enabled&&!ReplayManager.i.Playback.IsSeeking;
    private bool CanEditStudioLights=>CanRenderStudioLights&&StudioLightEditorVisible&&StudioLightMarkers&&!Clean&&!DynamicMap.mapMaximized&&!GameplayUI.GameIsPaused;
    public bool StudioLightCanDrag
    {
        get
        {
            var camera=SceneSingleton<CameraStateManager>.i;
            return CanEditStudioLights&&camera!=null&&camera.currentState==camera.freeState&&Project.rig==0&&!CameraKeysEnabled&&!RailActive&&ReplayMod.Camera.CameraManager.i?.IsFlying!=true;
        }
    }

    public void SetStudioLightEditorVisible(bool value)
    {
        if(StudioLightEditorVisible==value)return;StudioLightEditorVisible=value;
        if(!value)studioLights.CancelDrag();UpdateStudioLights();
    }
    public void SetStudioLightMarkers(bool value)
    {StudioLightMarkers=value;if(!value)studioLights.CancelDrag();UpdateStudioLights();}
    public void SelectStudioLight(int index)
    {
        if(index<0||index>=Project.studioLights.Count){selectedStudioLight=-1;studioLights.CancelDrag();return;}
        selectedStudioLight=index;
    }
    public void AddStudioLight(int type)
    {
        if(!LightCamera(out var camera))return;
        if(Project.studioLights.Count>=StudioLightSettings.MaximumLights){Status="A camera project can contain up to 16 studio lights. Remove one to add another.";return;}
        if(type<0||type>1)return;
        var item=new StudioLightSettings{type=type,name=(type==1?"Spot ":"Point ")+(Project.studioLights.Count+1)};
        SetLightPose(item,camera.transform.position+camera.transform.forward*15,camera.transform.rotation);
        Project.studioLights.Add(item);selectedStudioLight=Project.studioLights.Count-1;
        StudioLightMarkers=true;UpdateStudioLights();Status="Light added 15 m ahead. Drag its box to move it; hold the box and scroll to change depth. Save project to keep it.";
    }
    public void DuplicateStudioLight()
    {
        var item=SelectedStudioLight;if(item==null||!LightCamera(out var camera))return;
        if(Project.studioLights.Count>=StudioLightSettings.MaximumLights){Status="The project already contains 16 studio lights.";return;}
        var copy=item.Duplicate();var right=camera.transform.right*2;copy.x+=right.x;copy.y+=right.y;copy.z+=right.z;
        if(!copy.Valid())return;
        Project.studioLights.Add(copy);selectedStudioLight=Project.studioLights.Count-1;UpdateStudioLights();Status="Light duplicated 2 m to the right.";
    }
    public void DeleteStudioLight()
    {
        int index=SelectedStudioLightIndex;if(index<0||!CanRenderStudioLights)return;
        studioLights.CancelDrag();Project.studioLights.RemoveAt(index);selectedStudioLight=Project.studioLights.Count==0?-1:Mathf.Min(index,Project.studioLights.Count-1);
        UpdateStudioLights();Status="Studio light removed. Save project to keep the change.";
    }
    public void PlaceStudioLightAtView()
    {
        var item=SelectedStudioLight;if(item==null||!LightCamera(out var camera))return;
        studioLights.CancelDrag();SetLightPose(item,camera.transform.position+camera.transform.forward*5,camera.transform.rotation);
        UpdateStudioLights();Status="Light placed 5 m ahead and aimed in the view direction.";
    }
    public void AimStudioLightFromView()
    {
        var item=SelectedStudioLight;if(item==null||!LightCamera(out var camera))return;
        SetLightRotation(item,camera.transform.rotation);UpdateStudioLights();Status="Light aimed in the current camera direction.";
    }
    public void NudgeStudioLight(int axis,float amount,bool local)
    {
        var item=SelectedStudioLight;if(item==null||axis<0||axis>2||!DirectorProject.Range(amount,-1000,1000)||!LightCamera(out var camera))return;
        Vector3 direction=axis==0?Vector3.right:axis==1?Vector3.up:Vector3.forward;
        if(local)direction=camera.transform.rotation*direction;
        var delta=direction*amount;
        if(!DirectorProject.Range(item.x+delta.x,-1e8,1e8)||!DirectorProject.Range(item.y+delta.y,-1e8,1e8)||!DirectorProject.Range(item.z+delta.z,-1e8,1e8))return;
        item.x+=delta.x;item.y+=delta.y;item.z+=delta.z;UpdateStudioLights();
    }
    public void RotateStudioLight(int axis,float degrees,bool local)
    {
        var item=SelectedStudioLight;if(item==null||axis<0||axis>2||!DirectorProject.Range(degrees,-360,360)||!CanRenderStudioLights)return;
        Vector3 direction=axis==0?Vector3.right:axis==1?Vector3.up:Vector3.forward;
        var current=new Quaternion(item.qx,item.qy,item.qz,item.qw);var turn=Quaternion.AngleAxis(degrees,direction);
        SetLightRotation(item,local?current*turn:turn*current);UpdateStudioLights();
    }
    public void ResetStudioLightRotation()
    {var item=SelectedStudioLight;if(item==null||!CanRenderStudioLights)return;SetLightRotation(item,Quaternion.identity);UpdateStudioLights();Status="Light rotation levelled to the world axes.";}
    private bool LightCamera(out UnityEngine.Camera camera)
    {
        camera=SceneSingleton<CameraStateManager>.i?.mainCamera;
        if(CanRenderStudioLights&&camera!=null)return true;
        Status="Open a replay and wait for seeking to finish before editing studio lights.";return false;
    }
    private static void SetLightPose(StudioLightSettings item,Vector3 position,Quaternion rotation)
    {var origin=Datum.originPosition;item.x=(double)position.x-origin.x;item.y=(double)position.y-origin.y;item.z=(double)position.z-origin.z;SetLightRotation(item,rotation);}
    private static void SetLightRotation(StudioLightSettings item,Quaternion rotation)
    {item.qx=rotation.x;item.qy=rotation.y;item.qz=rotation.z;item.qw=rotation.w;}
    private void UpdateStudioLights()
    {
        if(!ReplayRuntime.IsPlayback||SceneHelper.IsTransitionPending||ReplayManager.i?.IsTransitioning==true){studioLights.Clear();return;}
        studioLights.Update(Project.studioLights,SelectedStudioLightIndex,SceneSingleton<CameraStateManager>.i?.mainCamera,
            CanRenderStudioLights,CanEditStudioLights,StudioLightCanDrag,SelectStudioLight);
    }
    private void ClearStudioLights(bool resetSelection=true)
    {studioLights.Clear();if(resetSelection)selectedStudioLight=-1;}
}
