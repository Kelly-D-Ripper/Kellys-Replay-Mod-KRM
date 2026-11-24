using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using NuclearOption.SceneLoading;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using ReplayMod.Misc;
using UnityEngine;

namespace ReplayMod.Core;

[DefaultExecutionOrder(-1000)]
public partial class Player : MonoBehaviour
{
    private string _path;
    private MapKey _key;
    private long _eventsCount, _dataStartOffset;
    private byte _formatMajor;
    private UnitController _unitCotroller;
    private FileStream _fileStream;
    private BinaryReader _binaryReader;
    private CancellationTokenSource _producerCts, _lifetimeCts;
    private Task _producerTask;
    private BoundedEventBuffer _buffer;
    private readonly SemaphoreSlim _seekGate = new SemaphoreSlim(1,1);
    private readonly PlaybackControlState _controls = new PlaybackControlState();
    private readonly Queue<IReplayEvent> _restoreBuffer = new Queue<IReplayEvent>();
    private readonly Queue<IReplayEvent> _pendingEvents = new Queue<IReplayEvent>();
    private readonly List<ReplayMod.Data.Index> _index = new List<ReplayMod.Data.Index>();
    private double[] _indexTimes = Array.Empty<double>();
    private SeekCheckpointCache<ReplaySeekState> _checkpoints;
    private float? _temporaryClock;
    private bool _onReset, _stepping, _buffering, _faulted;
    private double? _pendingTarget;
    private double _bufferStarted, _producedTime=-1;
    private volatile bool _producerFinished;
    private Exception _producerError;
    private double? _seekTarget;
    private string _seekPhase=string.Empty, _playbackFailure=string.Empty;
    private PositionRotation _seekPose;
    private float _seekLens;
    private bool _hasSeekPose;
    private GameplayUI _nativeSlowUi;
    public int CompletedSeeks {get;private set;}

    public string RecordingPath => _path;
    public double replayDuration {get;private set;}
    public double currentVirtualTime {get;private set;}
    private double? playbackEndTime;
    public double? PlaybackEndTime
    {
        get=>playbackEndTime;
        set=>playbackEndTime=value.HasValue&&!double.IsNaN(value.Value)&&!double.IsInfinity(value.Value)?Math.Max(0,Math.Min(replayDuration,value.Value)):(double?)null;
    }
    private double ClampPlaybackTime(double time)=>PlaybackControlState.LimitTime(time,replayDuration,PlaybackEndTime);
    public bool IsPaused=>_controls.IsPaused;
    public bool IsSeeking=>_controls.IsSeeking||_onReset||_stepping;
    public bool IsReloading=>_onReset;
    public bool IsBuffering=>_buffering;
    public float PlaybackSpeed=>_controls.Speed;
    public double? SeekTargetTime=>_controls.IsSeeking?_seekTarget:null;
    public string SeekPhase=>_seekPhase;
    public string PlaybackFailure=>_playbackFailure;
    public int SeekCheckpointCount=>_checkpoints?.Count??0;
    public long SeekCheckpointBytes=>_checkpoints?.Bytes??0;
    public long LastSeekScannedEvents {get;private set;}
    public List<ReplayMod.Director.Bookmark> AutomaticBookmarks {get;private set;}=new List<ReplayMod.Director.Bookmark>();
    public IEnumerable<KeyValuePair<uint,Unit>> Actors()=>_unitCotroller?.Actors()??Enumerable.Empty<KeyValuePair<uint,Unit>>();
    public Unit FindActor(uint id)=>_unitCotroller?.FindActor(id);
    private int _motionSmoothing;
    private uint _motionSmoothingActor;
    private bool _motionSmoothingSelectedOnly=true;
    private BoundedEventBuffer _motionBarrierBuffer;
    private Func<uint,double> _motionBarrierQuery;
    public int MotionSmoothingPreset=>_motionSmoothing;
    public bool MotionSmoothingDensityLimited {get;private set;}
    public int SmoothedAircraftCount=>_unitCotroller?.SmoothedAircraftCount??0;
    public void SetMotionSmoothing(int preset,uint selectedActorId,bool selectedOnly)
    {
        _motionSmoothing=ReplayMotionSmoothing.Enabled(preset)?preset:0;
        if(_motionSmoothing==0)MotionSmoothingDensityLimited=false;
        _motionSmoothingActor=selectedActorId;_motionSmoothingSelectedOnly=selectedOnly;
        ConfigureMotionSmoothing();
    }
    private void ConfigureMotionSmoothing()
    {
        if(!ReferenceEquals(_motionBarrierBuffer,_buffer))
        {_motionBarrierBuffer=_buffer;_motionBarrierQuery=_buffer==null?null:_buffer.NextMotionBarrier;}
        _unitCotroller?.SetMotionSmoothing(_motionSmoothing,_motionSmoothingActor,_motionSmoothingSelectedOnly,_motionBarrierQuery);
    }
    private bool MotionLookAheadReady(double target)=>_motionSmoothing==0||ReplayMotionSmoothing.LookAheadReady(target,Volatile.Read(ref _producedTime),_producerFinished);
    public void RefreshMotionForCamera()
    {
        if(_motionSmoothing==0||!enabled||_faulted||IsSeeking||IsBuffering||!ReplayRuntime.IsPlayback||SceneHelper.IsTransitionPending)return;
        _unitCotroller?.RefreshRecordedUnitPoses(currentVirtualTime);
        _unitCotroller?.RefreshRecordedAircraftParts(currentVirtualTime);
    }
    public void RefreshAircraftVisualsLate()
    {
        if(enabled&&!_faulted&&!SceneHelper.IsTransitionPending)
        {
            _unitCotroller?.RefreshRecordedUnitPoses(currentVirtualTime);
            _unitCotroller?.RefreshRecordedAircraftParts(currentVirtualTime);
            _unitCotroller?.RefreshAircraftVisualsLate(currentVirtualTime);
            _unitCotroller?.RefreshRecordedAircraftParts(currentVirtualTime);
            _unitCotroller?.RefreshFuelFlames(currentVirtualTime);
            ReplayDamageParticleMotion.Refresh(currentVirtualTime);
        }
    }

