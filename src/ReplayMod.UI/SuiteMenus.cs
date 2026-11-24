using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using ReplayMod.Camera;
using ReplayMod.Core;
using ReplayMod.Director;
using TMPro;
using UnityEngine;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private bool advancedCamera,advancedWeather,showPathTimes,showPathOptions;
    private bool showViewOptions,showEventSetup,showBookmarkEditor,showRecordDetails;
    private bool targetPicker;
    private CameraKey pendingPathPointDelete;
    private int pathPointsPage,manualKeysPage;
    private const int CameraPointsPerPage=25;
    private int cameraTab,helpTab,settingsTab,libraryTab,targetType,pendingCameraRig=-1;
    private string targetSearch="";
    private int eventFollowRig=1;
    private float eventLeadIn=5;
    private string MenuTaskKey=>currentMenu=="Camera"?"Camera/"+(targetPicker?"Targets/"+cameraTab:cameraTab.ToString()):
        currentMenu=="Help"?"Help/"+helpTab:currentMenu=="Settings"?"Settings/"+settingsTab:currentMenu=="Library"?"Library/"+libraryTab:currentMenu=="Lights"?"Lights/"+lightTab:currentMenu;

    private void LibraryMenu()
    {
        var page=Page("Replay library",libraryTab==1?"Recover footage from an interrupted recording. The original stays untouched.":"Choose a recording, then open it. Loading ends your current mission.");
        page.Tabs(new[]{"Recordings","Recovery"},libraryTab,index=> {libraryTab=index;if(index==1)showRecovery=true;RebuildMenu();});
        if(libraryTab==1){RecoveryMenu(page);return;}
        BulkReplayMenu(page);
        if(selectedFile!=null && File.Exists(selectedFile))
        {
            page.Note(Path.GetFileNameWithoutExtension(selectedFile),32,false);
            page.Action("Open selected replay","End your current mission and load this recording offline.",()=>Run(async()=> {
                await Manager.StartPlaying(Path.GetFileName(selectedFile));
                if(!Playback||Manager.Playback?.enabled!=true)
                    throw new InvalidOperationException(!string.IsNullOrWhiteSpace(Director?.Status)?Director.Status:"This recording could not be opened. Use Help → Support → Export logs.");
                ClosePopout();
                Notice("Replay ready. Open Camera to set up your first shot.");
            },true),true);
            ReplayRenameMenu(page);
            ReplayDeleteMenu(page);
            string integrityPath=selectedFile;
            page.Action("Check recording integrity","Check the completed file, event order and available checksums without changing the recording.",()=>Run(async()=>{
                if(!RenameStorageIdle||!ReplayRecordingRecovery.TryBeginRecording())throw new InvalidOperationException("Stop recording and wait for saves to finish.");
                try{string result=await System.Threading.Tasks.Task.Run(()=>ReplayIntegrityCheck.Check(integrityPath));Notice(result);}
                finally{ReplayRecordingRecovery.EndRecording();}
            },false,"Checking recording…"));
        }
        page.Pair("Refresh","Look for newly saved recordings in the game's Replays folder.",RebuildMenu,
            "Record gameplay","Open recording controls for a live mission.",()=>OpenMenu("Record"));
        if(Playback)page.Action("Close replay","Save the camera project automatically and return to the game menu.",()=>Run(async()=> {await Manager.SwitchState(ModStates.Idle);Notice("Replay closed.");}));
        var files=Manager.GetFiles().OrderByDescending(File.GetLastWriteTimeUtc).Take(200).ToArray();
        page.Heading("Your recordings");
        if(files.Length==0)page.Note("No recordings yet. Open Record to save your first mission.",52);
        foreach(var file in files)
        {
            var info=new FileInfo(file);
            page.Action((bulkSelecting?(bulkReplayPaths.Contains(file)?"[x] ":"[ ] "):(file==selectedFile?"> ":""))+Path.GetFileNameWithoutExtension(file),bulkSelecting?"Select or deselect this recording for removal.":"Select this recording. Saved "+info.LastWriteTime.ToString("g")+" · "+(info.Length/1048576.0).ToString("0.0")+" MiB. Opening is a separate action.",()=> {if(bulkSelecting){if(!bulkReplayPaths.Remove(file))bulkReplayPaths.Add(file);bulkConfirm=false;RebuildMenu();}else{selectedFile=file;RebuildMenuAtTop();}});
        }
        if(files.Length==200)page.Note("Showing the 200 newest recordings. Older files remain in the Replays folder.",48);
    }

    private void RecordMenu()
    {
        var page=Page("Record gameplay","Save a whole mission, or keep recent gameplay for instant clips.");
        if(Playback){page.Note("Close the replay in Library and start a live mission to record.",52);page.Action("Open library","Close this replay from the Library.",()=>OpenMenu("Library"));return;}
        var rolling=RollingRecordingController.Instance;
        page.Heading("Full mission");
        if(Manager.GetState()==ModStates.Record&&Recorder.Active?.IsRolling!=true)
            recordingControls.Add(page.Action("Stop & save mission","Finish this recording and save it to Library. Stop before leaving the mission.",()=>Run(async()=> {await Manager.SwitchState(ModStates.Idle);Notice(string.IsNullOrEmpty(Manager.LastRecordingStopStatus)?"Recording stopped. Check Library for completed recordings.":Manager.LastRecordingStopStatus);}),true));
        else recordingControls.Add(page.Action("Start mission recording","Record from now until you press Stop. Start or join a mission first.",()=>Run(async()=> {
            if(!RollingRecordingController.IsUsableGameplay()){Notice("Start or join a mission before recording.");return;}
            rolling?.SetEnabled(false);await Manager.SwitchState(ModStates.Idle);Manager.NextRecordingIsRolling=false;await Manager.SwitchState(ModStates.Record);
            Notice(Manager.GetState()==ModStates.Record&&Recorder.Active?.IsRecording==true?"Recording mission. Stop and save before leaving it.":!string.IsNullOrEmpty(Director?.Status)?Director.Status:"Recording could not start. Wait for saving to finish, then try again.");
        }),true));
        page.Heading("Instant clips");
        if(rolling==null){page.Note("Recording controller is still starting. Reopen this menu shortly.",52);return;}
        recordingControls.Add(page.Switch("Keep recent gameplay","Automatically keep a rolling buffer in live missions. Saving a clip keeps recording; unsaved older gameplay is discarded.",()=>rolling.Enabled,rolling.SetEnabled));
        var retention=page.Slide("Clip length","Keep 30 to 300 seconds (5 minutes). Complex scenes may reach the memory limit sooner.",rolling.RetentionSeconds,30,300,v=>rolling.SetRetention(Mathf.RoundToInt(v)),"0 s");retention.wholeNumbers=true;recordingControls.Add(retention);
        recordingControls.Add(page.Action("Save recent clip · "+rolling.SaveHotkeyLabel,"Save the current recent buffer as a separate replay. Recording continues.",()=>Run(async()=> {await rolling.SaveRecentClip();Notice(rolling.Status);}),true));
        saveRecentKeyButton=page.Action("Save clip key · "+rolling.SaveHotkeyLabel,"Choose a keyboard key to save recent gameplay without opening the studio. Esc cancels.",()=>BeginHotkeyCapture(HotkeyCaptureTarget.SaveRecent));
        recordStatus=page.Note(rolling.Status,64,false);
        page.Action(showRecordDetails?"Hide recording help":"Recording help", "Explain how recordings and OBS work together.",()=> {showRecordDetails=!showRecordDetails;RebuildMenu();});
        if(showRecordDetails)page.Note("Recordings save to Library. Keep recent gameplay is opt-in and pauses during replay playback. Open a saved replay to make camera shots, then capture them in OBS with game audio.",100);
    }

    private bool ReplayPage(out MenuPage page,string title,string intro)
    {
        page=Page(title,intro,true);
        if(Playback)return true;
        page.Note("Open a recording to use these tools.",38);
        page.Action("Open library","Choose a saved recording to edit.",()=>OpenMenu("Library"),true);
        return false;
    }

    private void CameraMenu()
    {
        if(!ReplayPage(out var page,"Camera",""))return;
        if(targetPicker){TargetPickerMenu(page);return;}
        if(pairSubjectPicker){PairSubjectPickerMenu(page);return;}
        page.Tabs(new[]{"View","Path","Rail","Cockpit","More"},cameraTab,index=> {cameraTab=index;RebuildMenu();});
        switch(cameraTab)
        {
            case 1:CameraPathMenu(page);break;
            case 2:QuickCameraMovesMenu(page);break;
            case 3:CockpitMenu(page);break;
            case 4:MoreCameraMenu(page);break;
            default:CameraViewMenu(page);break;
        }
    }

    private void CameraSubject(MenuPage page,bool aircraftOnly=false)
    {
        var selected=Director.Actors().FirstOrDefault(a=>a.Key==Director.Project.target).Value;
        page.Note(selected==null?"Subject: none at this moment":"Subject: "+ActorName(Director.Project.target,selected),30,false);
        page.Action("Choose subject","Pick a recorded aircraft, missile or other unit. You can also select one on the map.",()=> {
            pendingCameraRig=-1;if(aircraftOnly)targetType=1;targetPicker=true;RebuildMenu();
        });
    }

    private void CameraViewMenu(MenuPage page)
    {
        var project=Director.Project;
        CameraSubject(page);
        string[] names={"Free","Chase","Orbit","Wingtip","Missile","Cockpit","Two aircraft","Fixed angle"};
        var modes=page.Choices(names,new[]{
            "Move with the game's free-camera controls.","Follow smoothly behind the selected subject.","Circle around the selected subject.",
            "Follow the selected aircraft from beside its wing.","Follow a recorded missile that exists at this moment.","Follow the selected aircraft's native cockpit seat; enables available interior meshes.","Two-Aircraft Framing: keep two selected aircraft in frame automatically.","Fixed-Angle Follow: follow an aircraft from a steady viewing angle."},index=> {
                if(!Director.SelectCameraRig(index)){Notice(Director.Status);pendingCameraRig=index;targetType=index==4?2:index==3||index>=5?1:0;targetPicker=true;}
                RebuildMenu();
            });
        for(int index=0;index<modes.Length;index++)SelectButton(modes[index],project.rig==index);
        lensControls.AddRange(modes);
        FramingControls(page);
        lensControls.Add(page.Switch("Reduce close clipping","Let the camera approach surfaces more closely. Some one-sided surfaces can still disappear from inside.",()=>project.reduceCloseClipping,Director.SetReduceCloseClipping));
        if(project.rig==0)page.Action("Lock current angle","Select an aircraft, compose a Free view, then lock this viewing angle for Fixed-Angle Follow.",()=>{Director.CaptureFollowAngle();Notice(Director.Status);RebuildMenu();});
        if(project.rig==1)page.Note("Chase: use WASD to move, R/F to rise or descend, and hold right mouse to look around. Movement speed is below.",52);
        cameraFov=page.Slide("FOV / zoom","Smaller values zoom in; larger values widen the view. Manual changes override animated FOV.",Director.CurrentCameraFov,1,120,Director.SetCameraFov,"0.0°",text=>cameraFovLabel=text);lensControls.Add(cameraFov);
        var level=page.Pair("Level · "+LevelKeyLabel,"Remove pitch and roll while keeping heading, position and FOV. Stops camera drivers.",()=> {Director.LevelCamera();keyRoll=0;RebuildMenu();},
            "Reset FOV · 60°","Restore a sixty-degree lens without moving the camera.",()=>Director.SetCameraFov(60));cameraLevel=level[0];lensControls.AddRange(level);
        lensControls.Add(page.Slide("Movement speed","Free-camera movement multiplier. 0.05 is very slow; 1 is normal. Shift still moves faster.",Director.FreeCameraSpeed,.05f,5,Director.SetFreeCameraSpeed,"0.00×"));
        lensControls.Add(page.Switch("Camera through terrain and objects","Allow replay cameras to move through the ground and objects. Turning this off restores the game's collision behavior.",()=>project.allowCameraThroughWorld,value=> {project.allowCameraThroughWorld=value;Director.Status=value?"Camera collision disabled for this replay.":"Native camera collision restored.";}));
        var roll=page.Pair("Roll left · "+RollLeftKeyLabel,"Click for a fifteen-degree left roll, or hold the key for smooth roll.",()=>Director.RollCamera(-15),
            "Roll right · "+RollRightKeyLabel,"Click for a fifteen-degree right roll, or hold the key for smooth roll.",()=>Director.RollCamera(15));cameraRollLeft=roll[0];cameraRollRight=roll[1];lensControls.AddRange(roll);
        lensControls.Add(page.Action("Game camera · gamma / focus","Open the game's native lens, focus and image controls. Stops the current path or rail.",()=>Director.OpenNativeCameraControls()));
        page.Action(showViewOptions?"Hide view options":"View options","Adjust tracking, aircraft motion smoothing and automatic framing.",()=> {showViewOptions=!showViewOptions;RebuildMenu();});
        if(!showViewOptions)return;
        lensControls.Add(page.Switch("Show player names","Show the recorded player name and callsign for the aircraft actually followed. Hide all UI also hides this caption. Older recordings may have no names.",()=>Director.ShowPlayerNames,Director.SetShowPlayerNames));
        page.Action("Aircraft motion smoothing","Reduce recorded multiplayer shake. Opens Settings → Motion; the recording is preserved.",()=> {settingsTab=1;currentMenu="Settings";RebuildMenu();});
        autoFrameSubjectButton=page.Switch("Auto frame subject","Adjust FOV to fit the selected subject's visible bounds. Manual FOV turns this off.",()=>Director.AutoFrameSubject,Director.SetAutoFrameSubject);lensControls.Add(autoFrameSubjectButton);
        lensControls.AddRange(page.Pair("Zoom in · −5°","Narrow FOV by five degrees.",()=>Director.SetCameraFov(Director.CurrentCameraFov-5),"Zoom out · +5°","Widen FOV by five degrees.",()=>Director.SetCameraFov(Director.CurrentCameraFov+5)));
        if(project.rig==0||project.rig>=5)return;
        lensControls.Add(page.Slide("Follow distance","Distance from the subject in metres.",project.distance,1,200,v=> {project.distance=v;Director.ResetCameraSmoothing();},"0 m"));
        lensControls.Add(page.Slide("Follow height","Vertical offset from the subject. Negative values place the camera below it.",project.height,-30,100,v=> {project.height=v;Director.ResetCameraSmoothing();},"0 m"));
        lensControls.Add(page.Slide("Follow smoothing","Smaller nonzero values give a softer response; zero locks directly to the target.",project.smoothing,0,12,v=>project.smoothing=v,"0.0"));
        if(project.rig==2)lensControls.Add(page.Slide("Orbit turn rate","Degrees per second. Try 5 to 10 for a slow orbit; negative values reverse direction.",project.orbitRate,-60,60,v=> {project.orbitRate=v;Director.ResetCameraSmoothing();},"0°/s"));
    }

    private void TargetPickerMenu(MenuPage page)
    {
        page.Pair("Back to camera","Return to your previous camera tab.",()=> {targetPicker=false;pendingCameraRig=-1;RebuildMenu();},"Refresh subjects","Update subjects after seeking.",RebuildMenu);
        if(pendingCameraRig>=0)page.Note("Choose a subject for "+new[]{"Free","Chase","Orbit","Wingtip","Missile","Cockpit","Two aircraft","Fixed angle"}[pendingCameraRig]+" view.",30,false);
        page.Field(targetSearch,"Search by name, replay ID or type (aircraft, missile, unit). Press Search to apply.",value=>targetSearch=value,"Search subjects: name, ID or type");
        page.Action("Search","Apply the name, ID or type filter.",RebuildMenu);
        string[] filters={"All","Aircraft","Missiles","Other"};
        var filterButtons=page.Choices(filters,new[]{"Show every recorded unit alive at this moment.","Show aircraft only.","Show missiles only.","Show other units, such as buildings and vehicles."},index=> {targetType=index;RebuildMenu();},4);
        for(int index=0;index<filterButtons.Length;index++)SelectButton(filterButtons[index],targetType==index);
        var all=Director.Actors().OrderBy(actor=>actor.Key).ToArray();
        var matches=all.Where(actor=>targetType==0||targetType==1&&actor.Value is Aircraft||targetType==2&&actor.Value is Missile||targetType==3&&actor.Value is not Aircraft&&actor.Value is not Missile)
            .Where(actor=>string.IsNullOrWhiteSpace(targetSearch)||(ActorName(actor.Key,actor.Value)+" "+ActorType(actor.Value)).IndexOf(targetSearch.Trim(),StringComparison.OrdinalIgnoreCase)>=0).ToArray();
        page.Note(matches.Length+" subject"+(matches.Length==1?"":"s")+" at this replay moment",30);
        foreach(var actor in matches.Take(100))
        {
            uint id=actor.Key;var unit=actor.Value;
            lensControls.Add(page.Action((Director.Project.target==id?"> ":"")+ActorName(id,unit),"Select this "+ActorType(unit)+" as camera and lighting subject, then return to the camera tools.",()=> {
                if(pendingCameraRig>=0&&!CameraSubjectSupportsRig(pendingCameraRig,unit))
                {Notice(pendingCameraRig==4?"Choose a live missile for Missile view.":"Choose a live aircraft for this view.");return;}
                if(!Director.SelectCameraTarget(id)){Notice(Director.Status);return;}
                if(pendingCameraRig>=0&&!Director.SelectCameraRig(pendingCameraRig)){Notice(Director.Status);RebuildMenu();return;}
                pendingCameraRig=-1;targetPicker=false;RebuildMenu();
            },Director.Project.target==id));
        }
        if(matches.Length==0)page.Note("No matches. Clear the search, change the type filter, or seek to when the subject exists.",62);
        if(matches.Length>100)page.Note("Showing the first 100 matches. Narrow the search to find your subject.",48);
    }

    private void CameraPathMenu(MenuPage page)
    {
        page.Note("Choose the replay time, position the camera, then add a point. Each point keeps that replay time.",46);
        pathEditControls.AddRange(page.Pair("Position camera","Use the game's free-camera controls to compose the next point.",()=> {Director.EnterFreeCamera();RebuildMenu();},
            "Add point here","Save position, aim and FOV at the current replay time. Capturing at the same timestamp updates that point. Seek to another time to add a new point.",()=> {Director.CapturePathPoint();Notice(Director.Status);RebuildMenu();}));
        pathAttachMode=page.Switch("Follow selected subject","ON makes the path move with the selected unit; OFF creates a world path. Choose before adding points; clear points before changing mode.",()=>Director.PathAttached,value=> {Director.SetPathAttached(value);Notice(Director.Status);RebuildMenu();});pathEditControls.Add(pathAttachMode);
        if(Director.PathAttached)
        {
            uint target=Director.Project.cameras.Count>0?Director.Project.cameras[0].target:Director.Project.target;
            var selected=Director.Actors().FirstOrDefault(actor=>actor.Key==target).Value;
            page.Note(selected!=null?"Attached to: "+ActorName(target,selected):"Select a live subject before adding attached points.",34,false);
            pathEditControls.Add(page.Action("Choose subject","Choose the aircraft for new attached points. Existing points keep their original subject.",()=> {pendingCameraRig=-1;targetType=1;targetPicker=true;RebuildMenu();}));
        }
        pathSmoothMode=page.Switch("Smooth curves","Round movement, aim and zoom through the points. Use Path smoothness to soften changes; OFF uses straight segments.",()=>Director.PathSmooth,Director.SetPathSmooth);pathEditControls.Add(pathSmoothMode);
        pathSmoothnessSlider=page.Slide("Path smoothness","0% keeps the classic curve; 100% softens motion, aim and zoom changes most. Saved point times stay exact. Curves may bow around points: preview near obstacles. Requires Smooth curves ON.",Director.PathSmoothness*100,0,100,value=>Director.SetPathSmoothness(value/100),"0'%'",label=>pathSmoothnessLabel=label);
        pathEditControls.Add(pathSmoothnessSlider);
        cameraFov=page.Slide("FOV / zoom","Set the lens before Add point here. Each point remembers its zoom. While previewing a path, manual zoom keeps the path moving and switches off saved FOV animation.",Director.CurrentCameraFov,1,120,Director.SetCameraFov,"0.0°",text=>cameraFovLabel=text);pathEditControls.Add(cameraFov);
        pathFovMode=page.Switch("Animate FOV / zoom","ON blends the zoom saved at each point; OFF lets you zoom manually while the path moves. Set the FOV slider, then capture each point.",()=>Director.PathAnimatesFov,Director.SetPathAnimatesFov);pathEditControls.Add(pathFovMode);
        pathStatus=page.Note("",42,false);
        pathStartTake=page.Action("Start take · 5s countdown","Jump to the first point, hide UI, wait five seconds, then play. Start OBS during the delay. Reopen the studio to cancel.",()=>Director.StartCameraTake(),true);
        var playback=page.Pair("Preview path","Jump to the first point and play the path with the studio visible.",()=>Director.PreviewCameraPath(),
            "Cancel take","Stop the countdown or running take, pause and restore the tools.",()=>Director.CancelCameraTake());pathPlay=playback[0];pathCancelTake=playback[1];
        pathEditControls.Add(page.Action("Stop preview / move camera","Release the path camera so you can move and look around again.",()=>Director.StopCameraPathPreview()));
        page.Action(showPathOptions?"Hide path options":"Path options & point times","Edit points, path visibility, aim, FOV and how the take ends.",()=> {showPathOptions=!showPathOptions;RebuildMenu();});
        if(!showPathOptions)return;
        pathEditControls.Add(page.Switch("Show path guides","Show point numbers, the curve and look directions. Hide All UI always hides guides.",()=>Director.ShowCameraPath,Director.SetShowCameraPath));
        pathEditControls.Add(page.Switch("Animate look direction","Animate the aim stored in camera points. OFF skips recorded aim animation.",()=>Director.PathAnimatesRotation,Director.SetPathAnimatesRotation));
        pathEditControls.AddRange(page.Pair("Remove last point","Delete the final point only.",()=> {Director.RemoveLastPathPoint();RebuildMenu();},"Clear all points","Delete every camera point. Required before changing attachment mode.",()=> {Director.ClearCameraPath();RebuildMenu();}));
        pathStopMode=page.Switch("Stop at last point","Pause at the last point and hold it for the chosen time. OFF lets gameplay continue.",()=>Director.StopAtPathEnd,Director.SetStopAtPathEnd);pathEditControls.Add(pathStopMode);
        pathEditControls.Add(page.Slide("Final-frame hold","Real seconds to hold the paused last frame before finishing the take.",Director.PathEndHoldSeconds,0,10,Director.SetPathEndHoldSeconds,"0.0 s"));
        page.Action(showPathTimes?"Hide points":"Edit / delete points","Restore a point's camera, change its time, or delete it.",()=> {showPathTimes=!showPathTimes;pendingPathPointDelete=null;RebuildMenu();});
        if(!showPathTimes)return;
        page.Note("Select a point to restore its time, position, aim, roll and FOV. Times must stay in order.",52);
        int pointOffset=CameraPointPager(page,false);
        var points=Director.Project.cameras.Skip(pointOffset).Take(CameraPointsPerPage).ToArray();
        for(int index=0;index<points.Length;index++)
        {
            int pointIndex=pointOffset+index;var point=points[index];
            pathEditControls.AddRange(page.PointTimeRow("Point "+(pointIndex+1)+" · "+Clock(point.time),point.time,()=>Director.GoToPathPoint(point),time=> {
                if(Director.SetPathPointTime(pointIndex,time))Notice("Point time saved.");else Notice("Use a replay time strictly between the adjacent points.");RebuildMenu();
            }));
            pathEditControls.Add(page.Action(ReferenceEquals(pendingPathPointDelete,point)?"Confirm delete point "+(pointIndex+1):"Delete point "+(pointIndex+1),"Press twice to delete this point. Other points are kept.",()=> {
                if(ReferenceEquals(pendingPathPointDelete,point)){Director.RemovePathPoint(point);pendingPathPointDelete=null;Notice(Director.Status);}
                else pendingPathPointDelete=point;
                RebuildMenu();
            }));
            if(pointIndex+1<Director.PathPointCount)
            {
                page.Note("Movement to next point · "+new[]{"Path default","Constant speed","Smooth curve","Instant jump"}[point.segmentMode],30,false);
                pathEditControls.AddRange(page.Choices(new[]{"Default","Constant","Smooth","Jump"},new[]{"Use the path's Smooth curves setting.","Travel in a straight line at constant speed between points.","Use the smooth curve and Path smoothness setting.","Hold this point until the next point, then jump."},mode=> {Director.SetPathSegmentMode(point,mode);RebuildMenu();},4));
            }
        }
        if(points.Length==0)page.Note("No points yet. Position the camera and choose Add point here.",48);
    }

    private int CameraPointPager(MenuPage page,bool manual)
    {
        int count=Director.PathPointCount,last=Math.Max(0,(count-1)/CameraPointsPerPage);
        int current=Math.Max(0,Math.Min(last,manual?manualKeysPage:pathPointsPage));
        if(manual)manualKeysPage=current;else pathPointsPage=current;
        if(count>CameraPointsPerPage)
        {
            page.Note("Points "+(current*CameraPointsPerPage+1)+"–"+Math.Min(count,(current+1)*CameraPointsPerPage)+" of "+count+" · page "+(current+1)+" / "+(last+1),34,false);
            pathEditControls.AddRange(page.Pair("Previous points","Show the previous page of camera points.",()=>ChangeCameraPointPage(manual,current-1),
                "Next points","Show the next page of camera points.",()=>ChangeCameraPointPage(manual,current+1)));
        }
        return current*CameraPointsPerPage;
    }
    private void ChangeCameraPointPage(bool manual,int page)
    {
        int last=Math.Max(0,(Director.PathPointCount-1)/CameraPointsPerPage);
        if(manual)manualKeysPage=Math.Max(0,Math.Min(last,page));else pathPointsPage=Math.Max(0,Math.Min(last,page));
        pendingPathPointDelete=null;RebuildMenuAtTop();
    }

    private void QuickCameraMovesMenu(MenuPage page)
    {
        page.Note("A steady move in one direction. Choose Follow view to steer while looking around.",46);
        page.Action("Position camera","Switch to Free camera to compose the shot before starting.",()=> {Director.EnterFreeCamera();RebuildMenu();});
        railModes=page.Pair("Straight line","Lock travel direction; looking around changes only the aim.",()=>Director.SetRailFollowsView(false),"Follow view","Travel direction follows the camera as you look around.",()=>Director.SetRailFollowsView(true));
        railDirections=page.Choices(RailNames,new[]{"Travel forward from the camera.","Travel backward from the camera.","Slide left.","Slide right.","Move upward.","Move downward."},index=>Director.SetRailDirection((RailDirection)index));
        page.Slide("Rail speed","Metres per real second. Try 1 to 5 for a gentle move. Changing this disables target-speed matching.",Director.RailSpeed,.1f,100,Director.SetRailSpeed,"0.0 m/s");
        railMatchSpeed=page.Switch("Match subject speed","Match the selected subject's displayed speed, including slow motion and pause. Direction remains manual.",()=>Director.RailMatchesTargetSpeed,Director.SetRailMatchesTargetSpeed);
        var buttons=page.Pair("Start rail","Begin travel in the chosen direction. Hide All UI keeps the rail moving.",()=>Director.StartRail(),"Stop rail","Stop travel at the current camera position.",()=>Director.StopRail());railStart=buttons[0];railStop=buttons[1];
        railStatus=page.Note("Rail stopped",42,false);
        CameraSubject(page);
    }

    private void CockpitMenu(MenuPage page)
    {
        var project=Director.Project;
        CameraSubject(page,true);
        CockpitSeatMenu(page);
        lensControls.AddRange(page.Pair("Follow cockpit seat","Follow the selected aircraft's native eye position. Enables available interior meshes and reduced clipping; hides seated heads.",()=> {
            if(!Director.SelectCameraRig(5)){Notice(Director.Status);pendingCameraRig=5;targetType=1;targetPicker=true;}else Notice("Following the cockpit seat.");RebuildMenu();
        },"Free at cockpit seat","Pause and place Free camera at the native cockpit viewpoint. Look around or add attached path points.",()=> {Director.PlaceCameraInCockpit();Notice(Director.Status);RebuildMenu();}));
        lensControls.Add(page.Switch("Detailed cockpit","Show the selected aircraft's available native cockpit meshes, including in Free camera. Missing models or unrecorded instruments cannot be recreated.",()=>project.detailedCockpit,Director.SetDetailedCockpit));
        lensControls.Add(page.Switch("Flight instruments","Show ground speed, MSL altitude, heading, climb, throttle and gear using recorded flight data on supported cockpit screens. Radar contacts and target-video history were not recorded.",()=>project.flightInstruments,Director.SetFlightInstruments));
        lensControls.Add(page.Switch("Show seated pilots","Show existing seated pilot models for exterior shots. Very close seat views hide heads; missing or ejected crew are never recreated.",()=>project.seatedPilots,Director.SetSeatedPilots));
        lensControls.Add(page.Switch("First-person ejection","Keep Cockpit view in the pilot's eyes through ejection, or in the escape capsule. Uses newly recorded eye data; hides only the followed pilot's body and restores it on exit. Save project to keep this option.",()=>project.firstPersonEjection,Director.SetFirstPersonEjection));
        page.Note("These switches save with the camera project. Available interiors and pilots depend on the aircraft.",52);
    }

    private void MoreCameraMenu(MenuPage page)
    {
        page.Action("Load saved project","Restore the last saved camera project, replacing your current edits.",()=> {Director.Load();Notice(Director.Status);RebuildMenu();});
        page.Action(advancedCamera?"Hide manual & legacy tools":"Manual keys & legacy tools","Optional manual keyframes and old F6/F7 splines. The Path tab is the simpler workflow.",()=> {advancedCamera=!advancedCamera;RebuildMenu();});
        if(!advancedCamera)return;
        var project=Director.Project;
        page.Heading("Manual camera keys");
        page.Note("Pause, position Free camera, capture a key. Seek ahead and repeat; then enable keys and play from the first point.",68);
        pathEditControls.Add(page.Switch("Play camera keys","Animate saved positions and lenses. Overrides view presets.",()=>Director.CameraKeysEnabled,value=> {if(value)Director.StopRail();Director.CameraKeysEnabled=value;}));
        pathEditControls.Add(page.Switch("Keys follow subject","Capture relative to the selected aircraft. Use the same subject and setting throughout a shot.",()=>relativeKey,value=>relativeKey=value));
        pathEditControls.Add(page.Switch("Ease at keys","Gently speed up and slow down between keys. Two keys avoid repeated stops in a long move.",()=>keyEase,value=>keyEase=value));
        pathEditControls.Add(page.Slide("Added roll","Extra roll in degrees for the next captured key.",keyRoll,-180,180,value=>keyRoll=value,"0°"));
        pathEditControls.Add(page.Action("Capture camera key","Save position, aim, FOV and roll here. Replaces a key at the same replay time.",()=> {Director.CaptureCameraKey(relativeKey,keyEase,keyRoll);Notice(Director.Status);RebuildMenu();},true));
        int keyOffset=CameraPointPager(page,true);
        foreach(var key in project.cameras.Skip(keyOffset).Take(CameraPointsPerPage).ToArray())pathEditControls.AddRange(page.Pair(Clock(key.time)+" · "+key.fov.ToString("0")+"°"+(key.relative?" · attached":""),"Restore this key's time, position, orientation, roll and FOV, then edit in Free camera.",()=>Director.GoToPathPoint(key),ReferenceEquals(pendingPathPointDelete,key)?"Confirm remove key":"Remove key","Press twice to delete this camera key.",()=> {
            if(ReferenceEquals(pendingPathPointDelete,key)){Director.RemovePathPoint(key);pendingPathPointDelete=null;Notice(Director.Status);}else pendingPathPointDelete=key;
            RebuildMenu();
        }));
        page.Heading("Legacy curve path");
        page.Note("F6 adds a legacy spline point; F7 removes the last. These are separate from the Path tab.",52);
        pathEditControls.AddRange(page.Pair("Add point · F6","Capture a legacy spline point here.",()=> {CameraManager.i.AddPoint();Notice("Path point added.");},"Fly saved spline","Switch between the legacy spline and manual Free camera. Requires two points.",()=> {Director.StopRail();CameraManager.i.ToggleFlight();}));
        if(CameraManager.i.IsControlPointSelected()||CameraManager.i.IsJointPointSelected())
        {
            string value=CameraManager.i.GetWaypointTime().ToString("0.000");page.Note("Selected point time (seconds)",24,false);page.Field(value,"Replay seconds for the selected legacy point. Apply below.",text=>value=text,"Replay seconds");
            pathEditControls.Add(page.Action("Set selected point time","Apply the entered replay time within neighbouring points.",()=> {if(double.TryParse(value,out var time)&&DirectorProject.Finite(time)){CameraManager.i.SetNewTime(time);Notice("Path point time updated.");}else Notice("Enter a valid time in seconds.");}));
        }
    }

    private static string ActorName(uint id,Unit unit)
    {
        var identity=ReplayManager.i?.Playback?.GetActorIdentity(id);
        string name=identity?.Callsign;
        if(string.IsNullOrWhiteSpace(name))name=unit?.UniqueName;
        if(string.IsNullOrWhiteSpace(name))name=unit?.definition?.unitName;
        if(string.IsNullOrWhiteSpace(name))name=unit?.definition?.jsonKey??"Unit";
        name=ReplayMod.Data.ActorIdentityText.Normalize(name);
        if(identity?.HumanControlled==true&&!string.IsNullOrEmpty(identity.PlayerName))name=identity.PlayerName+" · "+name;
        return name+" · #"+id+(unit is Missile?" (missile)":"");
    }
    private static string ActorType(Unit unit)=>unit is Aircraft?"aircraft":unit is Missile?"missile":"unit";
    private static bool CameraSubjectSupportsRig(int rig,Unit unit)=>(rig==3||rig>=5)?unit is Aircraft:rig!=4||unit is Missile;

    private void EventsMenu()
    {
        if(!ReplayPage(out var page,"Events & bookmarks","Find a moment. Jump pauses; Follow plays with its recorded subject."))return;
        page.Field(markerSearch,"Search event and bookmark names. Press Search to apply.",value=>markerSearch=value,"Search events and bookmarks");
        page.Pair("Search","Apply the event-name filter.",RebuildMenu,"Add bookmark","Name the current replay moment.",()=> {showBookmarkEditor=!showBookmarkEditor;RebuildMenu();});
        if(showBookmarkEditor)
        {
            page.Field(markerName,"Name this bookmark, for example bridge fly-by.",value=>markerName=value,"Bookmark name, e.g. bridge fly-by");
            page.Action("Bookmark this moment","Save this time and selected subject. Save the project to keep the marker.",()=> {
                Director.Project.bookmarks.Add(new Bookmark {time=Math.Max(0,Manager.GetCurrentVirtualTime()),name=markerName,actor=Director.Project.target,actorLifetime=Manager.Playback.CurrentActorGeneration(Director.Project.target)});showBookmarkEditor=false;RebuildMenu();
            },true);
        }
        page.Action(showEventSetup?"Hide Follow options":"Follow options · "+eventLeadIn.ToString("0")+"s lead-in","Choose the camera preset and lead-in used by Follow. Jump always pauses at the exact event time.",()=> {showEventSetup=!showEventSetup;RebuildMenu();});
        if(showEventSetup)
        {
            var lead=page.Slide("Follow lead-in","Start this many replay seconds before the event. Jump & pause stays exact.",eventLeadIn,0,10,value=>eventLeadIn=value,"0 s");lead.wholeNumbers=true;eventControls.Add(lead);
            string[] names={"Chase","Orbit","Wingtip","Missile"};
            var presets=page.Choices(names,new[]{"Follow behind the event's subject.","Orbit the event's subject.","Follow the wing of an aircraft subject.","Follow only a recorded missile subject."},index=> {eventFollowRig=index+1;RebuildMenu();},4);eventControls.AddRange(presets);
            for(int index=0;index<presets.Length;index++)SelectButton(presets[index],eventFollowRig==index+1);
        }
        var matches=Director.AllBookmarks().Where(marker=>string.IsNullOrEmpty(markerSearch)||marker.name.IndexOf(markerSearch,StringComparison.OrdinalIgnoreCase)>=0).Take(201).ToArray();
        foreach(var marker in matches.Take(200))
        {
            var buttons=page.EventRow(Clock(marker.time)+" · "+marker.name,()=>Director.Seek(marker.time,null,false),()=>Director.JumpAndFollow(marker,eventFollowRig,eventLeadIn),marker.automatic?(Action)null:()=> {Director.Project.bookmarks.Remove(marker);RebuildMenu();});
            eventControls.AddRange(buttons.Where((_,index)=>index!=1||marker.actor!=0));
            if(marker.actor==0){buttons[1].interactable=false;buttons[1].GetComponent<SuiteTooltip>().Description="No recorded subject for this bookmark. Jump still goes to its saved time.";}
        }
        if(matches.Length==0)page.Note("No matching events. Clear the search or add your own bookmark.",52);
        if(matches.Length>200)page.Note("Showing the first 200 matches. Narrow the search for more specific results.",48);
    }

    private void SettingsMenu()
    {
        var page=Page("Studio settings","Playback adjustments and shortcuts.",Playback);
        page.Tabs(new[]{"Playback","Motion","Speed ramps","Appearance"},settingsTab,index=> {settingsTab=index;RebuildMenu();});
        if(settingsTab==1){MotionMenu(page);return;}
        if(settingsTab==3){AppearanceMenu(page);return;}
        if(settingsTab==0)
        {
            page.Action("Keyboard shortcuts","Change Studio, Hide All UI and camera keys.",()=> {helpTab=1;currentMenu="Help";RebuildMenu();});
            snapEventsButton=page.Switch("Snap timeline to events","Snap within six screen pixels of shown ticks. OFF seeks exactly where you choose. Hold Shift to bypass snapping.",()=>snapToEvents,value=>snapToEvents=value);
            if(!Playback){page.Note("Open a replay for frame stepping and smoke lead-in settings.",52);return;}
            page.Heading("Time-lapse playback");
            page.Note("Experimental: the requested speed may slow down under load. Recorded weather and time follow the replay when available; custom Scene settings override them. Effects and audio may differ at high speeds.",80);
            float[] fastRates={1,2,5,10,30,60,120,240,480};
            var fastButtons=page.Choices(fastRates.Select(rate=>rate+"×").ToArray(),fastRates.Select(_=>"Set the maximum requested replay speed. The bottom speed slider returns to normal or slow motion.").ToArray(),index=> {Director.SetPlaybackSpeed(fastRates[index]);RebuildMenu();},3);
            eventControls.AddRange(fastButtons);
            for(int index=0;index<fastButtons.Length;index++)SelectButton(fastButtons[index],Math.Abs(Director.ManualSpeed-fastRates[index])<.001f&&!Director.SpeedRampEnabled);
            page.Heading("Frame step interval");
            int[] rates={24,30,60,120};
            var frames=page.Choices(rates.Select(rate=>rate+" fps").ToArray(),rates.Select(_=>"Set how far frame-step buttons move. This does not change OBS recording FPS.").ToArray(),index=> {Director.FrameRate=rates[index];RebuildMenu();},4);eventControls.AddRange(frames);
            for(int index=0;index<frames.Length;index++)SelectButton(frames[index],Director.FrameRate==rates[index]);
            eventControls.Add(page.Slide("Smoke lead-in","Replay this hidden lead-in after seeking so trails can form. Longer values may increase seek time.",(float)Director.Project.preRoll,0,5,value=>Director.Project.preRoll=value,"0.0 s"));
            return;
        }
        if(!Playback){page.Note("Open a recording to animate playback speed.",42);page.Action("Open library","Choose a recording to edit.",()=>OpenMenu("Library"));return;}
        eventControls.Add(page.Switch("Play saved speed ramps","Animate speed between saved keys. The bottom speed slider returns to manual control.",()=>Director.SpeedRampEnabled,value=>Director.SpeedRampEnabled=value));
        eventControls.Add(page.Slide("Speed for new key","Choose 0.1× to 1.0× for the next speed key.",speedKey,.1f,1,value=>speedKey=Mathf.Round(value*10)/10,"0.0×"));
        eventControls.Add(page.Action("Capture speed key","Save this speed at the current replay time. Add another key later for a smooth transition.",()=> {
            double time=Math.Max(0,Manager.GetCurrentVirtualTime());var keys=Director.Project.speeds;keys.RemoveAll(key=>Math.Abs(key.time-time)<.00001);keys.Add(new SpeedKey {time=time,scale=speedKey});keys.Sort((a,b)=>a.time.CompareTo(b.time));RebuildMenu();
        },true));
        foreach(var key in Director.Project.speeds.Take(100).ToArray())eventControls.AddRange(page.Pair(Clock(key.time)+" · "+key.scale.ToString("0.0")+"×","Jump to this speed key and pause.",()=>Director.Seek(key.time,null,false),"Remove key","Delete this saved speed key.",()=> {Director.Project.speeds.Remove(key);RebuildMenu();}));
    }

    private void SceneMenu()
    {
        if(!ReplayPage(out var page,"Scene","Set the replay's weather, time of day and aircraft lights."))return;
        var project=Director.Project;var environment=project.environment;
        page.Switch("Custom weather & time","Enable custom weather and frozen time. OFF restores the recorded environment.",()=>environment.enabled,value=> {environment.enabled=value;Director.QueueEnvironmentChange();RebuildMenu();});
        if(environment.enabled)
        {
            page.Slide("Time of day","Time on a 24-hour clock.",environment.hour,0,24,value=> {environment.hour=value;Director.QueueEnvironmentChange();},"0.0 h");
            page.Slide("Weather intensity","Clear conditions through overcast and storm effects.",environment.conditions,0,1,value=> {environment.conditions=value;Director.QueueEnvironmentChange();},"0.00");
            page.Slide("Cloud altitude","Cloud height in metres.",environment.cloudHeight,500,10000,value=> {environment.cloudHeight=value;Director.QueueEnvironmentChange();},"0 m");
        }
        AircraftLightingControls(page);
        if(!environment.enabled)return;
        page.Action("Restore recorded weather","Disable custom weather and time.",()=> {environment.enabled=false;Director.QueueEnvironmentChange();RebuildMenu();});
        page.Action(advancedWeather?"Hide wind & moon":"Wind & moon","Open wind, turbulence and moon controls.",()=> {advancedWeather=!advancedWeather;RebuildMenu();});
        if(!advancedWeather)return;
        page.Slide("Wind speed","Wind speed in metres per second.",environment.windSpeed,0,100,value=> {environment.windSpeed=value;Director.QueueEnvironmentChange();},"0 m/s");
        page.Slide("Wind heading","Wind direction in degrees.",environment.windHeading,0,360,value=> {environment.windHeading=value;Director.QueueEnvironmentChange();},"0°");
        page.Slide("Turbulence","Native replay turbulence factor.",environment.turbulence,0,1,value=> {environment.turbulence=value;Director.QueueEnvironmentChange();},"0.00");
        page.Slide("Moon phase","Native moon cycle from 0 to 28; changes night illumination.",environment.moonPhase,0,28,value=> {environment.moonPhase=value;Director.QueueEnvironmentChange();},"0");
    }

    private void HelpMenu()
    {
        var page=Page("Help","A quick guide, keyboard shortcuts and support logs.");
        page.Tabs(new[]{"Guide","Shortcuts","Support"},helpTab,index=> {helpTab=index;RebuildMenu();});
        if(helpTab==1){ShortcutMenu(page);return;}
        if(helpTab==2){SupportMenu(page);return;}
        page.Heading("Your first trailer shot");
        page.Note("1. Record a mission, stop and save, then open it in Library.\n2. Drag the timeline; choose playback speed.\n3. Camera → View: choose your subject and framing.\n4. Hide all UI, then record the shot in OBS.",118,false);
        page.Pair("Open Camera","Choose a camera view or build a smooth path.",()=>OpenCameraTools(),"Open Record","Set up full-mission recording or instant clips.",()=>OpenMenu("Record"));
        page.Action(showTutorial?"Hide detailed guide":"Camera paths & capture guide","Read the longer guide without leaving the game.",()=> {showTutorial=!showTutorial;RebuildMenu();});
        if(!showTutorial)return;
        page.Heading("Find the moment");
        page.Note("Release the timeline to jump and pause. Frame buttons make fine adjustments. Amber ticks are events, green ticks are bookmarks and blue diamonds are camera points. Event snapping is optional in Settings; hold Shift to bypass it.",100);
        page.Note("Events → Jump pauses at an exact moment. Follow starts before the event and follows its recorded subject. Follow options choose the lead-in and camera preset.",70);
        page.Heading("Build a camera path");
        page.Note("Camera → Path: choose attachment mode, seek to the time you want, position Free camera and Add point here. Repeat at each desired replay time. Point options let you choose constant, smooth or instant movement. Preview starts at the first point; Stop preview releases the camera.",112);
        page.Note("Smooth curves rounds the movement through points. Path smoothness softens motion, aim and zoom changes; try 65% and preview. Point times stay exact. Rail provides a constant-direction move; Follow view steers as you look. Match subject speed follows the subject's displayed speed.",112);
        page.Heading("Capture the shot");
        helpCaptureInstructions=page.Note("",116,false);
        page.Note("Hide All UI removes the HUD, menus, editing guides and cursor. The same key restores them. Save project keeps paths, bookmarks, weather and lights beside the recording. OBS records the finished picture and game audio.",96);
        page.Note("Camera → View → Game camera opens native gamma, focus and image controls. Camera → Cockpit enables available interior meshes and pilot visibility.",70);
        page.Note("Library → Recover interrupted recording makes a separate playable copy when a recording was interrupted. The original is kept.",64);
        RefreshBindingLabels();
    }

    private void ShortcutMenu(MenuPage page)
    {
        helpHideUiButton=page.Action("Hide all UI · "+CleanKeyLabel,HideUiDescription,HideUi,true);
        helpHideUiKeyButton=page.Action("Hide UI key · "+CleanKeyLabel,"Assign the single key that hides or restores every UI element. Esc cancels.",()=>BeginHotkeyCapture(HotkeyCaptureTarget.HideAllUi));
        helpStudioKeyButton=page.Action("Studio key · "+StudioKeyLabel,"Assign the key that opens and closes the studio. Hide All UI stays separate.",()=>BeginHotkeyCapture(HotkeyCaptureTarget.Studio));
        var roll=page.Pair("Roll left key · "+RollLeftKeyLabel,"Assign the key held to roll left.",()=>BeginHotkeyCapture(HotkeyCaptureTarget.RollLeft),"Roll right key · "+RollRightKeyLabel,"Assign the key held to roll right.",()=>BeginHotkeyCapture(HotkeyCaptureTarget.RollRight));helpRollLeftKey=roll[0];helpRollRightKey=roll[1];
        helpLevelKey=page.Action("Level key · "+LevelKeyLabel,"Assign the key that removes pitch and roll while keeping position, heading and lens.",()=>BeginHotkeyCapture(HotkeyCaptureTarget.Level));
        page.Action("Save recent clip key","Set the instant-clip key in Record.",()=>OpenMenu("Record"));
        helpShortcutInstructions=page.Note("",168,false);RefreshBindingLabels();
    }

    private void SupportMenu(MenuPage page)
    {
        page.Note("Export a support ZIP and attach it to your bug report. Include what happened and how to reproduce it.",62);
        helpExportLogsButton=page.Action("Export logs","Collect recent logs, loaded mod versions, replay/camera state and recent frame timing into one ZIP.",ExportLogs,true);
        var actions=page.Pair("Open logs folder","Show the support ZIP to attach to your report.",OpenLogsFolder,"Copy ZIP path","Copy the exported ZIP's path.",CopyLogsPath);openLogsFolderButton=actions[0];copyLogsPathButton=actions[1];
        logExportStatus=page.Note("",116,false);UpdateLogExportControls();
        page.Note("Recordings are in the game's Replays folder. Camera projects save beside them. Older recordings may contain fewer effects or events.",76);
    }
}



