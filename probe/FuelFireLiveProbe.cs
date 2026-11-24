using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class FuelFireLiveProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Native fuel fire test");
        TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);var recorder=Recorder.Active;
        if(recorder==null||!recorder.IsRecording)throw new Exception("Fire recorder unavailable");string partial=recorder.RecordingPath;
        var def=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
        var craft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
        await Wait(2);var tank=craft.partLookup.Where(p=>p!=null).SelectMany(p=>p.GetComponentsInChildren<FuelTank>(true)).FirstOrDefault(t=>t!=null&&t.part!=null&&t.part.parentUnit==craft&&t.fireEffect!=null&&t.part.attachInfo!=null&&!t.part.IsDetached());
        if(tank==null)throw new Exception("No suitable Brawler fuel tank");uint unitId=craft.persistentID.Id;byte partId=tank.part.id;
        log("FUEL FIRE native tank="+tank.name+" part="+tank.part.name+"#"+partId);
        tank.UpdateStatus(false,true);await Wait(2);if(tank.fireEffectSpawn==null)throw new Exception("Native ignition did not produce fire");
        var part=tank.part;part.Detach(craft.rb!=null?craft.rb.velocity:Vector3.zero,Vector3.zero);await Wait(3);
        int faults=recorder.CaptureFailureCount;await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file))throw new Exception("Fire recording missing");ReplayIntegrityCheck.Check(file);
        uint fireId=0;double begin=-1,detach=-1,last=-1;int samples=0;
        using(var input=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(input);for(long i=0;i<header.EventCount;i++)
            {
                var e=ReplayRecordTransport.ReadEvent(input,header.Format);
                if(e is FuelFlameStateEvent fire&&fire.unitId==unitId&&fire.partId==partId&&fire.phase==FuelFlamePhase.Begin&&fireId==0){fireId=fire.flameId;begin=fire.Time;}
                else if(e is FuelFlameStateEvent sample&&sample.flameId==fireId&&sample.phase==FuelFlamePhase.Sample){samples++;last=sample.Time;}
                if(e is DetachPartEvent detached&&detached.unitID==unitId&&detached.partID==partId&&detach<0)detach=detached.Time;
                ReplayEventFactory.Return(e);
            }
        }
        log("FUEL FIRE recorded fire="+fireId+" begin="+begin.ToString("F2")+" detach="+detach.ToString("F2")+" last="+last.ToString("F2")+" samples="+samples+" faults="+faults);
        if(fireId==0||detach<=begin||last<detach+.3||samples<5||faults!=0)throw new Exception("Native burning detached part was not fully recorded");
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||player==null)throw new Exception("Fire replay did not open");player.Pause();
        var controller=(UnitController)typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
        var find=typeof(UnitController).GetMethod("FindRecordedAircraftPart",BindingFlags.NonPublic|BindingFlags.Instance);
        foreach(double at in new[]{Math.Max(0,begin-.2),begin+.5,detach+.5,begin+.5,detach+.5})
        {
            await player.TimelineJump(at,0,false);await UniTask.NextFrame();var root=GameObject.Find("Replay fuel fire "+fireId);
            if(at<begin){if(root!=null)throw new Exception("Fire exists before ignition");continue;}
            controller=(UnitController)typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
            var actor=player.FindActor(unitId);var replayPart=actor==null?null:(UnitPart)find.Invoke(controller,new object[]{actor,partId});
            var played=(System.Collections.IDictionary)typeof(UnitController).GetField("_fuelFlames",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
            var entry=played.Contains(fireId)?played[fireId]:null;var entryType=entry?.GetType();
            var available=entryType?.GetField("unavailable",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(entry);
            var visual=entryType?.GetField("visual",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(entry);
            log("FUEL FIRE diagnostic time="+at.ToString("F2")+" actor="+(actor!=null)+" part="+(replayPart!=null)+" root="+(root!=null)+" entries="+played.Count+" tracked="+(entry!=null)+" unavailable="+available+" visual="+(visual!=null));
            if(root==null||replayPart==null)throw new Exception("Fire or its part missing at "+at);
            var damage=root.GetComponentInChildren<DamageParticles>(true);int particles=root.GetComponentsInChildren<ParticleSystem>(true).Sum(p=>p.particleCount);
            float distance=Vector3.Distance(root.transform.position,replayPart.transform.position);
            log("FUEL FIRE seek="+at.ToString("F2")+" detached="+replayPart.IsDetached()+" fireDistance="+distance.ToString("F2")+" particles="+particles+" nativeScriptEnabled="+(damage!=null&&damage.enabled));
            if(damage==null||damage.enabled||distance>20||particles==0||(at>detach&&!replayPart.IsDetached()))throw new Exception("Fire presentation failed at "+at);
        }
        foreach(float speed in new[]{1f,.1f})
        {
            double start=detach+.2,end=Math.Min(last-.1,start+(speed<1?.5:1.5));
            await player.TimelineJump(start,0,false);controller=(UnitController)typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
            ReplayMod.Director.ReplayDirector.i.SetPlaybackSpeed(speed);player.Resume();float timeout=Time.realtimeSinceStartup+20,worst=0;int frames=0,visible=0;
            while(player.currentVirtualTime<end)
            {
                if(Time.realtimeSinceStartup>timeout)throw new Exception("Fire playback stalled at "+speed+"x");
                await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
                var actor=player.FindActor(unitId);var trackedPart=actor==null?null:(UnitPart)find.Invoke(controller,new object[]{actor,partId});var effect=GameObject.Find("Replay fuel fire "+fireId);
                if(effect==null||trackedPart==null||!trackedPart.IsDetached())throw new Exception("Detached burning part missing during playback");
                worst=Math.Max(worst,Vector3.Distance(effect.transform.position,trackedPart.transform.position));
                if(effect.GetComponentsInChildren<ParticleSystem>(true).Sum(p=>p.particleCount)>0)visible++;frames++;
            }
            player.Pause();log("FUEL FIRE continuous speed="+speed+" frames="+frames+" visible="+visible+" maxDistance="+worst.ToString("F3"));
            if(frames<10||visible<frames*.9||worst>2)throw new Exception("Detached fire drifted or vanished during "+speed+"x playback");
        }
        log("FUEL FIRE PASS native ignition and part detachment recorded; replay fire and particles restored through repeated seeks");
    }
}
