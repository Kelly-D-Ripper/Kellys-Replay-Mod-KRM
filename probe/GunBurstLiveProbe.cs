using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class GunBurstLiveProbe
{
    static async UniTask Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)await UniTask.NextFrame();}
    static Gun Rotary(Unit unit)=>unit.partLookup.Where(p=>p!=null).SelectMany(p=>p.GetComponentsInChildren<Gun>(true))
        .First(g=>g!=null&&g.attachedUnit==unit&&(g.info?.weaponName??"").Contains(Environment.GetEnvironmentVariable("REPLAY_BURST_BRAWLER")=="1"?"35mm":"20mm Rotary"));
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        float oldVolume=AudioListener.volume;AudioListener.volume=0;
        try
        {
            await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Muted gun burst test");
            TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
            var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("Burst recording unavailable");
            string partial=recorder.RecordingPath;
            bool brawler=Environment.GetEnvironmentVariable("REPLAY_BURST_BRAWLER")=="1";Unit vehicle;
            if(brawler)
            {
                var definition=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
                vehicle=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,definition.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
            }
            else
            {
                var definition=(ShipDefinition)Encyclopedia.Lookup["Frigate1"];
                vehicle=NetworkSceneSingleton<Spawner>.i.SpawnShip(definition.unitPrefab,new GlobalPosition(1000,0,1000),Quaternion.identity,null,null,1f,false);
            }
            await Wait(2);var gun=Rotary(vehicle);uint id=vehicle.persistentID.Id;
            if(gun.sources==null||gun.sources.Length<2||gun.sources[1]==null||gun.sources[1].clip==null)throw new Exception("Rotary gun has no native loop source");
            log("GUN BURST native loop clip="+gun.sources[1].clip.name+" samples="+gun.sources[1].clip.samples+" looping="+gun.sources[1].loop+" fireInterval="+gun.fireInterval.ToString("F3"));
            bool tapping=Environment.GetEnvironmentVariable("REPLAY_GUN_AUDIO_TAP")=="1";
            if(tapping)
            {
                float until=Time.realtimeSinceStartup+10;while(!OwnAudioSession.Mute()){if(Time.realtimeSinceStartup>until)throw new Exception("Isolated process audio session not found; listener stayed muted");await UniTask.NextFrame();}
                AudioListener.volume=1;gun.sources[1].spatialBlend=0;log("GUN AUDIO Windows process-only mute verified; test source set to 2D");
            }
            var liveListener=tapping?UnityEngine.Object.FindObjectOfType<AudioListener>():null;
            if(tapping&&liveListener==null)throw new Exception("No native audio listener");
            var liveTap=tapping?liveListener.gameObject.AddComponent<AudioTap>():null;
            var liveSourceTap=tapping?gun.sources[1].gameObject.AddComponent<AudioTap>():null;
            if(tapping)log("GUN AUDIO native source object audioSources="+gun.sources[1].GetComponents<AudioSource>().Length);

            // Use a burst close to the rotary gun's native cadence. Deliberately
            // spaced shots longer than its release grace are separate bursts.
            for(int n=0;n<120;n++)
            {
                // SpawnBullet invokes native ShotSound itself. Give it the
                // same recent-fire timestamp and audible detail as a nearby
                // live firing unit before the call, not a duplicate onset.
                vehicle.displayDetail=1;gun.lastFired=Time.timeSinceLevelLoad;gun.SpawnBullet(0);
                await Wait(brawler?Mathf.Max(.015f,gun.fireInterval):.015f);
            }
            await Wait(.5f);if(liveTap!=null)log("GUN AUDIO live tap samples="+liveTap.Save("gun-live-v14.wav"));
            if(liveSourceTap!=null){int sourceSamples=liveSourceTap.Save("gun-live-source.wav");log("GUN AUDIO live source tap samples="+sourceSamples);if(sourceSamples==0)throw new Exception("Native live gun source was not observed; comparison would be invalid");}await ReplayManager.i.SwitchState(ModStates.Idle);
            string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
            if(!File.Exists(file))throw new Exception("Burst recording not published");
            ReplayIntegrityCheck.Check(file);int count=0;double first=-1,last=0;
            using(var reader=new BinaryReader(File.OpenRead(file)))
            {
                var header=ReplayRecordingHeader.Read(reader);
                for(long i=0;i<header.EventCount;i++)
                {
                    var e=ReplayRecordTransport.ReadEvent(reader,header.Format);
                    if(e is GunBulletEvent shot&&shot.unitId==id&&shot.sound){count++;if(first<0)first=shot.Time;last=shot.Time;}
                    ReplayEventFactory.Return(e);
                }
            }
            if(count<100||last-first<1.5)throw new Exception("Continuous burst was not recorded: "+count);
            await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;
            if(!ReplayRuntime.IsPlayback||player==null)throw new Exception("Burst replay did not open");
            player.Pause();await player.TimelineJump(Math.Max(0,first-.1),0,false);
            var actor=player.FindActor(id);var playbackGun=Rotary(actor);var source=playbackGun.sources[1];
            if(tapping)source.spatialBlend=0;
            var replayListener=tapping?UnityEngine.Object.FindObjectOfType<AudioListener>():null;
            if(tapping&&replayListener==null)throw new Exception("No replay audio listener");
            var replayTap=tapping?replayListener.gameObject.AddComponent<AudioTap>():null;
            var replaySourceTap=tapping?source.gameObject.AddComponent<AudioTap>():null;
            if(tapping)log("GUN AUDIO replay source object audioSources="+source.GetComponents<AudioSource>().Length);
            float speed=float.TryParse(Environment.GetEnvironmentVariable("REPLAY_BURST_SPEED"),out float requested)?Mathf.Clamp(requested,.1f,1f):1f;
            ReplayDirector.i.SetPlaybackSpeed(speed);player.Resume();
            bool pausedOnce=false;int starts=0,resets=0,seen=0,lastSample=-1,nonzeroOutput=0;float outputPeak=0;var outputData=new float[1024];double moved=0;float deadline=Time.realtimeSinceStartup+Mathf.Min(180,(float)((last-first+1)/speed)+15);
            while(player.currentVirtualTime<last+.35)
            {
                if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Burst playback timed out at "+player.currentVirtualTime.ToString("F2")+" of "+(last+.35).ToString("F2"));
                actor.displayDetail=1;
                bool playing=source.isPlaying;
                if(playing)
                {
                    if(tapping&&seen==0)log("GUN AUDIO replay source volume="+source.volume+" mute="+source.mute+" spatial="+source.spatialBlend+" clip="+(source.clip==null?"none":source.clip.name)+" listener="+AudioListener.volume);
                    if(tapping)
                    {
                        source.GetOutputData(outputData,0);float peak=outputData.Max(x=>Mathf.Abs(x));outputPeak=Mathf.Max(outputPeak,peak);if(peak>.00001f)nonzeroOutput++;
                    }
                    int sample=source.timeSamples;if(lastSample<0)starts++;
                    else if(sample>=lastSample)moved+=(sample-lastSample)/(double)source.clip.frequency;
                    else if(source.loop&&lastSample>source.clip.samples*.8&&sample<source.clip.samples*.2)moved+=(source.clip.samples-lastSample+sample)/(double)source.clip.frequency;
                    else resets++;
                    lastSample=sample;seen++;
                }
                else lastSample=-1;
                if(!pausedOnce&&player.currentVirtualTime>first+.5&&playing)
                {
                    pausedOnce=true;player.Pause();await Wait(.3f);int held=source.timeSamples;await Wait(.3f);
                    if(source.isPlaying||Math.Abs(source.timeSamples-held)>2)throw new Exception("Paused burst loop kept advancing");
                    player.Resume();lastSample=-1;
                }
                await UniTask.NextFrame();
            }
            if(replayTap!=null)log("GUN AUDIO replay tap samples="+replayTap.Save("gun-replay-v14.wav")+" nonzero source buffers="+nonzeroOutput+" peak="+outputPeak.ToString("F5"));
            if(replaySourceTap!=null)log("GUN AUDIO replay source tap samples="+replaySourceTap.Save("gun-replay-source.wav"));
            if(!pausedOnce||seen<10||moved<.3||resets>0||starts>3)throw new Exception("Burst loop unstable: starts="+starts+" resets="+resets+" samples="+seen+" moved="+moved.ToString("F2"));
            log("GUN BURST PASS vehicle="+(brawler?"Brawler":"Frigate")+" speed="+speed.ToString("F1")+"x; "+count+" recorded shots; muted replay loop advanced "+moved.ToString("F2")+"s with "+resets+" unexpected resets; pause/resume verified");
        }
        finally{AudioListener.volume=oldVolume;}
    }
}