    public async Task StartPlaying(string filePath)
    {
        if(!File.Exists(filePath))throw new FileNotFoundException("No such replay file",filePath);
        if(_fileStream!=null)await StopPlaying();
        await _seekGate.WaitAsync();
        string openingStage="reading the recording";
        try
        {
        enabled=false;
        _path=filePath;_lifetimeCts=new CancellationTokenSource();
        _controls.CancelSeeks();_faulted=false;_playbackFailure=string.Empty;
        currentVirtualTime=0;_pendingTarget=null;_buffering=false;
        _fileStream=new FileStream(filePath,FileMode.Open,FileAccess.Read,FileShare.Read,65536);
        _binaryReader=new BinaryReader(_fileStream,Encoding.UTF8,true);
        _buffer=new BoundedEventBuffer(ReplayManager.PlayerBoundedCapacity);
        ReadHeader(_binaryReader);
        // Validate and index before creating a replay world. The same pass builds
        // bounded reusable prefix states; no second background cache scan.
        var loadingToken=_lifetimeCts.Token;
        openingStage="checking the recording";
        var recordedUnits=await Task.Run(()=>BuildIndex(loadingToken));
        loadingToken.ThrowIfCancellationRequested();
        // The recording header only knows what existed when capture began.
        // Later spawns must be checked too, before replacing the current scene.
        var missingUnits=recordedUnits.Where(key=>!Encyclopedia.Lookup.ContainsKey(key)).OrderBy(key=>key,StringComparer.Ordinal).ToArray();
        if(missingUnits.Length>0)
            throw new InvalidDataException("Replay requires missing unit definitions: "+string.Join(", ",missingUnits.Take(20))+(missingUnits.Length>20?" (and "+(missingUnits.Length-20)+" more)":""));
        openingStage="loading the recorded terrain";
        if(!await SceneHelper.LoadSceneAsync(_key,Path.GetFileName(_path),loadingToken))
            throw new IOException("The replay map could not be loaded");
        loadingToken.ThrowIfCancellationRequested();
        _unitCotroller=new UnitController(_key);
        ConfigureMotionSmoothing();
        openingStage="preparing the replay world";
        await ResetWorld();
        loadingToken.ThrowIfCancellationRequested();
        foreach(var item in FactionRegistry.HQLookup)item.Value.preventJoin=true;
        StartProducer(_dataStartOffset);
        enabled=true;Pause();
        openingStage="restoring the opening frame";
        await RenderAt(0);
        ApplyPlaybackClock();
        }
        catch(Exception error) when(!(error is OperationCanceledException))
        {throw new IOException("Replay failed while "+openingStage+": "+error.Message,error);}
        finally{_seekGate.Release();}
    }

