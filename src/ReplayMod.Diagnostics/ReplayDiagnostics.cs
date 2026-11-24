using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;

namespace ReplayMod.Diagnostics;

public static class ReplayDiagnostics
{
    private static int exporting;
    private static bool initialized;
    private static readonly object warningGate = new object();
    private static readonly Queue<string> warnings = new Queue<string>();
    private static int warningCharacters;
    private static readonly Queue<FrameSample> frames = new Queue<FrameSample>();
    private static double frameSeconds;
    public static bool IsExporting => Volatile.Read(ref exporting) != 0;
    public static string LastExportPath { get; private set; }
    public static string LastExportNotice { get; private set; } = "";
    private struct FrameSample { public float Time, Delta; }

    // Call on the Unity thread during plugin startup. The event may subsequently
    // invoke ReceiveLog on a worker thread; it never reads Unity state.
    public static void Initialize()
    {
        if(initialized)return;
        initialized=true;Application.logMessageReceivedThreaded+=ReceiveLog;
    }
    public static void Shutdown()
    {
        if(!initialized)return;
        Application.logMessageReceivedThreaded-=ReceiveLog;initialized=false;
    }
    private static void ReceiveLog(string message,string stack,LogType type)
    {
        if(type!=LogType.Warning&&type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert)return;
        string line=DateTime.UtcNow.ToString("O")+" ["+type+"] "+Limit(message,2048)+"\n"+Limit(stack,4096)+"\n";
        lock(warningGate)
        {
            warnings.Enqueue(line);warningCharacters+=line.Length;
            while(warnings.Count>256||warningCharacters>256*1024)warningCharacters-=warnings.Dequeue().Length;
        }
    }
    private static string Limit(string text,int length) => text==null?"":text.Length<=length?text:text.Substring(0,length)+" [truncated]";

    internal static void SampleFrame(float time,float delta)
    {
        if(delta<=0||float.IsNaN(delta)||float.IsInfinity(delta))return;
        frames.Enqueue(new FrameSample {Time=time,Delta=delta});frameSeconds+=delta;
        while(frames.Count>4096||(frames.Count>0&&frames.Peek().Time<time-10))frameSeconds-=frames.Dequeue().Delta;
    }

    public static async Task<string> ExportAsync()
    {
        if(Interlocked.CompareExchange(ref exporting,1,0)!=0)throw new InvalidOperationException("A log export is already in progress.");
        try
        {
            // A caller does not need to know Unity's threading rules. Every
            // runtime getter is evaluated here, before the plain IO worker.
            await UniTask.SwitchToMainThread();
            var input=Capture();
            var result=await Task.Run(()=>DiagnosticsBundleWriter.Write(input));
            LastExportPath=result.Path;LastExportNotice=result.Notice;
            return result.Path;
        }
        catch(Exception ex)
        {
            LastExportNotice="Log export failed: "+ex.Message;
            throw;
        }
        finally {Interlocked.Exchange(ref exporting,0);}
    }

