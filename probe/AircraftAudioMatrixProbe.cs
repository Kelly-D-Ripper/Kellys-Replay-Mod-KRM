using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

// Test-only, process-muted native recordings. Never attached to a public package.
public static class AircraftAudioMatrixProbe
{
    private static async UniTask Wait(float seconds)
    {
        float until=Time.realtimeSinceStartup+seconds;
        while(Time.realtimeSinceStartup<until)await UniTask.NextFrame();
    }
    private static AudioSource Find(Aircraft craft,string clip)
    {
        if(craft==null)return null;
        var own=craft.GetComponentsInChildren<AudioSource>(true)
            .FirstOrDefault(s=>s!=null&&s.clip!=null&&s.clip.name==clip&&s.loop);
        if(own!=null)return own;
        // Some native aircraft detach engine parts from the root transform.
        // In this isolated scene, only accept a source within 30 m of our aircraft.
        return UnityEngine.Object.FindObjectsOfType<AudioSource>()
            .Where(s=>s!=null&&s.clip!=null&&s.clip.name==clip&&s.loop&&Vector3.Distance(s.transform.position,craft.transform.position)<30)
            .OrderBy(s=>Vector3.Distance(s.transform.position,craft.transform.position)).FirstOrDefault();
    }
    private static float Rms(AudioSource source,float[] samples)
    {
        source.GetOutputData(samples,0);double sum=0;
        foreach(float v in samples)sum+=v*v;
        return (float)Math.Sqrt(sum/samples.Length);
    }
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        string[,] cases={
            {"CAS1","casplane_turbine"},
            {"CAS1","casplane_prop"},
            {"Fighter1","revoker_turbine"},
            {"QuadVTOL1","Tara_Turbine"},
            {"QuadVTOL1","Tara_PropExt"},
            {"UtilityHelo1","CoaxHeli_Turbine"},
            {"Darkreach","revoker_turbine"}
        };
        float oldVolume=AudioListener.volume;AudioListener.volume=0;
        try
        {
            int completed=0,skipped=0,inconclusive=0;
            float muteTimeout=Time.realtimeSinceStartup+10;
            while(!OwnAudioSession.Mute())
            {
                if(Time.realtimeSinceStartup>muteTimeout)throw new Exception("Isolated process audio session could not be muted");
                await UniTask.NextFrame();
            }
            AudioListener.volume=1;
            for(int i=0;i<cases.GetLength(0);i++)
            {
                string unitKey=cases[i,0],clipName=cases[i,1];
                string filter=Environment.GetEnvironmentVariable("REPLAY_AUDIO_MATRIX_FILTER");
                if(!string.IsNullOrEmpty(filter)&&unitKey!=filter)continue;
                await ReplayManager.i.SwitchState(ModStates.Idle);
                await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Aircraft engine audio matrix");
                TimeScaleManager.Scale=1;
                await ReplayManager.i.SwitchState(ModStates.Record);
                var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("Cannot start matrix recording");
                string path=recorder.RecordingPath;
                var definition=(AircraftDefinition)Encyclopedia.Lookup[unitKey];
                var faction=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
                var aircraft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,definition.unitPrefab,null,1f,default(LiveryKey),
                    new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,faction,null,1f,1f);
                if(aircraft==null)throw new Exception("Could not spawn "+unitKey);
                uint id=aircraft.persistentID.Id;
                await Wait(2f);
                log("AUDIO MATRIX LIVE ENGINES unit="+unitKey+" values="+(aircraft.engines==null?"null":string.Join(",",aircraft.engines.Where(e=>e!=null).Select(e=>e.GetType().Name+":"+e.GetRPM().ToString("F1")))));
                var live=Find(aircraft,clipName);
                if(live==null)
                {
                    log("AUDIO MATRIX SKIP "+unitKey+" "+clipName+" craft="+(aircraft!=null)+" engines="+(aircraft?.engines==null?"null":string.Join(",",aircraft.engines.Where(e=>e!=null).Select(e=>e.GetType().Name)))+" sources="+(aircraft==null?"dead":string.Join(",",aircraft.GetComponentsInChildren<AudioSource>(true).Where(s=>s.clip!=null).Select(s=>s.clip.name+":"+s.loop))));
                    skipped++;await ReplayManager.i.SwitchState(ModStates.Idle);continue;
                }
                live.spatialBlend=0;
                var buffer=new float[256];float liveMax=0;
                for(int n=0;n<120;n++){liveMax=Math.Max(liveMax,Rms(live,buffer));await UniTask.NextFrame();}
                bool livePlaying=live.isPlaying;float liveVolume=live.volume;
                await Wait(2f);
                int faults=recorder.CaptureFailureCount;
                await ReplayManager.i.SwitchState(ModStates.Idle);
                string replay=path.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?path.Substring(0,path.Length-8):path;
                if(faults!=0||!File.Exists(replay))throw new Exception("Recording failed for "+unitKey);
                ReplayIntegrityCheck.Check(replay);
                using(var input=new BinaryReader(File.OpenRead(replay)))
                {
                    var header=ReplayRecordingHeader.Read(input);int count=0,min=int.MaxValue,max=0;
                    for(long n=0;n<header.EventCount;n++)
                    {
                        var e=ReplayRecordTransport.ReadEvent(input,header.Format);
                        if(e is AircraftStatusEvent status&&status.unitId==id){int length=status.rpm?.Length??0;count++;min=Math.Min(min,length);max=Math.Max(max,length);}
                    }
                    log("AUDIO MATRIX RECORD unit="+unitKey+" statuses="+count+" rpmLength="+(count==0?"none":min+".."+max));
                }
                await ReplayManager.i.StartPlaying(Path.GetFileName(replay));
                var player=ReplayManager.i.Playback;if(player==null||!ReplayRuntime.IsPlayback)throw new Exception("Replay failed for "+unitKey);
                foreach(float speed in new[]{1f,.1f})
                {
                    player.Pause();await player.TimelineJump(2.5,0,false);
                    var restored=player.FindActor(id) as Aircraft;
                    var source=Find(restored,clipName);if(source==null)throw new Exception("Replay source missing "+unitKey+" "+clipName);
                    source.spatialBlend=0;
                    log("AUDIO MATRIX BEFORE unit="+unitKey+" clip="+clipName+" speed="+speed+" playing="+source.isPlaying+" volume="+source.volume.ToString("F3")+" pitch="+source.pitch.ToString("F3")+" samples="+source.timeSamples);
                    ReplayDirector.i.SetPlaybackSpeed(speed);player.Resume();
                    if(Environment.GetEnvironmentVariable("REPLAY_TEST_JET_WAKE")=="1")
                    {
                        var jet=source.GetComponent<Turbojet>();
                        if(jet!=null)
                        {
                            var fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
                            var jetSource=(AudioSource)typeof(Turbojet).GetField("turbineAudio",fields).GetValue(jet);
                            bool outOfCone=(bool)typeof(Turbojet).GetField("outOfSoundCone",fields).GetValue(jet);
                            float maxRpm=(float)typeof(Turbojet).GetField("maxRPM",fields).GetValue(jet);
                            float maxPitch=(float)typeof(Turbojet).GetField("turbineMaxPitch",fields).GetValue(jet);
                            var owner=(Aircraft)typeof(Turbojet).GetField("aircraft",fields).GetValue(jet);
                            log("AUDIO MATRIX JET TEST rpm="+jet.rpm.ToString("F1")+" max="+maxRpm.ToString("F1")+" cone="+outOfCone+" matched="+(jetSource==source)+" owner="+(owner==restored)+" recordedEngines="+(restored.engines==null?-1:restored.engines.Count));
                            if(jetSource==source&&!source.isPlaying&&!source.mute&&!outOfCone&&jet.rpm>0&&maxRpm>0)
                            {source.pitch=jet.rpm/maxRpm*maxPitch;source.Play();log("AUDIO MATRIX JET TEST started source after playback clock");}
                        }
                    }
                    float end=Time.realtimeSinceStartup+2f,start=Time.realtimeSinceStartup,first=-1,max=0;
                    int frames=0,playing=0,resets=0,last=-1;float maxPitchStep=0,lastPitch=-1;
                    var trace=new System.Text.StringBuilder("elapsed,playing,volume,pitch,samples,rms\n");
                    while(Time.realtimeSinceStartup<end)
                    {
                        float rms=Rms(source,buffer);max=Math.Max(max,rms);
                        if(first<0&&rms>.001f)first=Time.realtimeSinceStartup-start;
                        if(source.isPlaying)playing++;
                        int sample=source.timeSamples;
                        if(last>=0&&sample+2048<last&&last<source.clip.samples-4096)resets++;
                        if(lastPitch>=0)maxPitchStep=Math.Max(maxPitchStep,Math.Abs(source.pitch-lastPitch));
                        if(frames<80)trace.AppendLine((Time.realtimeSinceStartup-start).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+(source.isPlaying?1:0)+","+source.volume.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+source.pitch.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+sample+","+rms.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                        lastPitch=source.pitch;last=sample;frames++;await UniTask.NextFrame();
                    }
                    log("AUDIO MATRIX unit="+unitKey+" clip="+clipName+" speed="+speed+" livePlaying="+livePlaying+" liveVolume="+liveVolume.ToString("F3")+" liveRmsMax="+liveMax.ToString("F3")+" replayPlaying="+playing+"/"+frames+" replayVolume="+source.volume.ToString("F3")+" firstSignal="+first.ToString("F3")+" maxRms="+max.ToString("F3")+" resets="+resets+" pitchStep="+maxPitchStep.ToString("F3"));
                    string tag=Environment.GetEnvironmentVariable("REPLAY_AUDIO_TAG")??"v22-aircraft-audio";
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ghost-probe",tag+"-"+unitKey+"-"+clipName+"-"+speed.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+".csv"),trace.ToString());
                    if(frames<30||resets!=0)throw new Exception("Source continuity failed "+unitKey+" "+clipName);
                    if(livePlaying&&playing<frames*.9)throw new Exception("Replay source stopped "+unitKey+" "+clipName);
                    if(liveMax>.001f&&max<.001f)throw new Exception("Replay source silent despite audible live source "+unitKey+" "+clipName);
                }
                completed++;if(liveMax<=.001f)inconclusive++;
            }
            log("AUDIO MATRIX "+(skipped==0&&inconclusive==0?"PASS":"INCOMPLETE")+" completed="+completed+" skipped="+skipped+" silentLive="+inconclusive+" scoped native source continuity checks");
        }
        finally{AudioListener.volume=oldVolume;}
    }
}
