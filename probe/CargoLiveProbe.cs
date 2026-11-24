using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class CargoLiveProbe
{
    static async UniTask Until(Func<bool> condition,float seconds,string message)
    {
        float end=Time.realtimeSinceStartup+seconds;
        while(!condition()){if(Time.realtimeSinceStartup>end)throw new Exception(message);await UniTask.NextFrame();}
    }
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    static MountedCargo[] Mounts(Aircraft craft)=>craft==null||craft.partLookup==null?Array.Empty<MountedCargo>():craft.partLookup.Where(p=>p!=null).SelectMany(p=>p.GetComponentsInChildren<MountedCargo>(true)).Where(m=>m!=null&&m.attachedUnit==craft).Distinct().ToArray();
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);
        await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Cargo recording test");
        await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Live scene unavailable");
        TimeScaleManager.Scale=1;
        await ReplayManager.i.SwitchState(ModStates.Record);
        await Until(()=>Recorder.Active!=null&&Recorder.Active.IsRecording,15,"Recorder unavailable");
        int liveWrapped=ReplayDeadLatchProbe.Wrapped,productionLiveWrapped=ProductionContinuationProbe.LiveWrapped;
        string partial=Recorder.Active.RecordingPath;
        log("CARGO LIVE recording started");
        var def=(AircraftDefinition)Encyclopedia.Lookup.First(p=>p.Value is AircraftDefinition&&p.Value.unitName.IndexOf("Tarantula",StringComparison.OrdinalIgnoreCase)>=0).Value;
        var sets=def.unitPrefab.GetComponent<Aircraft>().weaponManager.hardpointSets;
        string requested=Environment.GetEnvironmentVariable("REPLAY_CARGO_KEY");
        bool Matches(WeaponMount w)=>w!=null&&w.prefab!=null&&w.prefab.GetComponentInChildren<MountedCargo>(true)!=null&&(string.IsNullOrEmpty(requested)||w.jsonKey==requested);
        int slot=Array.FindIndex(sets,s=>s.weaponOptions.Any(Matches));
        if(slot<0)throw new Exception("Cargo option unavailable: "+requested);
        var loadout=new NuclearOption.SavedMission.Loadout();for(int i=0;i<sets.Length;i++)loadout.weapons.Add(null);
        loadout.weapons[slot]=sets[slot].weaponOptions.First(Matches);log("CARGO LIVE selected "+loadout.weapons[slot].jsonKey);
        var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
        var craft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,loadout,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
        log("CARGO LIVE spawned craft null="+(craft==null)+" server="+(craft!=null&&craft.IsServer)+" mounts="+Mounts(craft).Length);
        await Wait(2);log("CARGO LIVE after init craft null="+(craft==null)+" mounts="+Mounts(craft).Length+" hq="+(hq==null?"null":hq.name));
        await Until(()=>Mounts(craft).Length>0,20,"Cargo mount did not initialize");
        await Wait(2);
        var mounts=Mounts(craft);var cargo=mounts.First();
        bool releaseAll=Environment.GetEnvironmentVariable("REPLAY_CARGO_ALL")=="1";
        int expectedDrops=releaseAll?mounts.Length:1;
        string cargoKey=cargo.cargo.jsonKey;
        var before=new HashSet<Unit>(UnityEngine.Object.FindObjectsOfType<Unit>());
        log("CARGO LIVE releasing "+expectedDrops+" "+cargoKey+" via native MountedCargo.Fire");
        foreach(var current in mounts.Take(expectedDrops))
        {
            var station=craft.weaponStations.First(s=>s!=null&&s.Weapons!=null&&s.Weapons.Contains(current));
            current.Fire(craft,null,craft.rb.velocity,station,craft.GlobalPosition());
            if(releaseAll)await Wait(.35f);
        }
        await Until(()=>UnityEngine.Object.FindObjectsOfType<Unit>().Count(v=>v!=null&&!before.Contains(v)&&v.definition?.jsonKey==cargoKey)>=expectedDrops,45,"Native deployed units did not spawn");
        log("CARGO LIVE separate native vehicle spawned");
        bool testRemoval=Environment.GetEnvironmentVariable("REPLAY_CARGO_DESTROY_CHUTE")=="1";
        await Wait(testRemoval?5:8);
        var liveVehicle=UnityEngine.Object.FindObjectsOfType<Unit>().First(v=>v!=null&&!before.Contains(v)&&v.definition?.jsonKey==cargoKey);
        bool nativeChutes=liveVehicle.GetComponentsInChildren<CargoDeploymentSystem>(true).Any(s=>s!=null&&s.attachedUnit==liveVehicle&&s.listParachutes!=null&&s.listParachutes.Count>0);
        log("CARGO LIVE native unit="+liveVehicle.GetType().Name+" parachuteRig="+nativeChutes);
        if(testRemoval)
        {
            var liveSystem=liveVehicle.GetComponentsInChildren<CargoDeploymentSystem>(true).First(s=>s!=null&&s.attachedUnit==liveVehicle);
            if(liveSystem.listParachutes.Count<2||liveSystem.listParachutes[0]==null)throw new Exception("Removal fixture needs multiple parachutes");
            liveSystem.enabled=false;foreach(var chute in liveSystem.listParachutes)if(chute!=null)chute.enabled=false;
            UnityEngine.Object.Destroy(liveSystem.listParachutes[0].gameObject);
            await Wait(1);
            if(Recorder.Active.CaptureFailureCount!=0)throw new Exception("Parachute removal caused recording capture faults");
        }
        await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file))throw new Exception("Final recording missing: "+ReplayManager.i.LastRecordingStopStatus);
        log("CARGO LIVE "+ReplayIntegrityCheck.Check(file));
        int loaded=0,released=0,spawned=0,chutes=0,active=0;double beforeDrop=-1,afterDrop=-1,openAt=-1,removedAt=-1;uint carrierId=0,vehicleId=0;
        var mountSamples=new List<(double time,uint unitId,CargoMountPose[] poses)>();var chuteSamples=new List<(double time,uint unitId,CargoParachutePose[] poses)>();var positions=new List<(double time,uint unitId,GlobalPosition position,Quaternion rotation)>();var births=new List<(double time,uint id)>();var openByUnit=new Dictionary<uint,double>();
        using(var input=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(input);
            for(long i=0;i<header.EventCount;i++)
            {
                var e=ReplayRecordTransport.ReadEvent(input,header.Format);
                if(e is CargoMountStateEvent mount){mountSamples.Add((mount.Time,mount.unitId,(CargoMountPose[])mount.mounts.Clone()));if(mount.mounts.Any(m=>m.active))loaded++;if(mount.mounts.Length>0&&mount.mounts.All(m=>m.active&&!m.fired)){beforeDrop=mount.Time;carrierId=mount.unitId;}if(mount.mounts.Any(m=>!m.active)){released++;if(afterDrop<0)afterDrop=mount.Time;}}
                if(e is SpawnEvent spawn&&spawn.jsonKey==cargoKey){spawned++;vehicleId=spawn.unitId;births.Add((spawn.Time,spawn.unitId));}
                if(e is UpdatePositionEvent move)positions.Add((move.Time,move.unitId,move.position,move.rotation));
                if(e is CargoParachuteStateEvent chute&&chute.present){chutes++;chuteSamples.Add((chute.Time,chute.unitId,(CargoParachutePose[])chute.parachutes.Clone()));if(chute.parachutes.Any(p=>p.canopyActive)){active++;if(!openByUnit.ContainsKey(chute.unitId))openByUnit[chute.unitId]=chute.Time;}if(testRemoval&&openByUnit.ContainsKey(chute.unitId)&&chute.parachutes.Length>1&&!chute.parachutes[0].canopyActive&&!chute.parachutes[0].linesActive&&chute.parachutes.Skip(1).Any(p=>p.canopyActive))removedAt=chute.Time;}
                ReplayMod.Events.ReplayEventFactory.Return(e);
            }
        }
        var opened=births.Where(b=>openByUnit.ContainsKey(b.id)).OrderBy(b=>openByUnit[b.id]).FirstOrDefault();
        if(opened.id!=0){vehicleId=opened.id;openAt=openByUnit[vehicleId];}
        log("CARGO LIVE recorded loaded="+loaded+" released="+released+" spawned="+spawned+" chute="+chutes+" open="+active);
        if(loaded==0||released==0||spawned<expectedDrops||beforeDrop<0||afterDrop<0||(nativeChutes&&openAt<0))throw new Exception("Native cargo release was not fully recorded");
        if(ReplayDeadLatchProbe.Enabled&&ReplayDeadLatchProbe.Wrapped!=liveWrapped)throw new Exception("Live cargo recording unexpectedly wrapped replay continuations");
        if(ReplayDeadLatchProbe.Enabled)log("CONTINUATION LIVE CARGO PASS no live callbacks wrapped");
        if(ProductionContinuationProbe.Enabled&&ProductionContinuationProbe.LiveWrapped!=productionLiveWrapped)throw new Exception("Production cleanup wrapped live cargo callbacks");
        if(ProductionContinuationProbe.Enabled)log("PRODUCTION CONTINUATION LIVE CARGO "+ProductionContinuationProbe.Stats);
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));
        if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("Recorded cargo replay failed to open");
        var player=ReplayManager.i.Playback;player.Pause();
        var bindingType=typeof(UnitController).Assembly.GetType("ReplayMod.Core.ReplayCargoMounts");
        var ctor=bindingType.GetConstructor(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(Aircraft)},null);
        var capture=bindingType.GetMethod("Capture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        float maxMountDistance=0,maxMountAngle=0,maxChuteDistance=0,maxChuteAngle=0,maxChuteScale=0;int mountPoseChecks=0,chutePoseChecks=0;
        void CheckMounts(Aircraft carrier,double at)
        {
            var expected=mountSamples.Where(x=>x.unitId==carrierId&&x.time<=at+.00001).Last().poses;
            var actual=(CargoMountPose[])capture.Invoke(ctor.Invoke(new object[]{carrier}),null);
            if(actual.Length!=expected.Length)throw new Exception("Mount count differs at "+at);
            for(int j=0;j<expected.Length;j++)
            {
                if(actual[j].key!=expected[j].key||actual[j].active!=expected[j].active||actual[j].fired!=expected[j].fired||actual[j].ammo!=expected[j].ammo)
                    throw new Exception("Cargo mount state differs for "+expected[j].key+" at "+at+" expected active="+expected[j].active+" actual="+actual[j].active);
                float distance=Vector3.Distance(actual[j].position,expected[j].position),angle=Quaternion.Angle(actual[j].rotation,expected[j].rotation);
                maxMountDistance=Math.Max(maxMountDistance,distance);maxMountAngle=Math.Max(maxMountAngle,angle);mountPoseChecks++;
                if(distance>.02f||angle>.2f)throw new Exception("Cargo mount pose differs for "+expected[j].key+" at "+at+": "+distance+"m / "+angle+"deg");
            }
        }
        void CheckChutes(Unit vehicle,double at,CargoParachutePose[] expected,CargoParachutePose[] future,float fraction)
        {
            var system=vehicle.GetComponentsInChildren<CargoDeploymentSystem>(true).First(s=>s!=null&&s.attachedUnit==vehicle);
            if(system.listParachutes.Count!=expected.Length)throw new Exception("Parachute count differs at "+at);
            for(int j=0;j<expected.Length;j++)
            {
                var chute=system.listParachutes[j];var pose=expected[j];var next=future[j];
                if(chute==null||chute.canopy==null||chute.lines==null||chute.canopy.activeSelf!=pose.canopyActive||chute.lines.activeSelf!=pose.linesActive)throw new Exception("Parachute visibility differs for "+pose.key);
                void CheckPose(Transform item,GlobalPosition position,GlobalPosition nextPosition,Quaternion rotation,Quaternion nextRotation,Vector3 scale,Vector3 nextScale,string part)
                {
                    float distance=Vector3.Distance(item.position,Vector3.Lerp(position.ToLocalPosition(),nextPosition.ToLocalPosition(),fraction)),angle=Quaternion.Angle(item.rotation,Quaternion.Slerp(rotation,nextRotation,fraction)),size=Vector3.Distance(item.localScale,Vector3.Lerp(scale,nextScale,fraction));
                    maxChuteDistance=Math.Max(maxChuteDistance,distance);maxChuteAngle=Math.Max(maxChuteAngle,angle);maxChuteScale=Math.Max(maxChuteScale,size);chutePoseChecks++;
                    if(distance>.15f||angle>2f||size>.02f)throw new Exception("Parachute "+part+" pose differs for "+pose.key+" at "+at+": "+distance+"m / "+angle+"deg / "+size+" scale");
                }
                if(pose.canopyActive)CheckPose(chute.canopy.transform,pose.canopyPosition,next.canopyPosition,pose.canopyRotation,next.canopyRotation,pose.canopyScale,next.canopyScale,"canopy");
                if(pose.linesActive)CheckPose(chute.lines.transform,pose.linesPosition,next.linesPosition,pose.linesRotation,next.linesRotation,pose.linesScale,next.linesScale,"lines");
            }
        }
        for(int i=0;i<3;i++)
        {
            await player.TimelineJump(beforeDrop,0,false);await UniTask.NextFrame();
            var carrier=player.FindActor(carrierId) as Aircraft;
            if(carrier==null||!Mounts(carrier).Any(m=>m.gameObject.activeSelf&&!m.fired))throw new Exception("Recorded loaded cargo missing on rewind "+i);CheckMounts(carrier,beforeDrop);
            await player.TimelineJump(Math.Max(afterDrop,openAt)+.05,0,false);await UniTask.NextFrame();
            carrier=player.FindActor(carrierId) as Aircraft;
            if(carrier==null||!Mounts(carrier).Any(m=>!m.gameObject.activeSelf))throw new Exception("Recorded cargo duplicate after release "+i);CheckMounts(carrier,Math.Max(afterDrop,openAt)+.05);
            var vehicle=player.FindActor(vehicleId);if(vehicle==null)throw new Exception("Recorded deployed vehicle missing "+i);
            var type=typeof(UnitController).Assembly.GetType("ReplayMod.Core.ReplayCargoParachutes");
            var find=type.GetMethod("Find",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            var system=(CargoDeploymentSystem)find.Invoke(null,new object[]{vehicle});
            if(nativeChutes&&(system==null||!system.listParachutes.Any(p=>p!=null&&p.canopy.activeSelf&&!p.enabled)))throw new Exception("Recorded open canopy missing "+i);
            if(nativeChutes)
            {
                await player.TimelineJump(openAt,0,false);await UniTask.NextFrame();
                vehicle=player.FindActor(vehicleId);if(vehicle==null)throw new Exception("Deployed vehicle missing at exact canopy sample");
                var exact=chuteSamples.First(x=>x.unitId==vehicleId&&Math.Abs(x.time-openAt)<.000001).poses;CheckChutes(vehicle,openAt,exact,exact,0);
            }
        }
        if(nativeChutes)
        {
            var samples=chuteSamples.Where(x=>x.unitId==vehicleId&&x.time>=openAt).OrderBy(x=>x.time).ToArray();
            int found=-1;
            for(int k=0;k+1<samples.Length;k++)
            {
                var a=samples[k];var b=samples[k+1];
                if(b.time<=a.time||b.time-a.time>.2||a.poses.Length!=b.poses.Length||!a.poses.Any(p=>p.canopyActive))continue;
                if(a.poses.Where((p,j)=>p.key!=b.poses[j].key||p.canopyActive!=b.poses[j].canopyActive||p.linesActive!=b.poses[j].linesActive).Any())continue;
                if(!a.poses.Where((p,j)=>p.canopyActive&&Vector3.Distance(p.canopyPosition.ToLocalPosition(),b.poses[j].canopyPosition.ToLocalPosition())>.1f).Any())continue;
                found=k;break;
            }
            if(found<0)throw new Exception("No moving compatible parachute samples to check interpolation");
            var first=samples[found];var next=samples[found+1];double at=(first.time+next.time)*.5;
            await player.TimelineJump(at,0,false);await UniTask.NextFrame();var vehicle=player.FindActor(vehicleId);
            if(vehicle==null)throw new Exception("Deployed vehicle missing at interpolated sample");
            CheckChutes(vehicle,at,first.poses,next.poses,.5f);log("CARGO PARACHUTE INTERPOLATION PASS midpoint between recorded moving poses at "+at.ToString("F3"));
            if(Environment.GetEnvironmentVariable("REPLAY_CARGO_MOVING_CHECK")=="1")
            {
                double begin=at,lastChecked=-1;int checks=0;float deadline=Time.realtimeSinceStartup+8;
                ReplayMod.Director.ReplayDirector.i.SetPlaybackSpeed(1);player.Resume();
                while(player.currentVirtualTime<begin+.5&&checks<20)
                {
                    if(Time.realtimeSinceStartup>deadline)throw new Exception("Cargo moving playback stalled");
                    await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);double t=player.currentVirtualTime;
                    if(t<=lastChecked+.00001)continue;
                    int left=Array.FindLastIndex(samples,x=>x.time<=t);
                    if(left<0||left+1>=samples.Length)continue;var a=samples[left];var b=samples[left+1];
                    if(b.time<=a.time||b.time-a.time>.2||a.poses.Length!=b.poses.Length||!a.poses.Any(p=>p.canopyActive))continue;
                    if(a.poses.Where((p,j)=>p.key!=b.poses[j].key||p.canopyActive!=b.poses[j].canopyActive||p.linesActive!=b.poses[j].linesActive).Any())continue;
                    vehicle=player.FindActor(vehicleId);if(vehicle==null)throw new Exception("Deployed vehicle missing during playback");
                    CheckChutes(vehicle,t,a.poses,b.poses,(float)((t-a.time)/(b.time-a.time)));lastChecked=t;checks++;
                }
                player.Pause();if(checks<5)throw new Exception("Not enough moving parachute playback samples: "+checks);
                log("CARGO PARACHUTE MOVING PASS "+checks+" rendered-frame checks against recorded interpolated poses");
            }
        }
        if(Environment.GetEnvironmentVariable("REPLAY_CARGO_MOVING_CHECK")=="1")
        {
            var all=positions.Where(x=>x.unitId==vehicleId&&x.time>=openAt).OrderBy(x=>x.time).ToArray();
            var selected=new List<(double time,uint unitId,GlobalPosition position,Quaternion rotation)>();
            foreach(var item in all)if(selected.Count==0||item.time-selected[selected.Count-1].time>=.5){selected.Add(item);if(selected.Count==4)break;}
            if(selected.Count<3||Vector3.Distance(selected[0].position.ToLocalPosition(),selected[selected.Count-1].position.ToLocalPosition())<1)throw new Exception("Deployed vehicle fixture did not move far enough to verify its flight path");
            float maxDistance=0,maxAngle=0;
            foreach(var item in selected)
            {
                await player.TimelineJump(item.time,0,false);await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
                var actor=player.FindActor(vehicleId);if(actor==null)throw new Exception("Deployed vehicle missing at recorded movement sample");
                float distance=Vector3.Distance(actor.transform.position,item.position.ToLocalPosition()),angle=Quaternion.Angle(actor.transform.rotation,item.rotation);
                maxDistance=Math.Max(maxDistance,distance);maxAngle=Math.Max(maxAngle,angle);
                if(distance>.15f||angle>1f)throw new Exception("Deployed vehicle recorded pose differs by "+distance+"m / "+angle+"deg at "+item.time);
            }
            log("CARGO VEHICLE MOTION PASS "+selected.Count+" recorded poses over "+(selected[selected.Count-1].time-selected[0].time).ToString("F2")+"s; maxDistance="+maxDistance.ToString("F3")+"m maxAngle="+maxAngle.ToString("F3")+"deg");
        }
        if(releaseAll)
        {
            var final=mountSamples.Where(x=>x.unitId==carrierId&&x.poses.Length==expectedDrops&&x.poses.All(p=>!p.active)).LastOrDefault();
            if(final.poses==null)throw new Exception("Not all cargo mounts became inactive");
            double settled=Math.Max(final.time,births.Max(b=>b.time))+.05;
            await player.TimelineJump(settled,0,false);await UniTask.NextFrame();
            var carrier=player.FindActor(carrierId) as Aircraft;if(carrier==null)throw new Exception("Carrier missing after multiple drops");CheckMounts(carrier,settled);
            if(births.Select(x=>x.id).Distinct().Count()!=expectedDrops||births.Any(x=>player.FindActor(x.id)==null))throw new Exception("One or more separately deployed units are missing after multiple drops");
            log("CARGO MULTI PASS "+expectedDrops+" separate native releases, all mounted copies hidden and all deployed actors restored");
        }
        if(testRemoval)
        {
            if(removedAt<0)throw new Exception("Disappeared parachute was not recorded while the others stayed visible");
            await player.TimelineJump(removedAt,0,false);await UniTask.NextFrame();
            var vehicle=player.FindActor(vehicleId);
            var system=vehicle.GetComponentsInChildren<CargoDeploymentSystem>(true).First(s=>s.attachedUnit==vehicle);
            if(system.listParachutes[0].canopy.activeSelf||system.listParachutes[0].lines.activeSelf||!system.listParachutes.Skip(1).Any(p=>p!=null&&p.canopy.activeSelf))throw new Exception("Replayed parachute removal was incorrect");
            log("CARGO REMOVAL PASS individual parachute disappearance recorded without capture fault and restored on seek");
        }
        log("CARGO POSES mountChecks="+mountPoseChecks+" maxDistance="+maxMountDistance.ToString("F3")+"m maxAngle="+maxMountAngle.ToString("F3")+"deg chuteChecks="+chutePoseChecks+" maxDistance="+maxChuteDistance.ToString("F3")+"m maxAngle="+maxChuteAngle.ToString("F3")+"deg maxScale="+maxChuteScale.ToString("F4"));
        log("CARGO LIVE PASS "+cargoKey+" native drop recorded, reopened and repeatedly sought before/after deployment; nativeParachutes="+nativeChutes+" samples="+chutes);
    }
}
