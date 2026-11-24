using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HarmonyLib;
using NuclearOption.SavedMission;
using ReplayMod.Camera;
using ReplayMod.Core;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Director;

[DefaultExecutionOrder(1000)]
public partial class ReplayDirector : MonoBehaviour
{
    public static ReplayDirector i;
    public DirectorProject Project {get;private set;} = new DirectorProject();
    public bool Clean {get;private set;}
    public string CleanCaptureHotkeyLabel => ConfigManager.ToggleCleanCapture?.Value.ToString() ?? "F8";
    public bool CameraKeysEnabled;
    public bool SpeedRampEnabled;
    public float ManualSpeed {get;private set;} = 1;
    public float FreeCameraSpeed {get;private set;} = 1;
    public float CurrentCameraFov=>CameraOrientation.Lens(SceneSingleton<CameraStateManager>.i?.mainCamera?.fieldOfView??Project.fov);
    public float CameraRollDegrees=>CameraRollMath.Normalize(SceneSingleton<CameraStateManager>.i?.transform.eulerAngles.z??freeCameraRoll);
    private float freeCameraRoll,trackingCameraRoll;
    private Vector3 chaseOffset;
    private float chaseYaw,chasePitch;
    private uint chaseTarget;
    private bool chaseAdjusted;
    private Unit chaseActor;
    private CameraStateManager rolledFreeCamera;
    private bool freeRollOwned;
    private readonly CameraRailState rail = new CameraRailState();
    public bool RailActive => rail.Active;
    public float RailSpeed => rail.Speed;
    public bool RailMatchesTargetSpeed {get;private set;}
    private double matchedRailTime;
    private float matchedRailSpeed;
    public float RailEffectiveSpeed=>RailMatchesTargetSpeed?matchedRailSpeed:RailSpeed;
    public bool RailFollowsView {get;private set;}
    public RailDirection RailDirection {get;private set;} = global::ReplayMod.Director.RailDirection.Forward;
    private CameraStateManager railCamera;
    private bool railOriginalInputs;
    private int frameRate=60;
    public int FrameRate {get=>frameRate;set=>frameRate=value==24 || value==30 || value==60 || value==120?value:60;}
    public bool IsBusy => ReplayManager.i?.Playback?.IsSeeking == true;
    public string Status="";
    private string replayPath, replayHash;
    private readonly PresentationOverrides presentation=new PresentationOverrides();
    private bool cameraOwned;
    private UnityEngine.Camera ownedCamera;
    private float originalFov, originalDesiredFov;
    private bool originalInputs;
    private readonly CameraRelativeTracking relativeTracking=new CameraRelativeTracking();
    private readonly CameraSubjectHold subjectHold=new CameraSubjectHold();
    private Quaternion trackingRotation;
    private double heldCameraX,heldCameraY,heldCameraZ;
    private Quaternion heldCameraRotation;
    private float heldCameraFov,trackingFov;
    private bool holdingSubject;
    private double lastCameraTime=double.NaN;
    private int lastRig=-1;
    private uint lastTarget;
    private readonly CameraTargetBinding<Unit> cameraTargets=new CameraTargetBinding<Unit>();
    private Unit pendingNativeTarget;
    private DynamicMap subscribedMap;
    private float lastDistance,lastHeight,lastOrbit,lastSmoothing,lastAspect;
    private Unit lastTrackingActor;
    private bool safeToAutoSave;
    private bool environmentPending;
    private float nextEnvironmentApply;
    private int cleanChangedFrame=-1;
    private bool cleanEnteredDuringGameplay;
    private void Awake() {i=this;gameObject.AddComponent<ReplayMod.Patches.ReplayDetailRenderOrder>();}
    private void OnEnable(){CameraStateManager.onFollowingUnitSet+=OnNativeCameraTarget;}
    private void OnDestroy(){ClearStudioLights();CameraStateManager.onFollowingUnitSet-=OnNativeCameraTarget;SubscribeMap(null);if(i==this)i=null;}
    public IEnumerable<KeyValuePair<uint,Unit>> Actors() => ReplayManager.i?.Playback?.Actors() ?? Enumerable.Empty<KeyValuePair<uint,Unit>>();
    public bool IsActor(uint id,Unit unit) => ReplayManager.i?.Playback?.FindActor(id)==unit;
    public async Task Open(string path)
    {
        ClearStudioLights();studioLightProjectReady=false;
        CancelEventShot();ResetCameraSmoothing();
        CancelCameraTake();
        StopRail();
        cameraTargets.Reset();pendingNativeTarget=null;ResetRollOwnership();
        replayPath=path;
        safeToAutoSave=false;
        replayHash=await Task.Run(()=> {using(var f=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","");});
        Project=new DirectorProject {replayHash=replayHash};safeToAutoSave=true;studioLightProjectReady=true;
        SetPlaybackSpeed(1);
        if(File.Exists(path+".director.json"))Load();
        else Status="New camera project. Save project to keep edits.";
        // A new replay scene creates a camera with the game's default input
        // multiplier. Restore the studio's selected speed after it has loaded.
        ApplyMotionSmoothing();SetFreeCameraSpeed(FreeCameraSpeed);RestoreCameraLens(Project.fov);
    }
    public void Save()
    {
        if(replayPath==null || !ReplayRuntime.IsPlayback) return;
        try
        {
            if(Project.rig==0&&!CameraKeysEnabled)Project.fov=CurrentCameraFov;
            Project.path=CameraManager.i.ExportPath();Project.firstPoint=CameraManager.i.ExportFirstPoint();Project.replayHash=replayHash;
            if(!Project.Validate(ReplayManager.i.GetDuration()))throw new InvalidDataException("Project contains invalid or overlapping keyframes/shots");
            var path=replayPath+".director.json"; var temp=path+".tmp";
            File.WriteAllText(temp,ProjectJsonCodec.Write(Project));
            if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
            safeToAutoSave=true;Status="Camera project saved.";
        }
        catch(Exception e){Status="Save failed: "+e.Message;Plugin.logger.LogError(e);}
    }
    public void Load()
    {
        CancelEventShot();ResetCameraSmoothing();
        CancelCameraTake();
        StopRail();
        try
        {
            var path=replayPath+".director.json";
            if(new FileInfo(path).Length>ProjectJsonCodec.MaximumBytes)throw new InvalidDataException("Project is too large");
            var project=ProjectJsonCodec.Read(File.ReadAllText(path));
            if(project==null || project.replayHash!=replayHash || !project.Validate(ReplayManager.i.GetDuration()))throw new InvalidDataException("Invalid project or replay fingerprint mismatch");
            ClearStudioLights();presentation.RestorePlayback();ReleaseCamera();cameraTargets.Reset();pendingNativeTarget=null;ResetRollOwnership();Project=project;CameraManager.i.ImportPath(project.path,project.firstPoint);safeToAutoSave=true;
            CameraKeysEnabled=false;SpeedRampEnabled=false;ApplyMotionSmoothing();RestoreCameraLens(Project.fov);Status="Camera project loaded.";
        }
        catch(Exception e){safeToAutoSave=false;Status="Load failed: "+e.Message;Plugin.logger.LogWarning(Status);}
    }
    public void Close()
    {
        studioLightProjectReady=false;ClearStudioLights();StudioLightEditorVisible=false;
        CancelEventShot();ResetCameraSmoothing();
        CancelCameraTake();
        StopRail();
        cameraTargets.Reset();pendingNativeTarget=null;SubscribeMap(null);ResetRollOwnership();
        if(safeToAutoSave)Save();
        SetClean(false);presentation.Restore();ReleaseCamera();CameraKeysEnabled=false;SpeedRampEnabled=false;
        replayPath=null;replayHash=null;Project=new DirectorProject();ClearStudioLights();ejectionPlayer=null;ejectionBirth=double.NaN;
    }
    public void SetClean(bool clean)
    {
        // whut
        if(clean && !MissionManager.IsRunning)
        {Status="Hide UI is available during an active mission or replay.";return;}
        if(!clean&&TakeActive)CancelCameraTake();
        if(Clean==clean)return;
        if(clean && !CleanCaptureImguiGuard.RefreshTargets())Status="Some mod overlays could not be hidden. Check the game log before capture.";
        Clean=clean;
        cleanChangedFrame=Time.frameCount;
        cleanEnteredDuringGameplay=clean && MissionManager.IsRunning;
        if(clean)
        {
            GUIUtility.keyboardControl=0;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }
        presentation.UpdateCapture(clean);
        CameraManager.i?.CaptureEditorVisibility(clean);
        ReplayManager.i?.SetCaptureOverlayHidden(clean);
        var playback=ReplayManager.i?.Playback;
        var camera=SceneSingleton<CameraStateManager>.i?.mainCamera;
        Plugin.SafeLog("Clean capture "+(clean?"on":"off")+"; replay="+ReplayRuntime.IsPlayback+
            "; seeking="+(playback?.IsSeeking??false)+"; reloading="+(playback?.IsReloading??false)+
            "; reconstructionCover="+(ReplayMod.UI.TrailerSuiteUi.Instance?.ReconstructionCoverVisible??false)+
            "; cameraEnabled="+(camera!=null&&camera.enabled)+"; cameraActive="+(camera!=null&&camera.gameObject.activeInHierarchy)+
            "; cameraHasTargetTexture="+(camera!=null&&camera.targetTexture!=null));
        UpdateStudioLights();
        if(!clean)
        {
            CursorManager.Refresh();
            Cursor.visible=CursorManager.Visible;
        }
    }
    public bool AssignCleanCaptureHotkey(KeyCode key,out string reason)
    {
        if(ConfigManager.ToggleCleanCapture==null){reason="Capture settings are not ready yet.";return false;}
        if(!ConfigManager.CanAssignReplayHotkey(key,ConfigManager.ToggleCleanCapture,out reason))return false;
        ConfigManager.ToggleCleanCapture.Value=new BepInEx.Configuration.KeyboardShortcut(key);
        reason="Hide or restore all UI: "+CleanCaptureHotkeyLabel+". Check for conflicts with the game's own bindings.";
        Status=reason;return true;
    }
    private void OnApplicationQuit(){if(safeToAutoSave && replayPath!=null)Save();}
    private void OnDisable() {ClearStudioLights();StudioLightEditorVisible=false;CancelEventShot();CancelCameraTake();CameraStateManager.onFollowingUnitSet-=OnNativeCameraTarget;SubscribeMap(null);StopRail();SetClean(false);presentation.Restore();ReleaseCamera();}
    private void Update()
    {
        UpdateCameraTake();
        if(Clean && cleanEnteredDuringGameplay && !ReplayRuntime.IsPlayback && !MissionManager.IsRunning && ReplayManager.i?.IsTransitioning!=true)
            SetClean(false);
        bool captureShortcut=ConfigManager.ToggleCleanCapture?.Value.GetDown()==true;
        if(captureShortcut && cleanChangedFrame!=Time.frameCount && (Clean || (MissionManager.IsRunning && !MainMenu.BlockHotkeys)))SetClean(!Clean);
        if(ReplayRuntime.IsPlayback && ReplayManager.i?.Playback!=null)
        {
            ApplyMotionSmoothing();
            SubscribeMap(SceneSingleton<DynamicMap>.i);
            ConsumeNativeCameraTarget();
            UpdateEventShot();
            UpdateChaseInput();
            if(!captureShortcut && !IsBusy && !MainMenu.BlockHotkeys && GUIUtility.keyboardControl==0)
            {
                if(Input.GetKeyDown(KeyCode.Period)){CancelCameraTake();ReplayManager.i.Playback.StepForward(1.0/frameRate);}
                if(ConfigManager.CameraLevel?.Value.GetDown()==true)LevelCamera();
                else
                {
                    float turn=(ConfigManager.CameraRollRight?.Value.IsPressed()==true?1:0)-(ConfigManager.CameraRollLeft?.Value.IsPressed()==true?1:0);
                    if(turn!=0)ApplyRollChange(CameraRollMath.Advance(0,turn,Time.unscaledDeltaTime),false);
                }
            }
            if(!ReplayManager.i.Playback.IsSeeking)
                ReplayManager.i.Playback.SetPlaybackSpeed(SpeedRampEnabled?DirectorProject.SpeedAt(Project.speeds,ReplayManager.i.GetCurrentVirtualTime(),ManualSpeed):ManualSpeed);
            if(RailActive)PrepareRailFrame();
        }
        else {CancelEventShot();pendingNativeTarget=null;SubscribeMap(null);StopRail();}
    }
    private void UpdateChaseInput()
    {
        if(Project.rig!=1 || CameraKeysEnabled || IsBusy || GameplayUI.GameIsPaused || MainMenu.BlockHotkeys || MainMenu.HasInputFocus || MainMenu.IsPointerOverUI || GUIUtility.keyboardControl!=0)
            return;
        var target=ResolveCameraActor(Project.target);var camera=SceneSingleton<CameraStateManager>.i;
        if(target==null || camera==null)return;
        if(chaseTarget!=Project.target || !ReferenceEquals(chaseActor,target)){chaseTarget=Project.target;chaseActor=target;chaseOffset=Vector3.zero;chaseYaw=chasePitch=0;chaseAdjusted=false;}
        float horizontal=(Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0);
        float forward=(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0);
        float vertical=(Input.GetKey(KeyCode.R)?1:0)-(Input.GetKey(KeyCode.F)?1:0);
        var movement=new Vector3(horizontal,vertical,forward);
        if(movement.sqrMagnitude>0)
        {
            movement=Vector3.ClampMagnitude(movement,1)*Mathf.Clamp(Time.unscaledDeltaTime,0,.1f)*FreeCameraSpeed*10;
            chaseOffset+=target.transform.InverseTransformVector(camera.transform.rotation*movement);
            chaseOffset=Vector3.ClampMagnitude(chaseOffset,2000);chaseAdjusted=true;
        }
        if(Input.GetMouseButton(1))
        {
            float x=Input.GetAxis("Mouse X"),y=Input.GetAxis("Mouse Y");
            if(x!=0||y!=0){chaseYaw+=x*2;chasePitch=Mathf.Clamp(chasePitch-y*2,-89,89);chaseAdjusted=true;}
        }
    }
    private void LateUpdate()
    {
        presentation.UpdateCapture(Clean);
        CameraManager.i?.CaptureEditorVisibility(Clean);
        ReplayManager.i?.SetCaptureOverlayHidden(Clean);
        if(!ReplayRuntime.IsPlayback || ReplayManager.i.Playback==null) {ClearStudioLights();StopRail();presentation.RestorePlayback();ReleaseCamera();return;}
        if(ReplayManager.i.Playback.IsSeeking){UpdateStudioLights();StopRail();RestoreCameraVisibility();return;}
        presentation.Update(this);
        if(!Project.environment.enabled)environmentPending=false;
        if(environmentPending && (Time.realtimeSinceStartup>=nextEnvironmentApply || !Input.GetMouseButton(0)))
        {presentation.ApplyEnvironment(Project.environment);environmentPending=false;nextEnvironmentApply=Time.realtimeSinceStartup+.25f;}
        ReplayManager.i.Playback.RefreshMotionForCamera();
        UpdateCamera(ReplayManager.i.GetCurrentVirtualTime());
        ApplyFreeCameraRoll();
        UpdateRail();
        UpdateCameraVisibility();
        UpdateStudioLights();
    }
    public void UpdateCamera(double time)
    {
        var manager=SceneSingleton<CameraStateManager>.i;
        if(manager==null || manager.mainCamera==null)return;
        if(TryApplyEjectionCamera(manager))return;
        bool driven=Project.rig!=0 || CameraKeysEnabled;
        if(!driven) {ReleaseCamera();return;}
        StopRail();
        Vector3 pos;Quaternion rot;float fov=Project.fov;
        if(CameraKeysEnabled && Project.cameras.Count>0)
        {
            CancelEventShot();subjectHold.Reset();holdingSubject=false;
            if(!CameraPathSampler.TrySample(Project.cameras,time,PathSmooth,PathSmoothness,Resolve,out var sample))
            {CancelCameraTake();ReleaseCamera();Status="Camera path target is absent or its points are invalid.";return;}
            EnsureDrivenCamera(manager);
            pos=sample.position;rot=PathAnimatesRotation?sample.rotation:manager.transform.rotation;fov=PathAnimatesFov?sample.fov:CurrentCameraFov;
        }
        else
        {
            var target=ResolveCameraActor(Project.target);
            if(target==null || !RigAccepts(Project.rig,target))
            {HandleMissingCameraSubject(manager,time);return;}
            EnsureDrivenCamera(manager);
            if(Project.rig==5)
            {
                if(!ReplayInteriorPresentation.TryGetCockpitEye(ReplayManager.i.Playback,Project.target,target as Aircraft,out pos,out rot,Project.cockpitSeat))
                {HandleMissingCameraSubject(manager,time);return;}
            }
            else if(Project.rig==6||Project.rig==7)
            {
                if(!TryFramingCamera(manager,target,time,out pos,out rot))
                {
                    string framingStatus=Status;int rig=Project.rig;
                    HandleMissingCameraSubject(manager,time);
                    if(Project.rig==rig&&!holdingSubject)Status=framingStatus;
                    return;
                }
            }
            else
            {
            var tr=target.transform;Vector3 offset;
            if(Project.rig==2) {float angle=(float)time*Project.orbitRate*Mathf.Deg2Rad;offset=new Vector3(Mathf.Sin(angle)*Project.distance,Project.height,Mathf.Cos(angle)*Project.distance);}
            else if(Project.rig==3)offset=new Vector3(Project.distance,Project.height,0);
            else offset=new Vector3(0,Project.height,-Project.distance);
            if(Project.rig==1&&chaseAdjusted)offset+=chaseOffset;
            bool reset=double.IsNaN(lastCameraTime)||time<lastCameraTime||time-lastCameraTime>.5 || lastRig!=Project.rig||lastTarget!=Project.target||!ReferenceEquals(lastTrackingActor,target)||lastDistance!=Project.distance||lastHeight!=Project.height||lastOrbit!=Project.orbitRate||lastSmoothing!=Project.smoothing;
            var desiredOffset=tr.TransformVector(offset);
            if(!relativeTracking.Update(desiredOffset.x,desiredOffset.y,desiredOffset.z,time,Project.smoothing,reset))
            {ReleaseCamera();Status="Tracking settings are invalid. Choose a valid camera preset.";return;}
            pos=tr.position+new Vector3((float)relativeTracking.X,(float)relativeTracking.Y,(float)relativeTracking.Z);
            if(Project.rig==1&&chaseAdjusted)pos=tr.position+desiredOffset;
            Vector3 direction=tr.position-pos;rot=direction.sqrMagnitude>.01f?Quaternion.LookRotation(direction,tr.up):tr.rotation;
            if(!reset && !(Project.rig==1&&chaseAdjusted))rot=Quaternion.Slerp(trackingRotation,rot,(float)CameraRelativeTracking.Blend(Project.smoothing,Math.Max(0,time-lastCameraTime)));
            if(ReplayMod.Patches.ReplayCameraCollision.Linecast(tr.position,pos,out var obstruction,(int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask))
            {pos=obstruction.point+obstruction.normal*.75f;direction=tr.position-pos;if(direction.sqrMagnitude>.01f)rot=Quaternion.LookRotation(direction,tr.up);}
            if(Project.rig==1&&chaseAdjusted)rot=rot*Quaternion.Euler(chasePitch,chaseYaw,0);
            trackingRotation=rot;
            if(AutoFrameSubject&&CameraSubjectFraming.TryFov(target.maxRadius,Vector3.Distance(tr.position,pos),manager.mainCamera.aspect,out var framedFov,Vector3.Angle(rot*Vector3.forward,direction)))
            {
                bool lensReset=reset||lastAspect!=manager.mainCamera.aspect;
                float blend=lensReset?1:(float)CameraRelativeTracking.Blend(Project.smoothing,Math.Max(0,time-lastCameraTime));
                // Widen immediately to accommodate the subject while the aim
                // eases through a turn; narrowing can remain cinematic.
                trackingFov=lensReset||framedFov>trackingFov?framedFov:Mathf.Lerp(trackingFov,framedFov,blend);fov=trackingFov;
            }
            else trackingFov=fov;
            }
            lastTrackingActor=target;
        }
        lastCameraTime=time;lastRig=Project.rig;lastTarget=Project.target;
        lastDistance=Project.distance;lastHeight=Project.height;lastOrbit=Project.orbitRate;
        lastSmoothing=Project.smoothing;lastAspect=manager.mainCamera.aspect;
        var rotation=CameraKeysEnabled?rot:rot*Quaternion.Euler(0,0,trackingCameraRoll);
        manager.transform.SetPositionAndRotation(pos,rotation);manager.CheckOriginShift();manager.allowInputs=false;ApplyLens(manager,fov);
        if(!CameraKeysEnabled)
        {
            var origin=Datum.originPosition;var position=manager.transform.position;
            heldCameraX=(double)position.x-origin.x;heldCameraY=(double)position.y-origin.y;heldCameraZ=(double)position.z-origin.z;
            heldCameraRotation=rotation;heldCameraFov=fov;subjectHold.Observe();holdingSubject=false;
        }
    }
    private bool Resolve(CameraKey key,out Vector3 pos,out Quaternion rot)
    {
        pos=new GlobalPosition(key.x,key.y,key.z).ToLocalPosition();rot=new Quaternion(key.qx,key.qy,key.qz,key.qw);
        if(!key.relative)return true;
        var unit=ResolveCameraActor(key.target);if(unit==null)return false;
        pos=unit.transform.TransformPoint(new Vector3(key.x,key.y,key.z));rot=unit.transform.rotation*rot;return true;
    }
    public void ResetCameraSmoothing() {lastCameraTime=double.NaN;relativeTracking.Reset();subjectHold.Reset();holdingSubject=false;lastTrackingActor=null;}
    public void PrepareSeek()
    {
        RestoreCameraVisibility();
        if(!eventShot.Preparing)CancelEventShot();
        StopRail();cameraTargets.Reset();pendingNativeTarget=null;
        if(Project.rig==0&&!CameraKeysEnabled)OwnFreeCameraRoll(SceneSingleton<CameraStateManager>.i);
        presentation.Update(this);ApplyMotionSmoothing();SetFreeCameraSpeed(FreeCameraSpeed);RestoreCameraLens(Project.fov);ResetCameraSmoothing();
    }
    private Unit ResolveCameraActor(uint id)
    {
        var current=ReplayManager.i?.Playback?.FindActor(id);
        return current!=null&&cameraTargets.TryResolve(id,current,out var actor)?actor:null;
    }
    private static bool RigAccepts(int rig,Unit target)=>(rig==3||rig==5||rig==6||rig==7)?target is Aircraft:rig!=4||target is Missile;
    public bool SelectCameraTarget(uint id)
    {
        if(!ReplayRuntime.IsPlayback||IsBusy)return false;
        CancelEventShot();
        CancelCameraTake();
        var actor=ReplayManager.i?.Playback?.FindActor(id);
        if(actor==null){Status="That camera target is absent at this replay moment.";return false;}
        RestoreEjectionCamera();ejectionPlayer=null;ejectionBirth=double.NaN;
        pendingNativeTarget=null;cameraTargets.Select(id,actor);
        if(Project.target!=id)Project.cockpitSeat=0;
        Project.target=id;Project.lightTarget=id;ResetCameraSmoothing();return true;
    }
    public bool SelectCameraRig(int rig)
    {
        if(!ReplayRuntime.IsPlayback||IsBusy||rig<0||rig>7)return false;
        CancelEventShot();
        CancelCameraTake();
        ConsumeNativeCameraTarget();
        if(rig==0){EnterFreeCamera();return true;}
        var target=ResolveCameraActor(Project.target);
        if(target==null||!RigAccepts(rig,target))
        {Status=rig==3?"Select a live aircraft before choosing Wingtip.":rig==5?"Select a live aircraft before choosing Cockpit view.":rig==4?"Select a live missile before choosing Missile follow.":"Select a live camera target first.";return false;}
        if(rig==5)
        {
            if(!ReplayInteriorPresentation.TryGetCockpitEye(ReplayManager.i.Playback,Project.target,target as Aircraft,out _,out _,Project.cockpitSeat))
            {Status="This aircraft has no available attached cockpit viewpoint.";return false;}
            Project.detailedCockpit=true;Project.reduceCloseClipping=true;
        }
        StopRail();Project.rig=rig;CameraKeysEnabled=false;chaseTarget=0;chaseActor=null;chaseOffset=Vector3.zero;chaseYaw=chasePitch=0;chaseAdjusted=false;CameraManager.i?.StopCamFlight();ResetCameraSmoothing();return true;
    }
    private void SubscribeMap(DynamicMap map)
    {
        if(ReferenceEquals(subscribedMap,map))return;
        if(!ReferenceEquals(subscribedMap,null))subscribedMap.onUnitSelected-=OnNativeCameraTarget;
        subscribedMap=map;
        if(subscribedMap!=null)subscribedMap.onUnitSelected+=OnNativeCameraTarget;
    }
    private void OnNativeCameraTarget(Unit actor)
    {
        // SetFollowingUnit raises its event before updating native state. Queue
        // the selection; never switch native states recursively in that event.
        // Internal SetCameraPosition clears followingUnit and emits null.
        if(actor!=null&&ReplayRuntime.IsPlayback&&!IsBusy)pendingNativeTarget=actor;
    }
    private void ConsumeNativeCameraTarget()
    {
        if(IsBusy){pendingNativeTarget=null;return;}
        var actor=pendingNativeTarget;pendingNativeTarget=null;if(actor==null)return;
        foreach(var candidate in Actors())
            if(ReferenceEquals(candidate.Value,actor))
            {
                if(SelectCameraTarget(candidate.Key))Status="Camera target selected from the map.";
                return;
            }
        if(cameraOwned)EnterFreeCamera();
        Status="That map object is not a live recorded camera target.";
    }
    private void EnsureDrivenCamera(CameraStateManager manager)
    {
        if(!cameraOwned||ownedCamera!=manager.mainCamera)
        {
            ReleaseCamera();ownedCamera=manager.mainCamera;originalFov=ownedCamera.fieldOfView;originalDesiredFov=manager.desiredFOV;originalInputs=manager.allowInputs;cameraOwned=true;
            CameraManager.i?.StopCamFlight();lastCameraTime=double.NaN;
        }
        // Map selections and native view keys may replace the native state after
        // we acquired the camera. Detach again before applying the studio pose.
        if(manager.currentState!=manager.freeState||manager.followingUnit!=null||manager.transform.parent!=null)
        {manager.GetCameraPosition(out var current);manager.SetCameraPosition(current);lastCameraTime=double.NaN;}
        manager.allowInputs=false;manager.cameraVelocity=Vector3.zero;
    }
    public void QueueEnvironmentChange(){environmentPending=true;}
    private void ReleaseCamera()
    {
        RestoreEjectionCamera();
        if(cameraOwned)
        {
            if(ownedCamera!=null)ownedCamera.fieldOfView=originalFov;
            var manager=SceneSingleton<CameraStateManager>.i;if(manager!=null && manager.mainCamera==ownedCamera)
            {manager.GetCameraPosition(out var pose);manager.SetCameraPosition(pose);manager.desiredFOV=originalDesiredFov;manager.allowInputs=originalInputs;manager.cameraVelocity=Vector3.zero;}
        }
        cameraOwned=false;ownedCamera=null;lastCameraTime=double.NaN;
    }
    public void CaptureCameraKey(bool relative, bool ease, float roll)
    {
        CancelEventShot();
        CancelCameraTake();
        if(!ReplayRuntime.IsPlayback || ReplayManager.i?.Playback==null || IsBusy)return;
        var manager=SceneSingleton<CameraStateManager>.i;if(manager==null)return;
        if(float.IsNaN(roll) || float.IsInfinity(roll))return;
        manager.GetCameraPosition(out var pose);var pos=pose.Position.ToLocalPosition();var rot=pose.Rotation*Quaternion.Euler(0,0,Mathf.Clamp(roll,-180,180));
        var target=ReplayManager.i.Playback.FindActor(Project.target);
        if(relative && target==null){Status="Select a live target for a relative key.";return;}
        if(relative){pos=target.transform.InverseTransformPoint(pos);rot=Quaternion.Inverse(target.transform.rotation)*rot;}
        else {var global=pos.ToGlobalPosition();pos=new Vector3(global.x,global.y,global.z);}
        double time=ReplayManager.i.GetCurrentVirtualTime();Project.cameras.RemoveAll(k=>Math.Abs(k.time-time)<.00001);
        Project.fov=CurrentCameraFov;
        Project.cameras.Add(new CameraKey {time=time,x=pos.x,y=pos.y,z=pos.z,qx=rot.x,qy=rot.y,qz=rot.z,qw=rot.w,fov=Project.fov,easing=ease?1:0,relative=relative,target=Project.target});
        Project.cameras.Sort((a,b)=>a.time.CompareTo(b.time));Status="Camera key captured.";
    }
    public void PausePlayback() => ReplayManager.i?.Playback?.Pause();
    public void PlayPlayback() => ReplayManager.i?.Playback?.Resume();
    public void EnterFreeCamera()
    {
        if(!ReplayRuntime.IsPlayback || IsBusy)return;
        CancelEventShot();
        CancelCameraTake();
        var manager=SceneSingleton<CameraStateManager>.i;if(manager==null||manager.mainCamera==null)return;
        manager.GetCameraPosition(out var pose);float lens=CurrentCameraFov;
        StopRail();
        ReleaseCamera();Project.rig=0;CameraKeysEnabled=false;
        CameraManager.i?.StopCamFlight();
        manager.SetCameraPosition(pose);manager.allowInputs=true;manager.cameraVelocity=Vector3.zero;
        OwnFreeCameraRoll(manager,CameraRollMath.Normalize(pose.Rotation.eulerAngles.z));
        RestoreCameraLens(lens);
        SetFreeCameraSpeed(FreeCameraSpeed);ResetCameraSmoothing();
    }
    public void SetCameraFov(float fov)
    {
        if(!ReplayRuntime.IsPlayback||IsBusy||float.IsNaN(fov)||float.IsInfinity(fov))return;
        if(holdingSubject&&!CameraKeysEnabled)EnterFreeCamera();
        Project.autoFrameSubject=false;
        // A manual lens change wins over animated camera keys. Tracking presets
        // continue to use the new project lens without changing their positions.
        if(CameraKeysEnabled){Project.animatePathFov=false;Status="Manual zoom enabled; the path keeps moving. Turn Animate FOV / zoom on to use saved point lenses.";}
        RestoreCameraLens(fov);
    }
    public void RestoreCameraLens(float fov)
    {
        Project.fov=CameraOrientation.Lens(fov,Project.fov);
        var manager=SceneSingleton<CameraStateManager>.i;if(manager?.mainCamera!=null)ApplyLens(manager,Project.fov);
    }
    private static void ApplyLens(CameraStateManager manager,float fov)
    {
        // Native wheel zoom is an offset from desiredFOV. Reset that offset so
        // the next free-state update cannot undo an explicit studio lens value.
        if(manager.freeState!=null)manager.freeState.FOVAdjustment=0;
        manager.SetDesiredFoV(CameraOrientation.Lens(fov),CameraOrientation.Lens(fov));
    }
    public void LevelCamera()
    {
        if(!ReplayRuntime.IsPlayback||IsBusy)return;
        var manager=SceneSingleton<CameraStateManager>.i;if(manager?.mainCamera==null)return;
        manager.GetCameraPosition(out var pose);float lens=CurrentCameraFov;
        Vector3 forward=pose.Rotation*Vector3.forward;
        float yaw=CameraOrientation.FlatYaw(forward.x,forward.z,pose.Rotation.eulerAngles.y);
        EnterFreeCamera();
        manager.SetCameraPosition(new PositionRotation {Position=pose.Position,Rotation=Quaternion.Euler(0,yaw,0)});
        trackingCameraRoll=0;OwnFreeCameraRoll(manager,0);
        manager.allowInputs=true;manager.cameraVelocity=Vector3.zero;RestoreCameraLens(lens);ResetCameraSmoothing();
        Status="Camera level: horizontal view, with the same position, heading and lens.";
    }
    public void RollCamera(float degrees)=>ApplyRollChange(degrees,true);
    private void ApplyRollChange(float degrees,bool notify)
    {
        if(!ReplayRuntime.IsPlayback||IsBusy||float.IsNaN(degrees)||float.IsInfinity(degrees))return;
        var manager=SceneSingleton<CameraStateManager>.i;if(manager?.mainCamera==null)return;
        if(holdingSubject)EnterFreeCamera();
        if(CameraKeysEnabled)EnterFreeCamera();
        if(Project.rig!=0)
            trackingCameraRoll=CameraRollMath.Add(trackingCameraRoll,degrees);
        else
        {
            if(manager.currentState!=manager.freeState)EnterFreeCamera();
            if(!freeRollOwned||rolledFreeCamera!=manager)OwnFreeCameraRoll(manager);
            freeCameraRoll=CameraRollMath.Add(freeCameraRoll,degrees);ApplyFreeCameraRoll();
        }
        if(notify)Status="Camera roll adjusted. Level camera removes pitch and roll.";
    }
    private void OwnFreeCameraRoll(CameraStateManager manager,float? angle=null)
    {
        rolledFreeCamera=manager;freeRollOwned=manager!=null;
        if(manager!=null)freeCameraRoll=CameraRollMath.Normalize(angle??manager.transform.eulerAngles.z);
    }
    private void ResetRollOwnership()
    {rolledFreeCamera=null;freeRollOwned=false;freeCameraRoll=0;trackingCameraRoll=0;}
    private void ApplyFreeCameraRoll()
    {
        if(!freeRollOwned||Project.rig!=0||CameraKeysEnabled)return;
        var manager=SceneSingleton<CameraStateManager>.i;
        if(manager==null||manager!=rolledFreeCamera||manager.currentState!=manager.freeState)
        {freeRollOwned=false;rolledFreeCamera=null;return;}
        // Stop levelling my camera, Mitch. I'm trying to be cinematic.
        // Native free-state mouse aiming always writes zero roll. Reapply only
        // the selected bank afterwards, preserving its updated pitch and yaw.
        var angles=manager.transform.eulerAngles;
        manager.transform.rotation=Quaternion.Euler(angles.x,angles.y,freeCameraRoll);
    }
    public void SetPlaybackSpeed(float speed)
    {
        if(float.IsNaN(speed) || float.IsInfinity(speed))return;
        ManualSpeed=Mathf.Clamp(speed,.1f,PlaybackControlState.MaximumSpeed);
        SpeedRampEnabled=false;
        ReplayManager.i?.Playback?.SetPlaybackSpeed(ManualSpeed);
    }
    public void SetFreeCameraSpeed(float speed)
    {
        if(float.IsNaN(speed) || float.IsInfinity(speed))return;
        StopRail();
        FreeCameraSpeed=Mathf.Clamp(speed,.05f,5);
        var manager=SceneSingleton<CameraStateManager>.i;
        if(manager!=null){manager.SetDesiredSpeed(FreeCameraSpeed,manager.desiredRotSpeed);manager.cameraVelocity=Vector3.zero;}
    }
    public void SetRailSpeed(float speed) {rail.SetSpeed(speed);RailMatchesTargetSpeed=false;}
    public void SetRailMatchesTargetSpeed(bool match)
    {
        if(match&&ResolveCameraActor(Project.target)==null){Status="Select a live camera target before matching its speed.";return;}
        RailMatchesTargetSpeed=match;matchedRailTime=ReplayManager.i?.GetCurrentVirtualTime()??0;matchedRailSpeed=0;
        Status=match?"Rail matches the target's displayed speed. You still choose its direction.":"Rail uses the manual speed.";
    }
    public void SetRailFollowsView(bool follow)
    {
        RailFollowsView=follow;
        // Switching back to a straight rail locks the direction at this moment.
        if(RailActive)UpdateRailDirection();
    }
    public void SetRailDirection(RailDirection direction)
    {
        if(!Enum.IsDefined(typeof(RailDirection),direction))return;
        RailDirection=direction;
        if(RailActive)UpdateRailDirection();
    }
    public void StartRail()
    {
        if(!ReplayRuntime.IsPlayback || ReplayManager.i?.Playback?.enabled!=true || IsBusy)
        {Status="Open a replay and wait for seeking to finish before starting a rail move.";return;}
        var manager=SceneSingleton<CameraStateManager>.i;
        if(manager==null || manager.mainCamera==null){Status="The replay camera is not ready yet.";return;}
        EnterFreeCamera();
        Vector3 direction=GetRailDirection(manager);
        var position=manager.transform.position;var origin=Datum.originPosition;
        if(!rail.Start((double)position.x-origin.x,(double)position.y-origin.y,(double)position.z-origin.z,direction.x,direction.y,direction.z))
        {Status="The camera position or rail direction is invalid.";return;}
        railCamera=manager;railOriginalInputs=manager.allowInputs;
        matchedRailTime=ReplayManager.i.GetCurrentVirtualTime();matchedRailSpeed=0;
        manager.allowInputs=false;manager.cameraVelocity=Vector3.zero;
        Status="Rail moving "+RailDirection.ToString().ToLowerInvariant()+" at "+RailSpeed.ToString("0.0")+" m/s. "+(RailFollowsView?"Looking steers the movement.":"Mouse look aims without bending the line.");
    }
    private Vector3 GetRailDirection(CameraStateManager manager)
    {
        Vector3 direction=manager.transform.forward;
        switch(RailDirection)
        {
            case global::ReplayMod.Director.RailDirection.Backward:direction=-manager.transform.forward;break;
            case global::ReplayMod.Director.RailDirection.Left:direction=-manager.transform.right;break;
            case global::ReplayMod.Director.RailDirection.Right:direction=manager.transform.right;break;
            case global::ReplayMod.Director.RailDirection.Up:direction=manager.transform.up;break;
            case global::ReplayMod.Director.RailDirection.Down:direction=-manager.transform.up;break;
        }
        return direction;
    }
    private void UpdateRailDirection()
    {
        if(!RailActive||railCamera==null)return;
        Vector3 direction=GetRailDirection(railCamera);
        if(!rail.SetDirection(direction.x,direction.y,direction.z)){StopRail();Status="Rail stopped because the camera direction was invalid.";}
    }
    public void StopRail()
    {
        bool wasActive=rail.Active;rail.Stop();
        if(railCamera!=null)
        {railCamera.allowInputs=railOriginalInputs;railCamera.cameraVelocity=Vector3.zero;}
        railCamera=null;
        if(wasActive)Status="Rail stopped. Free camera movement restored.";
    }
    private void PrepareRailFrame()
    {
        var manager=SceneSingleton<CameraStateManager>.i;
        if(manager==null || manager!=railCamera || manager.currentState!=manager.freeState || IsBusy || Project.rig!=0 || CameraKeysEnabled || CameraManager.i?.IsFlying==true)
        {StopRail();return;}
        // Native free-camera mouse look works with translation inputs disabled.
        // Keep velocity zero so its movement/damping cannot bend or slow the rail.
        manager.allowInputs=false;manager.cameraVelocity=Vector3.zero;
    }
    private void UpdateRail()
    {
        if(!RailActive)return;
        PrepareRailFrame();if(!RailActive)return;
        var manager=railCamera;
        if(RailFollowsView){UpdateRailDirection();if(!RailActive)return;}
        var origin=Datum.originPosition;
        var previous=new Vector3(rail.LocalX(origin.x),rail.LocalY(origin.y),rail.LocalZ(origin.z));
        if(RailMatchesTargetSpeed)
        {
            var target=ResolveCameraActor(Project.target);
            if(target==null){StopRail();Status="Matched-speed rail stopped: target is absent.";return;}
            double clock=ReplayManager.i.GetCurrentVirtualTime();
            matchedRailSpeed=(float)RailSpeedMatch.EffectiveSpeed(target.speed,clock-matchedRailTime,Time.unscaledDeltaTime);matchedRailTime=clock;
            rail.AdvanceAtSpeed(Time.unscaledDeltaTime,matchedRailSpeed);
        }
        else rail.Advance(Time.unscaledDeltaTime);
        if(!RailActive){StopRail();Status="Rail stopped because the movement was invalid.";return;}
        var next=new Vector3(rail.LocalX(origin.x),rail.LocalY(origin.y),rail.LocalZ(origin.z));
        if(!ReplayMod.Patches.ReplayCameraCollision.Bypass&&(next.y<Datum.LocalSeaY+1.7f || Physics.Linecast(previous,next,(int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask)))
        {StopRail();Status="Rail stopped before an obstacle or the sea surface. Reposition the camera and start again.";return;}
        manager.transform.position=next;
        manager.cameraVelocity=Vector3.zero;manager.CheckOriginShift();
    }
    public async void Seek(double time,double? preRoll=null,bool? resumeAfter=null)
    {
        CancelEventShot();
        CancelCameraTake();
        var player=ReplayManager.i?.Playback;if(player==null)return;
        StopRail();
        try{await player.TimelineJump(time,preRoll??Project.preRoll,resumeAfter);ResetCameraSmoothing();}
        catch(Exception e){Status="Seek failed: "+e.Message;Plugin.logger.LogError(e);}
    }
    public IEnumerable<Bookmark> AllBookmarks() => (ReplayManager.i?.Playback?.AutomaticBookmarks ?? new List<Bookmark>()).Concat(Project.bookmarks).OrderBy(b=>b.time);
}

[HarmonyPatch(typeof(CursorManager),nameof(CursorManager.Refresh))]
public static class CleanCursorPatch
{
    [HarmonyPostfix]public static void Postfix() {if(ReplayDirector.i?.Clean==true)Cursor.visible=false;}
}