    private void ReadHeader(BinaryReader br)
    {
        var header=ReplayRecordingHeader.Read(br);
        _formatMajor=header.Format;
        ReplayRuntime.FormatMajor=_formatMajor;
        _key=new MapKey((MapKey.KeyType)header.MapType,header.MapPath);
        replayDuration=header.Duration;_eventsCount=header.EventCount;
        _=new ReplayTimelineValidation(replayDuration,_eventsCount,_formatMajor<3);
        var missing=new List<string>();
        if(header.RequiresHeaderResources)foreach(string key in header.Resources)if(!Encyclopedia.Lookup.ContainsKey(key))missing.Add(key);
        if(missing.Count>0)throw new InvalidDataException("Replay requires missing resources: "+string.Join(", ",missing));
        _dataStartOffset=_fileStream.Position;
    }

    private HashSet<string> BuildIndex(CancellationToken token)
    {
        _index.Clear();_cameraLifetimes.Clear();_checkpoints?.Dispose();
        _checkpoints=new SeekCheckpointCache<ReplaySeekState>(32L*1024*1024,64,30,state=>state.Dispose());
        var markers=new List<ReplayMod.Director.Bookmark>();
        var recordedUnits=new HashSet<string>(StringComparer.Ordinal);
        var names=new Dictionary<uint,string>();var lastMarker=new Dictionary<string,double>();
        var validation=new ReplayTimelineValidation(replayDuration,_eventsCount,_formatMajor<3);
        using var state=new ReplaySeekState(true);
        using var file=new FileStream(_path,FileMode.Open,FileAccess.Read,FileShare.Read,65536);
        using var reader=new BinaryReader(file);file.Position=_dataStartOffset;
        double groupTime=-1;
        while(file.Position<file.Length)
        {
            token.ThrowIfCancellationRequested();
            long offset=file.Position;IReplayEvent e=null;
            try
            {
                e=ReplayRecordTransport.ReadEvent(reader,_formatMajor);
                validation.Add(e.Time,e is UpdateTurretEvent);
                ObserveCameraLifetime(e,validation.Count);
                if(e.Time!=groupTime)
                {
                    // Prefix ends before the next timestamp group; exact seeks
                    // include every duplicate at the target via UpperBound.
                    if(_formatMajor>=3&&groupTime>=0&&_checkpoints.ShouldAdd(groupTime,0))
                    {
                        long bytes=state.EstimatedBytes;
                        if(_checkpoints.ShouldAdd(groupTime,bytes))
                            _checkpoints.Add(groupTime,offset,bytes,state.Clone());
                    }
                    _index.Add(new ReplayMod.Data.Index{VirtualTime=e.Time,Offset=offset});groupTime=e.Time;
                }
                uint actor=0;string label=null;
                if(e is SpawnEvent spawn){recordedUnits.Add(spawn.jsonKey??string.Empty);names[spawn.unitId]=spawn.jsonKey;if(!spawn.isAircraft&&spawn.ownerId!=spawn.unitId){actor=spawn.unitId;label="Missile launch";}}
                else if(e is PartDamageEvent hit&&hit.pierce+hit.blast+hit.impact>0){actor=hit.unitId;label="Hit";}
                else if(e is EjectionEvent eject){actor=eject.unitId;label="Ejection";}
                else if(e is LandingEvent landing){actor=landing.unitId;label="Landing";}
                if(label!=null)
                {
                    string markerKey=label+actor;
                    if(!lastMarker.TryGetValue(markerKey,out var last)||e.Time-last>1)
                    {
                        markers.Add(new ReplayMod.Director.Bookmark{time=e.Time,name=label+" · "+(names.TryGetValue(actor,out var name)?name:actor.ToString()),actor=actor,actorLifetime=_cameraLifetimes.CurrentGeneration(actor),automatic=true});
                        lastMarker[markerKey]=e.Time;
                    }
                }
                // Add owns the event even on failure.
                var owned=e;e=null;state.Add(owned);
            }
            catch(Exception error) when(!(error is OperationCanceledException))
            {throw new InvalidDataException($"Replay is corrupt or incomplete near byte {offset}: {error.Message}",error);}
            finally{if(e!=null)ReplayEventFactory.Return(e);}
        }
        validation.Complete();
        if(_formatMajor>=3&&groupTime>=0&&_checkpoints.ShouldAdd(groupTime,0))
        {
            long bytes=state.EstimatedBytes;
            if(_checkpoints.ShouldAdd(groupTime,bytes))_checkpoints.Add(groupTime,file.Length,bytes,state.Clone());
        }
        if(validation.UsedLegacyCount)Plugin.SafeLogWarning("Legacy replay header omitted turret events; accepted the known upstream counter defect");
        _indexTimes=_index.Select(e=>e.VirtualTime).ToArray();AutomaticBookmarks=markers;
        Plugin.SafeLog($"Replay validated: {validation.Count} events, {_checkpoints.Count} seek checkpoints ({_checkpoints.Bytes/1024/1024} MiB estimated)");
        return recordedUnits;
    }