    private static DiagnosticsBundleInput Capture()
    {
        string game=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        string unityLog=Application.consoleLogPath;
        string bep=Paths.BepInExRootPath;
        var roots=new List<KeyValuePair<string,string>> {
            new KeyValuePair<string,string>(game,"<GAME>"),
            new KeyValuePair<string,string>(bep,"<BEPINEX>"),
            new KeyValuePair<string,string>(Application.persistentDataPath,"<GAME-DATA>"),
            new KeyValuePair<string,string>(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<USER>")
        };
        var input=new DiagnosticsBundleInput {OutputDirectory=Path.Combine(game,"Replays","Diagnostics"),Redactor=new DiagnosticsRedactor(roots,Environment.UserName,Environment.MachineName)};
        var text=new StringBuilder();
        text.AppendLine("Snapshot captured on Unity main thread, UTC="+DateTime.UtcNow.ToString("O"));
        text.AppendLine("This is an observed diagnostic snapshot, not a gameplay correctness test.");
        Section(text,"Runtime",()=> {
            Value(text,"ModVersion","v"+global::ReplayMod.PluginInfo.Version);Value(text,"BuildChannel","Local testing copy");
            Value(text,"StartupPhase",ReplayStartup.Phase);Value(text,"RuntimeHeartbeatFrames",ReplayStartup.Heartbeats);Value(text,"StartupTrace",ReplayStartup.Trace);
            if(Chainloader.PluginInfos.TryGetValue(global::ReplayMod.PluginInfo.GUID,out var loadedPlugin))Value(text,"BepInExModVersion","v"+loadedPlugin.Metadata.Version);
            Value(text,"AssemblyVersion","v"+typeof(Plugin).Assembly.GetName().Version);
            Value(text,"ModFile",typeof(Plugin).Assembly.Location);
            Value(text,"GameProduct",Application.productName);Value(text,"GameVersion",Application.version);Value(text,"UnityVersion",Application.unityVersion);
            Value(text,"OperatingSystem",SystemInfo.operatingSystem);Value(text,"CPU",SystemInfo.processorType);Value(text,"LogicalProcessors",SystemInfo.processorCount);
            Value(text,"SystemMemoryMiB",SystemInfo.systemMemorySize);Value(text,"GPU",SystemInfo.graphicsDeviceName);Value(text,"GraphicsAPI",SystemInfo.graphicsDeviceType);
            Value(text,"GraphicsMemoryMiB",SystemInfo.graphicsMemorySize);Value(text,"GraphicsDriver",SystemInfo.graphicsDeviceVersion);
            Value(text,"ScreenPixels",Screen.width+"x"+Screen.height);Value(text,"Fullscreen",Screen.fullScreen);
            Value(text,"DisplayResolution",Screen.currentResolution.ToString());Value(text,"VSyncCount",QualitySettings.vSyncCount);Value(text,"TargetFrameRate",Application.targetFrameRate);
            Value(text,"UnityTimeScale",Time.timeScale);Value(text,"GameTimeScale",TimeScaleManager.Scale);Value(text,"GameSlowMotion",GameplayUI.GameSlowMotion);Value(text,"UnscaledFrameMilliseconds",Time.unscaledDeltaTime*1000);
            Value(text,"ManagedMemoryBytes",GC.GetTotalMemory(false));Value(text,"MissionRunning",MissionManager.IsRunning);
            Value(text,"NativeSlowMotionBinding","Not captured; use the game's Controls menu to identify its binding.");
            if(frames.Count>0&&frameSeconds>0)
            {
                var sample=frames.ToArray();var milliseconds=sample.Select(f=>(double)f.Delta*1000).OrderBy(v=>v).ToArray();
                Value(text,"FrameSampleCount",sample.Length);Value(text,"FrameSampleSeconds",frameSeconds);
                Value(text,"RecentAverageFPS",sample.Length/frameSeconds);Value(text,"RecentAverageFrameMs",frameSeconds*1000/sample.Length);
                Value(text,"RecentP95FrameMs",milliseconds[Math.Min(milliseconds.Length-1,(int)Math.Ceiling(milliseconds.Length*.95)-1)]);
                Value(text,"RecentWorstFrameMs",milliseconds[milliseconds.Length-1]);
            }
            else Value(text,"FrameStatistics","No frame samples yet");
        });
        Section(text,"Replay and recording",()=> {
            var manager=ReplayManager.i;
            Value(text,"ManagerAvailable",manager!=null);if(manager==null)return;
            Value(text,"State",manager.GetState());Value(text,"Transitioning",manager.IsTransitioning);Value(text,"FailedReplayWorld",manager.IsFailedReplayWorld);
            Value(text,"RecordingReservationHeld",ReplayRecordingRecovery.HasRecordingReservation);
            Value(text,"RecordingFinalizationPending",Recorder.Active==null&&ReplayRecordingRecovery.HasRecordingReservation);
            Value(text,"LastRecordingStopStatus",manager.LastRecordingStopStatus);
            Value(text,"RecordingClockSeconds",manager.virtualTime);
            var player=manager.Playback;
            Value(text,"PlayerAvailable",player!=null);
            if(player!=null)
            {
                input.ReplayPath=OwnField<string>(player,"_path");
                Value(text,"CurrentReplayFile",input.ReplayPath);Value(text,"TimeSeconds",player.currentVirtualTime);Value(text,"DurationSeconds",player.replayDuration);
                Value(text,"Speed",player.PlaybackSpeed);Value(text,"Paused",player.IsPaused);Value(text,"Seeking",player.IsSeeking);Value(text,"Reloading",player.IsReloading);
                Value(text,"Buffering",player.IsBuffering);Value(text,"SeekTargetSeconds",player.SeekTargetTime);Value(text,"SeekPhase",player.SeekPhase);Value(text,"PlaybackFailure",player.PlaybackFailure);
                Value(text,"CheckpointCount",player.SeekCheckpointCount);Value(text,"CheckpointEstimatedBytes",player.SeekCheckpointBytes);Value(text,"LastSeekSuffixEvents",player.LastSeekScannedEvents);
                Value(text,"CompletedSeeks",player.CompletedSeeks);
                Value(text,"MotionSmoothingPreset",player.MotionSmoothingPreset);Value(text,"SmoothedAircraftCount",player.SmoothedAircraftCount);
                Value(text,"SceneTransitionPending",ReplayMod.Misc.SceneHelper.IsTransitionPending);
                Value(text,"SceneTransitionPhase",ReplayMod.Misc.SceneHelper.TransitionPhase);
                Value(text,"OwnedReplayEffectCount",ReplayVfxPlayback.OwnedObjectCount);
                Value(text,"ProtectedReplayParticleCount",ReplayVfxPlayback.ProtectedParticleCount);Value(text,"CloseFadeOverrideCount",ReplayVfxPlayback.CloseFadeOverrideCount);
                Value(text,"ReplayCloudClockSystemCount",ReplayCloudParticleClock.ProtectedSystemCount);
                Value(text,"ReplayCloudOriginalUnscaledSystemCount",ReplayCloudParticleClock.OriginalUnscaledSystemCount);
                Value(text,"ReplayCloudParticleCount",ReplayCloudParticleClock.CurrentParticleCount);
                Value(text,"NativeTimeScale",Time.timeScale);
                Value(text,"CloudLayerHeight",NetworkSceneSingleton<LevelInfo>.i?.cloudHeight);
                Value(text,"CameraHeight",SceneSingleton<CameraStateManager>.i!=null?(float?)SceneSingleton<CameraStateManager>.i.transform.position.y:null);
                Value(text,"TakePlaybackEndLimit",player.PlaybackEndTime);Value(text,"NativeCameraRefreshActive",NativeCameraRefreshScope.IsActive);
                Value(text,"ProducerTimeSeconds",OwnField<double>(player,"_producedTime"));Value(text,"ProducerFinished",OwnField<bool>(player,"_producerFinished"));
                Value(text,"ProducerFailure",OwnField<Exception>(player,"_producerError"));
                var buffer=OwnField<BoundedEventBuffer>(player,"_buffer");Value(text,"BufferedEventCount",buffer?.Count);Value(text,"BufferFreeSlots",buffer?.FreeSpace);
                var actors=player.Actors().Where(p=>p.Value!=null).Select(p=>p.Value).ToArray();
                Value(text,"ActorCount",actors.Length);Value(text,"AircraftCount",actors.Count(a=>a is Aircraft));Value(text,"BuildingCount",actors.Count(a=>a is Building));
                Value(text,"MissileCount",actors.Count(a=>a is Missile));Value(text,"NeutralActorCount",actors.Count(a=>a.NetworkHQ==null));
            }
            var recorder=Recorder.Active;
            Value(text,"RecorderAvailable",recorder!=null);
            if(recorder!=null)
            {
                Value(text,"Recording",recorder.IsRecording);Value(text,"Rolling",recorder.IsRolling);Value(text,"RecordingSessionValid",recorder.SessionValid);
                Value(text,"RecordedEventCount",OwnField<long>(recorder,"_eventCount"));Value(text,"RecorderTimeSeconds",OwnField<double>(recorder,"_virtualTime"));
                Value(text,"RecorderActorCaptureFailures",recorder.CaptureFailureCount);
                Value(text,"RollingSeconds",recorder.RollingDuration);Value(text,"RollingBytes",recorder.RollingBytes);Value(text,"RollingMemoryLimited",recorder.RollingMemoryLimited);
                Value(text,"RecordingQueuedBytes",recorder.RecordingQueuedBytes);Value(text,"RecordingFailure",recorder.RecordingFailure);
                if(string.IsNullOrEmpty(input.ReplayPath))input.ReplayPath=recorder.RecordingPath;
            }
            var rolling=RollingRecordingController.Instance;
            if(rolling!=null){Value(text,"RollingEnabled",rolling.Enabled);Value(text,"RollingRetentionSeconds",rolling.RetentionSeconds);Value(text,"RollingBusy",rolling.Busy);Value(text,"RollingStatus",rolling.Status);}
        });
        Section(text,"Camera and clean capture",()=> {
            var director=ReplayDirector.i;Value(text,"DirectorAvailable",director!=null);if(director==null)return;
            Value(text,"Status",director.Status);Value(text,"CleanCapture",director.Clean);Value(text,"CleanCaptureKey",director.CleanCaptureHotkeyLabel);
            Value(text,"ReconstructionCoverVisible",ReplayMod.UI.TrailerSuiteUi.Instance?.ReconstructionCoverVisible);
            Value(text,"CameraKeysEnabled",director.CameraKeysEnabled);Value(text,"SpeedRampEnabled",director.SpeedRampEnabled);Value(text,"ManualSpeed",director.ManualSpeed);
            Value(text,"FreeCameraSpeed",director.FreeCameraSpeed);Value(text,"CurrentFOV",director.CurrentCameraFov);Value(text,"RailActive",director.RailActive);
            Value(text,"RailSpeed",director.RailSpeed);Value(text,"RailDirection",director.RailDirection);Value(text,"StepFrameRate",director.FrameRate);
            Value(text,"RailEffectiveSpeed",director.RailEffectiveSpeed);Value(text,"RailMatchesTargetSpeed",director.RailMatchesTargetSpeed);Value(text,"RailFollowsView",director.RailFollowsView);
            Value(text,"CameraRollDegrees",director.CameraRollDegrees);Value(text,"StudioKey",ConfigManager.StudioHotkeyLabel);
            Value(text,"CloseCameraOverride",ReplayCloseCameraVisibility.HasOverride);
            Value(text,"BoundCockpitRendererCount",ReplayInteriorPresentation.CockpitRendererCount);Value(text,"BoundPilotRendererCount",ReplayInteriorPresentation.PilotRendererCount);
            Value(text,"BoundCockpitFlightScreenCount",ReplayInteriorPresentation.FlightScreenCount);
            Value(text,"FlightInstrumentsEnabled",director.Project.flightInstruments);Value(text,"SpectatedPlayerNamesEnabled",director.Project.showPlayerNames);
            Value(text,"PathPointCount",director.PathPointCount);Value(text,"PathAttached",director.PathAttached);Value(text,"PathSmooth",director.PathSmooth);Value(text,"PathSmoothness",director.PathSmoothness);Value(text,"TakeActive",director.TakeActive);Value(text,"TakeStatus",director.TakeStatus);
            var nativeCamera=SceneSingleton<CameraStateManager>.i;
            if(nativeCamera!=null)
            {
                Value(text,"NativeCameraState",nativeCamera.currentState?.GetType().Name);Value(text,"NativeFollowingUnitId",nativeCamera.followingUnit?.persistentID.Id);
                if(nativeCamera.mainCamera!=null){Value(text,"MainCameraNearPlane",nativeCamera.mainCamera.nearClipPlane);Value(text,"MainCameraLayerMask",nativeCamera.mainCamera.cullingMask);}
                if(nativeCamera.cockpitCamRender!=null)
                {
                    Value(text,"CockpitOverlayEnabled",nativeCamera.cockpitCamRender.enabled);Value(text,"CockpitOverlayActive",nativeCamera.cockpitCamRender.gameObject.activeInHierarchy);
                    Value(text,"CockpitOverlayFOV",nativeCamera.cockpitCamRender.fieldOfView);Value(text,"CockpitOverlayNearPlane",nativeCamera.cockpitCamRender.nearClipPlane);Value(text,"CockpitOverlayFarPlane",nativeCamera.cockpitCamRender.farClipPlane);
                }
            }
            var project=director.Project;
            if(project!=null)
            {
                Value(text,"CameraRig",project.rig);Value(text,"CameraTargetReplayId",project.target);Value(text,"ProjectFOV",project.fov);Value(text,"PreRollSeconds",project.preRoll);
                Value(text,"ReduceCloseClipping",project.reduceCloseClipping);Value(text,"DetailedCockpit",project.detailedCockpit);Value(text,"ShowExistingSeatedPilots",project.seatedPilots);
                Value(text,"MotionSmoothingScope",project.motionSmoothingScope);
                Value(text,"CameraKeyCount",project.cameras?.Count);Value(text,"SpeedKeyCount",project.speeds?.Count);Value(text,"CameraPathCount",project.path?.Count);
                Value(text,"ManualBookmarkCount",project.bookmarks?.Count);Value(text,"WeatherOverride",project.environment?.enabled);Value(text,"WeatherHour",project.environment?.hour);
                Value(text,"LightMode",project.lightMode);Value(text,"LandingLightMode",project.landingLightMode);
            }
            Value(text,"IMGUIHiddenCallbacks",CleanCaptureImguiGuard.CallbackCount);Value(text,"IMGUIHideFailures",CleanCaptureImguiGuard.FailureCount);
        });
        Section(text,"ReplayMod settings (explicit allowlist)",()=> {
            Config(text,"Toggle menu",ConfigManager.ToggleMenu);Config(text,"Toggle record",ConfigManager.ToggleRecord);Config(text,"Toggle pause",ConfigManager.TogglePause);
            Config(text,"Toggle clean capture",ConfigManager.ToggleCleanCapture);Config(text,"Save recent gameplay",ConfigManager.SaveRecentClip);
            Config(text,"Camera roll left",ConfigManager.CameraRollLeft);Config(text,"Camera roll right",ConfigManager.CameraRollRight);Config(text,"Level camera",ConfigManager.CameraLevel);
            Config(text,"Add camera waypoint",ConfigManager.CreateCameraWaypoint);Config(text,"Delete last camera waypoint",ConfigManager.DeleteLastCameraWaypoint);
            Config(text,"Mirror control point",ConfigManager.MirrorControlPoint);Config(text,"Deselect point",ConfigManager.DeselectWaypoint);
            Config(text,"Enable logs",ConfigManager.EnableDebugLogs);Config(text,"Keep recent gameplay",ConfigManager.ContinuousRecording);Config(text,"Recent clip seconds",ConfigManager.RecentClipSeconds);
            Config(text,"Enable background test control",ConfigManager.EnableBackgroundControl);
            var appearance=ConfigManager.ReadAppearance();
            Value(text,"StudioTheme",appearance.Preset);Value(text,"StudioAccent",appearance.Accent);Value(text,"StudioPanel",appearance.Panel);
            Value(text,"StudioButton",appearance.Button);Value(text,"StudioText",appearance.Text);Value(text,"StudioPanelOpacity",appearance.Opacity);
        });
        Section(text,"Loaded BepInEx plugins",()=> {
            int count=0;
            foreach(var pair in Chainloader.PluginInfos.OrderBy(p=>p.Key,StringComparer.Ordinal))
            {
                if(count++>=256){text.AppendLine("Plugin list truncated after 256 entries.");break;}
                var info=pair.Value;text.AppendLine("GUID="+info.Metadata.GUID+"; Name="+info.Metadata.Name+"; Version=v"+info.Metadata.Version+"; Path="+info.Location);
            }
            Value(text,"LoadedPluginCount",Chainloader.PluginInfos.Count);
        });
        input.Snapshot=text.ToString();
        lock(warningGate)input.RecentWarnings=warnings.Count==0?"No recent Unity warnings/errors captured since ReplayMod startup.\n":string.Join("\n",warnings);
        input.Logs.Add(new DiagnosticsLogSource("BepInEx-LogOutput.log",Path.Combine(bep,"LogOutput.log")));
        input.Logs.Add(new DiagnosticsLogSource("BepInEx-LogOutput.log.1",Path.Combine(bep,"LogOutput.log.1")));
        if(!string.IsNullOrEmpty(unityLog))input.Logs.Add(new DiagnosticsLogSource("Unity-Player.log",unityLog));
        input.HashFiles.Add(new DiagnosticsLogSource("ReplayMod.dll",typeof(Plugin).Assembly.Location));
        input.HashFiles.Add(new DiagnosticsLogSource("Assembly-CSharp.dll",typeof(Unit).Assembly.Location));
        return input;
    }

    private static T OwnField<T>(object instance,string name)
    {
        // Own managed fields only; never reflect into a native Unity object.
        var field=instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
        return field==null?default(T):(T)field.GetValue(instance);
    }
    private static void Section(StringBuilder text,string title,Action capture)
    {
        text.AppendLine().AppendLine("["+title+"]");
        try{capture();}catch(Exception ex){text.AppendLine("Section partially unavailable: "+ex.GetType().Name+": "+ex.Message);}
    }
    private static void Value(StringBuilder text,string key,object value)
    {
        string result=value==null?"<unavailable>":value is IFormattable f?f.ToString(null,CultureInfo.InvariantCulture):value.ToString();
        text.Append(key).Append('=').AppendLine(Limit(result,8192));
    }
    private static void Config<T>(StringBuilder text,string key,ConfigEntry<T> entry) => Value(text,key,entry==null?null:(object)entry.Value);
}

// Adds only constant-time samples to ordinary frames; sorting and runtime
// inspection happen exclusively when the player clicks Export logs.
public sealed class ReplayDiagnosticsSampler : MonoBehaviour
{
    private void Awake(){ReplayDiagnostics.Initialize();}
    private void Update(){ReplayDiagnostics.SampleFrame(Time.realtimeSinceStartup,Time.unscaledDeltaTime);}
}
