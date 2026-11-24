using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class BrawlerEngineProbe
{
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        float oldVolume=AudioListener.volume;AudioListener.volume=0;
        try
        {
            string name="brawler-engine-"+Guid.NewGuid().ToString("N")+".replay";
            string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",name);
            bool ramp=Environment.GetEnvironmentVariable("REPLAY_BRAWLER_RAMP")=="1";
            const uint id=779;const int count=121;
            using(var stream=new FileStream(file,FileMode.CreateNew,FileAccess.ReadWrite))
            using(var output=new BinaryWriter(stream))
            {
                var header=new ReplayRecordingHeader{MapPath=key.Path,MapType=(byte)key.Type,Resources=new[]{"CAS1"}};header.Write(output);
                output.Write(ReplayRecordTransport.EncodeEvent(new SpawnEvent{Time=0,unitId=id,ownerId=id,jsonKey="CAS1",factionName="no faction",pos=new GlobalPosition(1000,3000,1000),rotation=Quaternion.identity,isAircraft=true,weapons=new List<WeaponMount>()}));
                for(int n=0;n<count;n++)
                {
                    double t=n*.05;
                    float recordedRpm=ramp?2000+Mathf.Clamp(n-20,0,60)*10:2000;
                    output.Write(ReplayRecordTransport.EncodeEvent(new AircraftStatusEvent{Time=t,unitId=id,ignition=true,rpm=new[]{recordedRpm,recordedRpm}}));
                    output.Write(ReplayRecordTransport.EncodeEvent(new UpdatePositionEvent{Time=t,unitId=id,position=new GlobalPosition(1000,3000,1000),rotation=Quaternion.identity,velocity=Vector3.zero}));
                }
                header.FinalizeHeader(output,6,1+count*2);
            }
            await ReplayManager.i.StartPlaying(name);var player=ReplayManager.i.Playback;
            if(!ReplayRuntime.IsPlayback||player==null)throw new Exception("Synthetic Brawler replay did not open");
            player.Pause();await player.TimelineJump(.5,0,false);
            var aircraft=player.FindActor(id) as Aircraft;
            var engines=aircraft?.engines.OfType<PropFan>().ToArray();
            if(engines==null||engines.Length!=2||engines.Any(e=>e.source==null||e.source.clip==null))throw new Exception("Brawler native engine audio unavailable");
            ReplayDirector.i.SetPlaybackSpeed(1);player.Resume();
            int samples=0;float maxPitchDelta=0,previousPitch=-1,minRpm=float.MaxValue,maxRpm=float.MinValue;float deadline=Time.realtimeSinceStartup+12;
            while(player.currentVirtualTime<5)
            {
                if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Brawler replay did not advance");
                if(player.currentVirtualTime>1)
                {
                    float pitch=engines[0].source.pitch;float rpm=engines[0].GetRPM();
                    if(previousPitch>=0)maxPitchDelta=Mathf.Max(maxPitchDelta,Mathf.Abs(pitch-previousPitch));
                    previousPitch=pitch;minRpm=Mathf.Min(minRpm,rpm);maxRpm=Mathf.Max(maxRpm,rpm);samples++;
                }
                await UniTask.NextFrame();
            }
            log("BRAWLER ENGINE observed samples="+samples+" pitchStep="+maxPitchDelta.ToString("F4")+" rpmRange="+minRpm.ToString("F1")+".."+maxRpm.ToString("F1")+" sources="+string.Join(",",engines.Select(e=>e.source.isPlaying.ToString())));
            float span=maxRpm-minRpm;
            if(samples<30||maxPitchDelta>.05f||(ramp?(span<400||span>700):span>100)||engines.Any(e=>!e.source.isPlaying))throw new Exception("Brawler engine audio state is unstable");
            log("BRAWLER ENGINE PASS "+(ramp?"ramping":"steady")+" recorded RPM produces stable muted native engine audio state");
        }
        finally{AudioListener.volume=oldVolume;}
    }
}