    // Compatibility entry points: the validated index scan now owns caching.
    public void StopCachingProcess(){}
    public void BuildCache(){}

    private void StartProducer(long offset)
    {
        _producerCts?.Dispose();_producerCts=new CancellationTokenSource();
        _producerFinished=false;Interlocked.Exchange(ref _producedTime,-1);Volatile.Write(ref _producerError,null);
        var token=_producerCts.Token;var file=_fileStream;var reader=_binaryReader;var buffer=_buffer;
        _producerTask=Task.Run(()=>ProducerLoop(token,offset,file,reader,buffer));
    }
    private void ProducerLoop(CancellationToken token,long offset,FileStream file,BinaryReader reader,BoundedEventBuffer buffer)
    {
        bool complete=false;IReplayEvent owned=null;
        try
        {
            file.Position=offset;double previous=-1;
            while(file.Position<file.Length)
            {
                token.ThrowIfCancellationRequested();
                owned=ReplayRecordTransport.ReadEvent(reader,_formatMajor);
                double time=owned.Time;
                if(double.IsNaN(time)||double.IsInfinity(time)||time<0||time<previous||time>replayDuration+.000001)
                    throw new InvalidDataException("Replay producer encountered an invalid or unordered timestamp");
                buffer.Add(owned,token);owned=null;previous=time;
                Interlocked.Exchange(ref _producedTime,time);
            }
            token.ThrowIfCancellationRequested();complete=true;
        }
        catch(OperationCanceledException){}
        catch(Exception error){Volatile.Write(ref _producerError,error);Plugin.SafeLogError("Replay reader failed: "+error);}
        finally
        {
            if(owned!=null)ReplayEventFactory.Return(owned);
            _producerFinished=complete;
        }
    }
    private async Task StopProducer()
    {
        _producerCts?.Cancel();
        var task=_producerTask;
        if(task!=null)await task;
        _producerTask=null;_producerCts?.Dispose();_producerCts=null;
    }
    private void ReturnQueues()
    {
        while(_restoreBuffer.Count>0)ReplayEventFactory.Return(_restoreBuffer.Dequeue());
        while(_pendingEvents.Count>0)ReplayEventFactory.Return(_pendingEvents.Dequeue());
        _pendingTarget=null;
    }
    public async Task StopPlaying()
    {
        enabled=false;_lifetimeCts?.Cancel();_controls.CancelSeeks();_producerCts?.Cancel();
        await _seekGate.WaitAsync();
        try
        {
            await StopProducer();_temporaryClock=null;_buffering=false;
            await UniTask.SwitchToMainThread();
            // ReplayManager restores normal time after this teardown. Clear
            // only the native toggle owned by this player in this scene, so
            // same-scene reuse starts consistently and a newer world/player
            // cannot have its own slow-motion flag overwritten by old cleanup.
            if(_nativeSlowUi!=null&&ReplayRuntime.IsPlayback&&ReferenceEquals(ReplayManager.i.Playback,this)&&
                ReferenceEquals(SceneSingleton<GameplayUI>.i,_nativeSlowUi))GameplayUI.GameSlowMotion=false;
            _nativeSlowUi=null;
            _unitCotroller?.ReleaseOwnedPresentationObjects();
            _buffer?.ClearAndReturnToPool();ReturnQueues();
            _binaryReader?.Dispose();_binaryReader=null;_fileStream?.Dispose();_fileStream=null;
            _lifetimeCts?.Dispose();_lifetimeCts=null;
            _checkpoints?.Dispose();_checkpoints=null;
            _cameraLifetimes.Clear();
            _seekTarget=null;_seekPhase=string.Empty;_hasSeekPose=false;
        }
        finally{_seekGate.Release();}
    }
    public async Task StopPlayingAndDestroy(){await StopPlaying();UnityEngine.Object.Destroy(this);}

