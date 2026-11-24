using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class BrawlerLiveEngineProbe
{
    static bool AudioPrimeCheck=>AutoAudioPrimeProbe.Enabled||ProductionAudioPrimeProbe.Enabled;
    static int AudioPrimed=>AutoAudioPrimeProbe.Enabled?AutoAudioPrimeProbe.Primed:ProductionAudioPrimeProbe.Primed;
    private sealed class Sample{public double time;public bool ignition;public float rpm;}
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    static async UniTask PlaceReplayListenerNear(PropFan engine,Action<string> log)
    {
        ReplayDirector.i.EnterFreeCamera();var manager=SceneSingleton<CameraStateManager>.i;
        if(manager==null||engine?.source==null)throw new Exception("Cannot position replay listener near engine");
        Vector3 offset=new Vector3(0,3,-12),target=engine.source.transform.position;
        manager.SetCameraPosition((target+offset).ToGlobalPosition(),Quaternion.LookRotation(-offset,Vector3.up));
        await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
        var listener=UnityEngine.Object.FindObjectOfType<AudioListener>();
        if(listener==null)throw new Exception("Replay listener unavailable");
        float distance=Vector3.Distance(listener.transform.position,engine.source.transform.position);
        if(distance>40)throw new Exception("Replay listener did not follow the isolated test camera: "+distance);
        log("BRAWLER AUDIO near listener distance="+distance.ToString("F1")+"m sourceSpatial="+engine.source.spatialBlend.ToString("F1"));
    }
    static float Expected(List<Sample> samples,double time)
    {
        int n=samples.FindLastIndex(x=>x.time<=time);if(n<0)return float.NaN;
        var a=samples[n];if(n+1>=samples.Count)return a.rpm;
        var b=samples[n+1];if(a.ignition!=b.ignition||b.time-a.time>.5||b.time<=a.time)return a.rpm;
        return Mathf.Lerp(a.rpm,b.rpm,(float)((time-a.time)/(b.time-a.time)));
    }
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        float oldVolume=AudioListener.volume;AudioListener.volume=0;
        try
        {
            await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Brawler engine recording test");
            TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
            var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("Brawler live recorder unavailable");
            string partial=recorder.RecordingPath;
            var def=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];
            var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
            var aircraft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
            if(aircraft==null)throw new Exception("Brawler did not spawn");uint id=aircraft.persistentID.Id;
            log("BRAWLER LIVE initial loadout="+(aircraft.loadout==null?"null":aircraft.loadout.weapons==null?"null-weapons":aircraft.loadout.weapons.Count.ToString())+" hardpointSets="+(aircraft.weaponManager?.hardpointSets?.Length??-1));
            await Wait(1);
            log("BRAWLER LIVE after initialization loadout="+(aircraft.loadout==null?"null":aircraft.loadout.weapons==null?"null-weapons":aircraft.loadout.weapons.Count.ToString())+" hardpointSets="+(aircraft.weaponManager?.hardpointSets?.Length??-1)+" mounted="+(aircraft.weaponManager?.hardpointSets?.Count(s=>s!=null&&s.weaponMount!=null)??-1));
            var expectedLoadout=aircraft.loadout.weapons.Select(w=>w?.jsonKey??"NULLPTR").ToArray();
            var liveEngines=aircraft.engines.OfType<PropFan>().ToArray();if(liveEngines.Length!=2)throw new Exception("Live Brawler prop fans unavailable");
            bool tapping=Environment.GetEnvironmentVariable("REPLAY_ENGINE_AUDIO_TAP")=="1";
            if(tapping)
            {
                float until=Time.realtimeSinceStartup+10;while(!OwnAudioSession.Mute()){if(Time.realtimeSinceStartup>until)throw new Exception("Isolated process audio session was not found; audio remained muted");await UniTask.NextFrame();}
                AudioListener.volume=1;log("BRAWLER AUDIO Windows process-only mute verified");
            }
            if(tapping)
            {
                var source=liveEngines[0].source;var listener=UnityEngine.Object.FindObjectOfType<AudioListener>();
                log("BRAWLER AUDIO live source clip="+(source.clip==null?"null":source.clip.name)+" playing="+source.isPlaying+" volume="+source.volume+" mute="+source.mute+" spatial="+source.spatialBlend+" listener="+(listener==null?"none":listener.transform.position.ToString("F1"))+" sourcePos="+source.transform.position.ToString("F1"));
            }
            if(tapping)liveEngines[0].source.spatialBlend=0; // Isolate source signal from listener distance in this muted test.
            var liveTap=tapping?liveEngines[0].source.gameObject.AddComponent<AudioTap>():null;
            float liveEnd=Time.realtimeSinceStartup+7,livePrevious=-1,liveMaxStep=0;int liveFrames=0;
            while(Time.realtimeSinceStartup<liveEnd)
            {
                float pitch=liveEngines[0].source.pitch;
                if(livePrevious>=0)liveMaxStep=Mathf.Max(liveMaxStep,Mathf.Abs(pitch-livePrevious));
                livePrevious=pitch;liveFrames++;await UniTask.NextFrame();
            }
            if(liveTap!=null)log("BRAWLER AUDIO live tap samples="+liveTap.Save("brawler-live-v16-continuity.wav"));
            log("BRAWLER LIVE original frames="+liveFrames+" maxPitchStep="+liveMaxStep.ToString("F4"));int captureFaults=recorder.CaptureFailureCount;
            await ReplayManager.i.SwitchState(ModStates.Idle);
            string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
            if(!File.Exists(file))throw new Exception("Brawler recording missing");ReplayIntegrityCheck.Check(file);
            var samples=new List<Sample>();string[] recordedLoadout=null;int spawnCount=0,earlyActions=0;
            using(var input=new BinaryReader(File.OpenRead(file)))
            {
                var header=ReplayRecordingHeader.Read(input);
                for(long n=0;n<header.EventCount;n++)
                {
                    var e=ReplayRecordTransport.ReadEvent(input,header.Format);
                    if(e is SpawnEvent spawn&&spawn.unitId==id){spawnCount++;recordedLoadout=spawn.weapons?.Select(w=>w?.jsonKey??"NULLPTR").ToArray();}
                    if(spawnCount==0&&((e is SetGearEvent gear&&gear.unitId==id)||(e is PartDamageEvent damage&&damage.unitId==id)||(e is EjectionEvent eject&&eject.unitId==id)||(e is SetActiveStationEvent station&&station.unitId==id)||(e is WeaponManagerFireEvent fire&&fire.unitId==id)))earlyActions++;
                    if(e is AircraftStatusEvent status&&status.unitId==id&&status.rpm.Length>=2)samples.Add(new Sample{time=status.Time,ignition=status.ignition,rpm=status.rpm[0]});
                    ReplayEventFactory.Return(e);
                }
            }
            if(samples.Count<60||captureFaults!=0||earlyActions!=0||spawnCount!=1||recordedLoadout==null||!recordedLoadout.SequenceEqual(expectedLoadout))throw new Exception("Brawler capture incomplete: samples="+samples.Count+" faults="+captureFaults+" earlyActions="+earlyActions+" spawns="+spawnCount+" recordedLoadout="+(recordedLoadout==null?"null":string.Join(",",recordedLoadout))+" expected="+string.Join(",",expectedLoadout));
            log("BRAWLER LIVE spawn and all "+recordedLoadout.Length+" initialized loadout slots recorded once; no early actions preceding spawn");
            await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;
            if(!ReplayRuntime.IsPlayback||player==null)throw new Exception("Brawler recording did not reopen");
            double start=samples[0].time+.5,end=Math.Min(samples.Last().time-.2,start+5);
            player.Pause();await player.TimelineJump(start,0,false);
            var replay=player.FindActor(id) as Aircraft;var engines=replay?.engines.OfType<PropFan>().ToArray();
            if(engines==null||engines.Length!=2||engines.Any(e=>e.source==null))throw new Exception("Brawler replay engine sources unavailable");
            bool nearListener=Environment.GetEnvironmentVariable("REPLAY_ENGINE_LISTENER_NEAR")=="1";
            if(tapping&&nearListener)await PlaceReplayListenerNear(engines[0],log);else if(tapping)engines[0].source.spatialBlend=0;
            var replayTap=Environment.GetEnvironmentVariable("REPLAY_ENGINE_AUDIO_TAP")=="1"?engines[0].source.gameObject.AddComponent<AudioTap>():null;
            ReplayDirector.i.SetPlaybackSpeed(1);player.Resume();int frames=0,playing=0,resets=0,priorSample=-1;float maxError=0,maxPitchStep=0,priorPitch=-1;
            float deadline=Time.realtimeSinceStartup+15;
            while(player.currentVirtualTime<end)
            {
                if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Brawler recorded replay did not advance");
                float expected=Expected(samples,player.currentVirtualTime),actual=engines[0].GetRPM(),pitch=engines[0].source.pitch;
                if(!float.IsNaN(expected))maxError=Mathf.Max(maxError,Mathf.Abs(actual-expected));
                if(priorPitch>=0&&player.currentVirtualTime>start+.5)
                {
                    float step=Mathf.Abs(pitch-priorPitch);
                    if(step>maxPitchStep){maxPitchStep=step;if(step>.05f)log("BRAWLER LIVE pitch spike time="+player.currentVirtualTime.ToString("F3")+" from="+priorPitch.ToString("F3")+" to="+pitch.ToString("F3")+" throttle="+replay.controlInputs.throttle.ToString("F3")+" rpm="+actual.ToString("F1")+" power="+engines[0].powerRatio.ToString("F3"));}
                }
                priorPitch=pitch;frames++;
                if(engines.All(e=>e.source.isPlaying))
                {
                    playing++;var source=engines[0].source;int sample=source.timeSamples;
                    if(priorSample>=0&&sample<priorSample&&!(source.loop&&priorSample>source.clip.samples*.8&&sample<source.clip.samples*.2))resets++;
                    priorSample=sample;
                }
                else priorSample=-1;
                await UniTask.NextFrame();
            }
            if(replayTap!=null)log("BRAWLER AUDIO replay tap samples="+replayTap.Save("brawler-replay-v16-continuity.wav"));
            log("BRAWLER LIVE samples="+samples.Count+" frames="+frames+" bothPlaying="+playing+" resets="+resets+" maxRpmError="+maxError.ToString("F1")+" maxPitchStepAfterWarmup="+maxPitchStep.ToString("F4"));
            if(frames<30||playing<frames*.9||maxError>100||maxPitchStep>.08f||resets>0)throw new Exception("Recorded Brawler engine replay diverged or jittered");
            if(AudioPrimeCheck)
            {
                player.Pause();var one=engines[0].source;var two=engines[1].source;
                bool muteOne=one.mute,muteTwo=two.mute,loopOne=one.loop;float volumeOne=one.volume,volumeTwo=two.volume;
                try
                {
                    one.mute=true;one.volume=0;two.mute=false;two.volume=.3f;int before=AudioPrimed;int beforeRestart=PropellerInitializationProbe.changed;int beforeWake=ProductionPropWakeProbe.selected;
                    player.TogglePause();if(one.volume!=0||Math.Abs(two.volume-.3f)>.0001f||AudioPrimed!=before)throw new Exception("Audio prime changed a muted or already-audible source");
                    if(ProductionPropWakeProbe.Enabled&&ProductionPropWakeProbe.selected!=beforeWake)throw new Exception("Production wake selected a muted or already-audible source");
                    if(Environment.GetEnvironmentVariable("REPLAY_TEST_PROP_INITIALIZATION")=="fresh"&&PropellerInitializationProbe.changed!=beforeRestart)throw new Exception("Scoped test restarted a muted or already-audible source");
                    player.Pause();one.mute=false;two.mute=false;one.volume=0;two.volume=0;before=AudioPrimed;beforeRestart=PropellerInitializationProbe.changed;beforeWake=ProductionPropWakeProbe.selected;
                    player.TogglePause();if(one.volume<=0||two.volume<=0||AudioPrimed<before+2)throw new Exception("Toggle play did not prime both silent recorded sources");
                    if(ProductionPropWakeProbe.Enabled&&ProductionPropWakeProbe.selected<beforeWake+2)throw new Exception("Production wake did not select both eligible recorded loops");
                    if(Environment.GetEnvironmentVariable("REPLAY_TEST_PROP_INITIALIZATION")=="fresh"&&PropellerInitializationProbe.changed<beforeRestart+2)throw new Exception("Scoped test did not restart both eligible silent sources");
                    player.Pause();
                    if(ProductionAudioPrimeProbe.Enabled)
                    {
                        var controller=typeof(Player).GetField("_unitCotroller",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(player);
                        var map=controller?.GetType().GetField("_engineRpm",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(controller) as Dictionary<uint,float[]>;
                        if(map==null||!map.TryGetValue(id,out var savedRpm))throw new Exception("Recorded engine state missing from scope fixture");
                        one.volume=0;two.volume=0;map.Remove(id);beforeRestart=PropellerInitializationProbe.changed;beforeWake=ProductionPropWakeProbe.selected;
                        try{player.TogglePause();if(one.volume!=0||two.volume!=0)throw new Exception("Audio prime changed an unrecorded source");if(ProductionPropWakeProbe.Enabled&&ProductionPropWakeProbe.selected!=beforeWake)throw new Exception("Production wake selected an unrecorded source");if(Environment.GetEnvironmentVariable("REPLAY_TEST_PROP_INITIALIZATION")=="fresh"&&PropellerInitializationProbe.changed!=beforeRestart)throw new Exception("Scoped test restarted an unrecorded source");player.Pause();}
                        finally{if(!player.IsPaused)player.Pause();map[id]=savedRpm;}
                    }
                    if(ProductionPropWakeProbe.Enabled)
                    {
                        one.mute=false;one.volume=0;one.loop=false;two.mute=false;two.volume=.3f;beforeWake=ProductionPropWakeProbe.selected;
                        player.TogglePause();if(ProductionPropWakeProbe.selected!=beforeWake)throw new Exception("Production wake selected a non-looping source");player.Pause();one.loop=loopOne;
                        log("BRAWLER PRODUCTION WAKE non-looping source left native");
                    }
                    log("BRAWLER AUTO PRIME SCOPE PASS muted, audible and unrecorded sources unchanged; toggle play primed silent recorded sources");
                }
                finally{one.mute=muteOne;two.mute=muteTwo;one.volume=volumeOne;two.volume=volumeTwo;one.loop=loopOne;}
            }
            int cycles=int.TryParse(Environment.GetEnvironmentVariable("REPLAY_ENGINE_SEEK_LOOPS"),out int requestedCycles)?Mathf.Clamp(requestedCycles,1,5):1;
            for(int cycle=0;cycle<cycles;cycle++)foreach(var check in new[]{(speed:.1f,offset:.4,span:.3),(speed:1f,offset:1.0,span:1.0),(speed:.1f,offset:1.8,span:.3)})
            {
                player.Pause();double from=start+check.offset,to=from+check.span;await player.TimelineJump(from,0,false);
                replay=player.FindActor(id) as Aircraft;engines=replay?.engines.OfType<PropFan>().ToArray();
                if(engines==null||engines.Length!=2||engines.Any(e=>e.source==null))throw new Exception("Engine missing after seek");
                if(tapping&&nearListener)await PlaceReplayListenerNear(engines[0],log);else if(tapping)engines[0].source.spatialBlend=0;
                var src=engines[0].source;var clip=src.clip;float[] initial=new float[Math.Min(clip.samples,clip.frequency)*clip.channels];bool readable=clip.GetData(initial,0);int first=Array.FindIndex(initial,v=>Math.Abs(v)>.0002f);
                if(cycle==0&&check.offset==.4&&Environment.GetEnvironmentVariable("REPLAY_ENGINE_CLIP_EXPORT")=="1")
                {
                    if(!readable)throw new Exception("Cannot read native propeller clip for diagnostic comparison");
                    AudioTap.SaveClipBeginning(Environment.GetEnvironmentVariable("REPLAY_AUDIO_TAG"),"native-prop-clip-first-second.wav",initial,clip.frequency,clip.channels);
                    log("BRAWLER NATIVE CLIP frequency="+clip.frequency+" channels="+clip.channels+" samples="+clip.samples+" sourceCount="+src.GetComponents<AudioSource>().Length);
                }
                log("BRAWLER SEEK SOURCE speed="+check.speed+" samples="+src.timeSamples+" pitch="+src.pitch.ToString("F3")+" playing="+src.isPlaying+" volume="+src.volume.ToString("F3")+" rpmRatio="+engines[0].rpmRatio.ToString("F3")+" clip="+clip.name+" loop="+src.loop+" loadType="+clip.loadType+" clipFirstSignal="+(readable&&first>=0?(first/(double)clip.channels/clip.frequency).ToString("F3"):"unavailable"));
                if(Environment.GetEnvironmentVariable("REPLAY_AUDIO_SETTLE")=="1") {await Wait(1);log("BRAWLER SEEK SOURCE after paused settling volume="+src.volume.ToString("F3")+" playing="+src.isPlaying+" sample="+src.timeSamples);}
                if(Environment.GetEnvironmentVariable("REPLAY_AUDIO_PRIME")=="1")
                {
                    var engine=engines[0];float ratio=engine.nominalRPM>0?engine.currentRPM/engine.nominalRPM:0;float drive=Mathf.Min(engine.powerRatio,replay.controlInputs.throttle);
                    src.volume=engine.volumeBase+engine.rpmVolumePortion*ratio+engine.aoaVolumePortion*drive*ratio;
                    log("BRAWLER SEEK SOURCE test-only primed volume="+src.volume.ToString("F3")+" rpmRatio="+ratio.ToString("F3"));
                }
                if(Environment.GetEnvironmentVariable("REPLAY_AUDIO_PREWARM")=="1")
                {
                    if(Environment.GetEnvironmentVariable("REPLAY_AUDIO_PRIME")!="1")throw new Exception("Muted prewarm requires a primed source");
                    bool originalMute=src.mute;src.mute=true;int before=src.timeSamples;await Wait(.4f);
                    log("BRAWLER SEEK SOURCE muted test-only prewarm sample="+before+"→"+src.timeSamples+" paused="+player.IsPaused);
                    src.mute=originalMute;
                }
                bool noFilter=Environment.GetEnvironmentVariable("REPLAY_ENGINE_NO_FILTER")=="1";
                if(noFilter&&engines[0].source.GetComponents<AudioTap>().Length!=0)throw new Exception("An old test filter remained on the source");
                var tap=tapping&&!noFilter?engines[0].source.gameObject.AddComponent<AudioTap>():null;
                ReplayDirector.i.SetPlaybackSpeed(check.speed);int tapBeforeResume=tap?.CapturedFrames??0;player.Resume();int tapAfterResume=tap?.CapturedFrames??0;
                if(tap!=null)log("BRAWLER TAP RESUME cycle="+cycle+" speed="+check.speed+" offset="+check.offset+" framesBefore="+tapBeforeResume+" framesAfter="+tapAfterResume+" rate="+AudioSettings.outputSampleRate);
                var trace=Environment.GetEnvironmentVariable("REPLAY_ENGINE_TRACE")=="1"?new StringBuilder("elapsed,tapFrames,volume,playing,virtual,listenerPaused,muted,enabled,active,clipLoad,timeSamples,pitch,rpm,rpmRatio,scale,dspTime,sourceRms,sourcePeak\n"):null;
                float[] sourceOutput=trace==null?null:new float[1024];
                if(trace!=null&&cycle==0&&check.offset==.4)log("BRAWLER SOURCE COMPONENTS "+string.Join(",",src.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType().FullName))+" mixer="+(src.outputAudioMixerGroup==null?"none":src.outputAudioMixerGroup.name));
                int total=0,both=0,sourceResets=0,lastSample=-1;float largestError=0,firstVolumeAt=-1,phaseStart=Time.realtimeSinceStartup,until=phaseStart+12;
                while(player.currentVirtualTime<to)
                {
                    if(Time.realtimeSinceStartup>until)throw new Exception("Engine playback stalled at "+check.speed+"x");
                    await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);float expected=Expected(samples,player.currentVirtualTime);
                    if(trace!=null&&Time.realtimeSinceStartup-phaseStart<.7f)
                    {
                        var source=engines[0].source;var culture=System.Globalization.CultureInfo.InvariantCulture;
                        source.GetOutputData(sourceOutput,0);float peak=0,sum=0;foreach(float value in sourceOutput){peak=Mathf.Max(peak,Mathf.Abs(value));sum+=value*value;}
                        trace.Append((Time.realtimeSinceStartup-phaseStart).ToString("R",culture)).Append(',').Append(tap?.CapturedFrames??0).Append(',')
                            .Append(source.volume.ToString("R",culture)).Append(',').Append(source.isPlaying?1:0).Append(',').Append(source.isVirtual?1:0).Append(',').Append(AudioListener.pause?1:0).Append(',')
                            .Append(source.mute?1:0).Append(',').Append(source.enabled?1:0).Append(',').Append(source.gameObject.activeInHierarchy?1:0).Append(',').Append(source.clip.loadState).Append(',').Append(source.timeSamples).Append(',')
                            .Append(source.pitch.ToString("R",culture)).Append(',').Append(engines[0].GetRPM().ToString("R",culture)).Append(',')
                            .Append(engines[0].rpmRatio.ToString("R",culture)).Append(',').Append(TimeScaleManager.Scale.ToString("R",culture)).Append(',')
                            .Append(AudioSettings.dspTime.ToString("R",culture)).Append(',').Append(Mathf.Sqrt(sum/sourceOutput.Length).ToString("R",culture)).Append(',').Append(peak.ToString("R",culture)).Append('\n');
                    }
                    if(!float.IsNaN(expected))largestError=Mathf.Max(largestError,Mathf.Abs(engines[0].GetRPM()-expected));
                    if(firstVolumeAt<0&&engines[0].source.volume>.05f)firstVolumeAt=Time.realtimeSinceStartup-phaseStart;
                    if(engines.All(e=>e.source.isPlaying))
                    {
                        both++;var source=engines[0].source;int current=source.timeSamples;
                        if(lastSample>=0&&current<lastSample&&!(source.loop&&lastSample>source.clip.samples*.8&&current<source.clip.samples*.2))sourceResets++;
                        lastSample=current;
                    }
                    else lastSample=-1;total++;
                }
                player.Pause();int audioSamples=tap==null?0:tap.Save("brawler-replay-v16-"+(Environment.GetEnvironmentVariable("REPLAY_AUDIO_PRIME")=="1"?"primed-":Environment.GetEnvironmentVariable("REPLAY_AUDIO_SETTLE")=="1"?"settled-":"")+check.speed.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"-"+check.offset.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+(cycles>1?"-r"+cycle:"")+".wav");
                if(trace!=null)
                {
                    string tag=Environment.GetEnvironmentVariable("REPLAY_AUDIO_TAG")??throw new Exception("Trace requires an evidence tag");
                    if(tag.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'&&c!='_'))throw new Exception("Invalid trace tag");
                    string name=tag+"-engine-trace-"+check.speed.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"-"+check.offset.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"-r"+cycle+".csv";
                    using(var stream=new FileStream(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ghost-probe",name),FileMode.CreateNew,FileAccess.Write))using(var writer=new StreamWriter(stream))writer.Write(trace.ToString());
                }
                log("BRAWLER SEEK AUDIO speed="+check.speed+" frames="+total+" bothPlaying="+both+" resets="+sourceResets+" maxRpmError="+largestError.ToString("F1")+" audioSamples="+audioSamples+" volumeAbove005At="+firstVolumeAt.ToString("F3"));
                if(total<30||both<total*.9||sourceResets>0||largestError>100||(tapping&&!noFilter&&audioSamples==0))throw new Exception("Engine audio state failed following seek at "+check.speed+"x");
            }
            player.Pause();await player.TimelineJump(start+.4,0,false);replay=player.FindActor(id) as Aircraft;engines=replay.engines.OfType<PropFan>().ToArray();
            if(tapping&&nearListener)await PlaceReplayListenerNear(engines[0],log);else if(tapping)engines[0].source.spatialBlend=0;
            ReplayDirector.i.SetPlaybackSpeed(1);player.Resume();await Wait(1);
            var controlNormal=tapping?engines[0].source.gameObject.AddComponent<AudioTap>():null;await Wait(1);
            if(controlNormal!=null){log("BRAWLER AUDIO already-running normal tap samples="+controlNormal.Save("brawler-running-v16-normal.wav"));UnityEngine.Object.Destroy(controlNormal);}
            ReplayDirector.i.SetPlaybackSpeed(.1f);await Wait(.3f);
            var controlSlow=tapping?engines[0].source.gameObject.AddComponent<AudioTap>():null;await Wait(3);
            if(controlSlow!=null){log("BRAWLER AUDIO already-running slow tap samples="+controlSlow.Save("brawler-running-v16-slow.wav"));UnityEngine.Object.Destroy(controlSlow);}
            player.Pause();
            if(AudioPrimeCheck)
            {
                int beforeWarm=AudioPrimed;await player.TimelineJump(start+1,.5,false);
                if(!player.IsPaused||AudioPrimed!=beforeWarm)throw new Exception("Paused seek pre-roll primed source audio");
                log("BRAWLER AUTO PRIME PREROLL PASS paused seek did not prime audio");
                ReplayDirector.i.SetPlaybackSpeed(1);player.Resume();await Wait(.2f);int before=AudioPrimed;
                await player.TimelineJump(start+1,0,true);
                if(player.IsPaused||player.IsSeeking||AudioPrimed<before+2)throw new Exception("Auto-resuming seek did not prime the restored propeller sources");
                player.Pause();log("BRAWLER AUTO PRIME SEEK PASS resuming seek primed restored propeller sources");
            }
            ReplayDirector.i.SetPlaybackSpeed(1);log("BRAWLER SEEK AUDIO PASS repeated seeks at normal and slow speeds preserved playing engine sources");
            if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPLAY_TEST_PROP_INITIALIZATION")))
            {if(PropellerInitializationProbe.changed<1||PropellerInitializationProbe.phaseMismatches!=0)throw new Exception("Test-only propeller initialization was not exercised or lost playback position");log("BRAWLER TEST PROP INITIALIZATION mode="+Environment.GetEnvironmentVariable("REPLAY_TEST_PROP_INITIALIZATION")+" changed="+PropellerInitializationProbe.changed+" protected="+PropellerInitializationProbe.skippedProtected+" phaseMismatches="+PropellerInitializationProbe.phaseMismatches);}
            if(PropellerSkipFixedProbe.Enabled)
            {if(PropellerSkipFixedProbe.skipped<1)throw new Exception("Test-only propeller fixed-update suppression was not exercised");log("BRAWLER TEST PROP FIXED skipped="+PropellerSkipFixedProbe.skipped);}
            if(ProductionPropWakeProbe.Enabled)
            {if(ProductionPropWakeProbe.selected<1||ProductionPropWakeProbe.invalid!=0||ProductionPropWakeProbe.phaseMismatches!=0)throw new Exception("Production propeller wake selected an invalid source or lost its sample position");log("BRAWLER PRODUCTION WAKE selected="+ProductionPropWakeProbe.selected+" invalid="+ProductionPropWakeProbe.invalid+" phaseMismatches="+ProductionPropWakeProbe.phaseMismatches);}
            if(AudioPrimeCheck){if(AudioPrimed<1||ProductionAudioPrimeProbe.ProtectedChanged!=0)throw new Exception("Audio prime failed its source scope checks");log("BRAWLER AUTO PRIME sources="+AudioPrimed+" protectedChanged="+ProductionAudioPrimeProbe.ProtectedChanged);}
            log("BRAWLER LIVE PASS actual isolated flight recording restored smooth muted engine audio state");
        }
        finally{AudioListener.volume=oldVolume;}
    }
}