    private void RequireOperation(long? request=null)
    {
        if(!enabled||_fileStream==null||_lifetimeCts==null||_lifetimeCts.IsCancellationRequested||
            (!request.HasValue&&_controls.IsSeeking)||
            (request.HasValue&&!_controls.IsLatest(request.Value)))throw new OperationCanceledException("Replay operation superseded or stopped");
    }
    public async UniTask TimelineJump(double targetTime,double preRoll=0,bool? resumeAfter=null)
    {
        if(!enabled||_faulted||_fileStream==null||double.IsNaN(targetTime)||double.IsInfinity(targetTime))return;
        ReplayMod.Director.ReplayDirector.i?.StopRail();
        double requestedTime=Math.Max(0,Math.Min(replayDuration,targetTime));
        if(!_hasSeekPose)
        {
            var currentCamera=SceneSingleton<CameraStateManager>.i;
            currentCamera.GetCameraPosition(out _seekPose);
            _seekLens=ReplayMod.Director.ReplayDirector.i?.CurrentCameraFov??currentCamera.mainCamera.fieldOfView;
            _hasSeekPose=true;
        }
        long request=_controls.RequestSeek(resumeAfter);_seekTarget=requestedTime;_seekPhase="Queued";ApplyPlaybackClock();
        await _seekGate.WaitAsync();
        bool resetting=false;
        try
        {
            RequireOperation(request);
            if(double.IsNaN(preRoll)||double.IsInfinity(preRoll))preRoll=0;
            double restoreTime=Math.Max(0,requestedTime-Math.Max(0,Math.Min(5,preRoll)));
            resetting=_onReset=true;_seekPhase="Reloading world";
            var camera=SceneSingleton<CameraStateManager>.i;
            await StopProducer();RequireOperation(request);
            if(SceneSingleton<GameplayUI>.i!=null)SceneSingleton<GameplayUI>.i.ResumeGame();
            _unitCotroller?.ReleaseOwnedPresentationObjects();
            await SceneHelper.StopReplayConnectionAsync(_lifetimeCts.Token);RequireOperation(request);
            if(!await SceneHelper.LoadSceneAsync(_key,Path.GetFileName(_path),_lifetimeCts.Token))throw new IOException("The replay map could not be reloaded");
            RequireOperation(request);
            long offset=CalculateOffset(restoreTime);
            await UniTask.SwitchToMainThread();RequireOperation(request);
            // Have you tried turning the entire world off and on again?
            // A new native world gets a new ownership table. Destroyed Unity
            // references and recycled persistent IDs cannot leak across rewinds.
            _unitCotroller=new UnitController(_key);
            ConfigureMotionSmoothing();
            await ResetWorld();RequireOperation(request);ApplyPlaybackClock();
            _seekPhase="Restoring recorded state";
            await RestoreWorldState(offset,restoreTime,request);RequireOperation(request);
            // Let destroyed map objects settle before spawning the restored set.
            await UniTask.NextFrame();RequireOperation(request);
            await UniTask.NextFrame();RequireOperation(request);
            await ExecuteRestore(request);RequireOperation(request);
            camera=SceneSingleton<CameraStateManager>.i;
            camera.SetCameraPosition(_seekPose);
            ReplayMod.Director.ReplayDirector.i?.RestoreCameraLens(_seekLens);
            currentVirtualTime=restoreTime;StartProducer(offset);
            ReplayMod.Director.ReplayDirector.i?.PrepareSeek();
            _seekPhase=requestedTime>restoreTime?"Warming effects":"Preparing frame";
            while(currentVirtualTime<requestedTime)
            {
                RequireOperation(request);
                double next=Math.Min(requestedTime,currentVirtualTime+Math.Max(.001,Time.unscaledDeltaTime));
                await RenderAtCore(next,request);RequireOperation(request);
                ReplayMod.Director.ReplayDirector.i?.UpdateCamera(currentVirtualTime);
                _temporaryClock=1;ApplyPlaybackClock();
                await UniTask.NextFrame();RequireOperation(request);
                _temporaryClock=null;ApplyPlaybackClock();
            }
            await RenderAtCore(requestedTime,request);RequireOperation(request);
            CompletedSeeks++;
            if(requestedTime>=replayDuration)_controls.Pause();
        }
        catch(OperationCanceledException){}
        catch(Exception error)
        {
            if(_controls.IsLatest(request)){await FailPlayback(error);throw;}
        }
        finally
        {
            if(resetting){_temporaryClock=null;_onReset=false;}
            if(_controls.CompleteSeek(request)){_seekTarget=null;_seekPhase=string.Empty;_hasSeekPose=false;}
            try{ApplyPlaybackClock();}finally{_seekGate.Release();}
        }
    }
    private long CalculateOffset(double target)
    {
        int i=PlaybackReadiness.UpperBound(_indexTimes,target);
        return i<_index.Count?_index[i].Offset:_fileStream.Length;
    }
    private async UniTask ResetWorld()
    {
        await _unitCotroller.ResetWorld();
        _buffer.ClearAndReturnToPool();ReturnQueues();
        ReplayManager.i.onReset?.Invoke();
    }
    private async UniTask RestoreWorldState(long endOffset,double time,long request)
    {
        RequireOperation(request);ReturnQueues();
        var checkpoint=_checkpoints?.Find(time);
        using var state=checkpoint?.State.Clone()??new ReplaySeekState(true);
        // Legacy input masks depend on the reader's previous values. Rewinding
        // uses a fresh reader and a complete prefix; format 3 stores full masks.
        _binaryReader.Dispose();_binaryReader=new BinaryReader(_fileStream,Encoding.UTF8,true);
        _fileStream.Position=checkpoint?.Offset??_dataStartOffset;
        LastSeekScannedEvents=0;
        while(_fileStream.Position<endOffset)
        {
            state.Add(ReplayRecordTransport.ReadEvent(_binaryReader,_formatMajor));
            if(++LastSeekScannedEvents%2048==0){await UniTask.NextFrame();RequireOperation(request);}
        }
        RequireOperation(request);state.Drain(_restoreBuffer);
    }
    private async UniTask ExecuteRestore(long request)
    {
        while(_restoreBuffer.Count>0)
        {
            RequireOperation(request);
            bool spawning=_restoreBuffer.Peek() is SpawnEvent;
            int budget=spawning?100:500;
            for(int n=0;n<budget&&_restoreBuffer.Count>0&&(_restoreBuffer.Peek() is SpawnEvent)==spawning;n++)
            {
                RequireOperation(request);
                var e=_restoreBuffer.Dequeue();try{ExecuteReplayEvent(e);}finally{ReplayEventFactory.Return(e);}
            }
            await UniTask.NextFrame();RequireOperation(request);
        }
    }

    private void CheckProducer()
    {
        var error=Volatile.Read(ref _producerError);
        if(error!=null)throw new IOException("Replay file reader failed: "+error.Message,error);
        if(_producerTask?.IsFaulted==true)throw new IOException("Replay file reader failed",_producerTask.Exception);
    }
    private bool DrainAvailable(double target,int budget)
    {
        for(int n=0;n<budget;n++)
        {
            if(!_buffer.TryTakeIfReady(target,out var e))return true;
            if(e.Time<=target)
            {
                if(_pendingEvents.Count>=250000){ReplayEventFactory.Return(e);throw new InvalidDataException("Replay timestamp group exceeds the safe event staging limit");}
                _pendingEvents.Enqueue(e);
            }
            else
            {
                try{ExecuteReplayEvent(e);}finally{ReplayEventFactory.Return(e);}
            }
        }
        return false;
    }
    private bool TryCommitFrame(double target)
    {
        CheckProducer();
        bool capacityReached=_buffer.FreeSpace==0;
        // Snapshot readiness BEFORE the decisive drain. Otherwise a producer
        // could append more equal-time events and publish its later watermark
        // after we observed an empty queue, making that empty observation stale.
        bool ordinaryReady=PlaybackReadiness.CanCommit(target,Volatile.Read(ref _producedTime),_producerFinished,capacityReached);
        bool ready=ordinaryReady&&MotionLookAheadReady(target);
        bool drained=DrainAvailable(target,10000);
        CheckProducer();
        // A buffer consisting only of future actions cannot release slots while
        // the clock is held. Preserve ordinary playback by disabling this whole
        // optional presentation setting, rather than mixing frame-dependent
        // filtering or failing the replay. Director consumes the flag and saves
        // Off in its project before its next setting update.
        if(_motionSmoothing!=0&&drained&&!MotionLookAheadReady(target)&&_buffer.FreeSpace==0)
        {
            _motionSmoothing=0;MotionSmoothingDensityLimited=true;ConfigureMotionSmoothing();
            Plugin.SafeLogWarning("Replay motion smoothing disabled: recording density fills the future-action buffer before its smoothing lookahead. Original motion is restored.");
            ready=ordinaryReady;
        }
        if(!ready||!drained)return false;
        while(_pendingEvents.Count>0&&_pendingEvents.Peek().Time<=target)
        {
            var e=_pendingEvents.Dequeue();
            try{ExecuteReplayEvent(e);if(!BoundedEventBuffer.IsInterpolation(e))_buffer.MarkActionExecuted(e);}
            finally{ReplayEventFactory.Return(e);}
        }
        // Lifecycle actions above release held future samples for the new actor.
        if(!DrainAvailable(target,10000))return false;
        currentVirtualTime=target;_unitCotroller.Update(target);
        return true;
    }
    private void ExecuteReplayEvent(IReplayEvent value)
    {
        try{value.Execute(_unitCotroller);}
        catch(Exception error) when(!(error is OperationCanceledException))
        {throw new InvalidDataException("Replay "+value.EventType+" at "+value.Time.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s failed: "+error.Message,error);}
    }
    private void BeginBuffering()
    {
        if(!_buffering){_buffering=true;_bufferStarted=Time.realtimeSinceStartup;}
        ApplyPlaybackClock();
    }
    private void EndBuffering(){_buffering=false;_pendingTarget=null;ApplyPlaybackClock();}
    private void Update()
    {
        // Freeze the native simulation before its frame updates when the reader
        // has not yet closed the next replay timestamp group.
        if(!IsSeeking&&!_faulted&&_unitCotroller!=null&&_buffer!=null)
        {
            try
            {
                CheckProducer();
                double desired=ClampPlaybackTime(PlaybackControlState.FrameTarget(currentVirtualTime,IsPaused?0:Time.unscaledDeltaTime,PlaybackSpeed));
                double target=_pendingTarget??(PlaybackSpeed>1?PlaybackControlState.FastStepTarget(currentVirtualTime,desired):desired);
                if(!PlaybackReadiness.CanCommit(target,Volatile.Read(ref _producedTime),_producerFinished,_buffer.FreeSpace==0)||!MotionLookAheadReady(target))BeginBuffering();
            }
            catch(Exception error){_ = FailPlayback(error);}
        }
        ApplyPlaybackClock();
    }
    private void LateUpdate()
    {
        if(IsSeeking||_faulted||_unitCotroller==null||_buffer==null)return;
        try
        {
            CheckProducer();
            double desired=ClampPlaybackTime(PlaybackControlState.FrameTarget(currentVirtualTime,IsPaused?0:Time.unscaledDeltaTime,PlaybackSpeed));
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            int steps=PlaybackSpeed>1?PlaybackControlState.MaximumFastStepsPerFrame:1;
            for(int n=0;n<steps;n++)
            {
                double target=_pendingTarget??(PlaybackSpeed>1?PlaybackControlState.FastStepTarget(currentVirtualTime,desired):desired);
                // A pause pressed during starvation keeps the displayed frame fixed.
                if(IsPaused&&target>currentVirtualTime){_bufferStarted=Time.realtimeSinceStartup;ApplyPlaybackClock();return;}
                _pendingTarget=target;
                if(!TryCommitFrame(target))
                {
                    BeginBuffering();
                    if(Time.realtimeSinceStartup-_bufferStarted>15)throw new TimeoutException("Replay file reader stalled for 15 seconds");
                    return;
                }
                EndBuffering();
                if(currentVirtualTime>=replayDuration||(PlaybackEndTime.HasValue&&currentVirtualTime>=PlaybackEndTime.Value)){Pause();return;}
                if(IsPaused||currentVirtualTime>=desired||
                   (System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency>=5.0)break;
            }
        }
        catch(Exception error){_ = FailPlayback(error);}
    }
    private async Task FailPlayback(Exception error)
    {
        if(_faulted)return;
        _faulted=true;enabled=false;_playbackFailure=error.Message;_controls.CancelSeeks();
        _temporaryClock=null;_buffering=false;TimeScaleManager.Scale=0;
        if(ReplayMod.Director.ReplayDirector.i!=null)ReplayMod.Director.ReplayDirector.i.Status="Replay stopped: "+error.Message;
        Plugin.SafeLogError("Replay stopped after playback failure: "+error);
        await StopProducer();_buffer?.ClearAndReturnToPool();ReturnQueues();
    }
    public async void StepForward(double seconds)
    {
        if(!enabled||_faulted||_fileStream==null||IsSeeking||seconds<=0||double.IsNaN(seconds)||double.IsInfinity(seconds))return;
        ReplayMod.Director.ReplayDirector.i?.StopRail();_stepping=true;
        await _seekGate.WaitAsync();int capture=Time.captureFramerate;
        try
        {
            RequireOperation();Pause();
            double target=Math.Min(replayDuration,currentVirtualTime+seconds);
            await RenderAtCore(target,null);RequireOperation();
            Time.captureFramerate=Math.Max(1,Math.Min(1000,(int)Math.Round(1/seconds)));
            _temporaryClock=1;ApplyPlaybackClock();
            await UniTask.NextFrame();RequireOperation();
            ReplayMod.Director.ReplayDirector.i?.UpdateCamera(currentVirtualTime);
            await UniTask.WaitForEndOfFrame(this);RequireOperation();
        }
        catch(OperationCanceledException){}
        catch(Exception error){await FailPlayback(error);}
        finally
        {
            Time.captureFramerate=capture;_temporaryClock=null;_stepping=false;
            if(!_controls.IsSeeking)_controls.Pause();
            try{ApplyPlaybackClock();}finally{_seekGate.Release();}
        }
    }
    public async UniTask RenderAt(double time)=>await RenderAtCore(time,null);
    private async UniTask RenderAtCore(double time,long? request)
    {
        if(double.IsNaN(time)||double.IsInfinity(time))throw new ArgumentOutOfRangeException(nameof(time));
        RequireOperation(request);double target=Math.Max(0,Math.Min(replayDuration,time));
        if(target<currentVirtualTime)throw new InvalidOperationException("Backward positioning requires a timeline seek");
        BeginBuffering();double started=Time.realtimeSinceStartup;
        try
        {
            while(true)
            {
                RequireOperation(request);
                if(TryCommitFrame(target)){EndBuffering();return;}
                if(Time.realtimeSinceStartup-started>15)throw new TimeoutException("Replay file reader stalled for 15 seconds");
                await UniTask.NextFrame();RequireOperation(request);
            }
        }
        finally{_buffering=false;ApplyPlaybackClock();}
    }
    public void Pause(){_controls.Pause();ApplyPlaybackClock();}
    public void Resume(){if(_faulted||!enabled||ReplayMod.Director.ReplayDirector.i?.CameraTakeHoldsPlayback==true)return;_controls.Play();ApplyPlaybackClock();}
    public void TogglePause(){if(_faulted||!enabled||ReplayMod.Director.ReplayDirector.i?.CameraTakeHoldsPlayback==true)return;_controls.Toggle();ApplyPlaybackClock();}
    public void SetPlaybackSpeed(float speed)
    {
        _controls.SetSpeed(speed);
        // The native key toggles this flag before requesting a time factor.
        // Synchronize it with toolbar/ramp changes so the next key press always
        // toggles the speed the user currently sees, rather than a stale flag.
        if(enabled&&!_faulted&&_fileStream!=null&&ReplayRuntime.IsPlayback)
        {
            var nativeUi=SceneSingleton<GameplayUI>.i;
            if(nativeUi!=null)
            {
                _nativeSlowUi=nativeUi;
                bool slow=_controls.Speed<1f;
                if(GameplayUI.GameSlowMotion!=slow)GameplayUI.GameSlowMotion=slow;
            }
        }
        ApplyPlaybackClock();
    }
    public void SetNativePlaybackTimeFactor(float value)
    {
        if(!enabled||_faulted||_fileStream==null||IsSeeking||!_controls.ApplyNativeTimeFactor(value))return;
        // Director.Update reapplies its manual speed every frame. Keep it in
        // sync so a native slow-motion key cannot briefly change the Unity
        // clock and then get undone on the next studio update.
        if(value>0)ReplayMod.Director.ReplayDirector.i?.SetPlaybackSpeed(_controls.Speed);
        ApplyPlaybackClock();
    }
    public void RestoreRecordedEngineRpm(Aircraft aircraft)
    {
        if(enabled&&!_faulted&&aircraft!=null)_unitCotroller?.RestoreRecordedRpm(aircraft);
    }
    public bool OwnsReplayAircraft(Aircraft aircraft)=>_unitCotroller?.OwnsReplayAircraft(aircraft)??false;
    public bool OwnsReplayUnit(Unit unit)=>_unitCotroller?.OwnsReplayUnit(unit)??false;
    public bool HasRecordedAircraftVisuals(Aircraft aircraft)=>_unitCotroller?.HasRecordedAircraftVisuals(aircraft)??false;
    public void ApplyPlaybackClock()
    {
        if(_fileStream==null||(!enabled&&!_faulted))return;
        float scale=_faulted||_buffering?0:(_temporaryClock??(IsSeeking?0:_controls.ClockScale));
        float previous=TimeScaleManager.Scale;
        if(Math.Abs(previous-scale)>.00001f)
        {
            // PropFan normally sets its source volume in FixedUpdate, which may
            // take 0.2 real seconds at 0.1x. Only prime a silent recorded source
            // at actual playback start; seeking and paused audio stay native.
            List<(AudioSource source,AudioClip clip)> primed=null;
            bool starting=previous<=0&&scale>0&&ReplayRuntime.IsPlayback&&!IsSeeking&&!_controls.IsPaused&&!_faulted&&!_buffering;
            if(starting)
                primed=_unitCotroller?.PrimeRecordedPropAudio();
            TimeScaleManager.Scale=scale;
            UnitController.WakePrimedPropAudio(primed);
            if(starting)_unitCotroller?.StartRecordedJetAudio();
        }
    }
    public bool ShouldContinue(uint id)=>!_faulted&&(_unitCotroller?.ShouldContinuePatchedMethod(id)??true);
}

