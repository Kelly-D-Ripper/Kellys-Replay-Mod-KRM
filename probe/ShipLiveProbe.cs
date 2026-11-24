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

public static class ShipLiveProbe
{
    static async UniTask Until(Func<bool> condition,float seconds,string message)
    {float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new Exception(message);await UniTask.NextFrame();}}
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    static Gun[] Guns(Unit unit)=>unit.partLookup.Where(p=>p!=null).SelectMany(p=>p.GetComponentsInChildren<Gun>(true)).Where(g=>g!=null&&g.attachedUnit==unit).Distinct().ToArray();
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Argus gun recording test");
        await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Live scene unavailable");TimeScaleManager.Scale=1;
        await ReplayManager.i.SwitchState(ModStates.Record);await Until(()=>Recorder.Active!=null&&Recorder.Active.IsRecording,15,"Recorder unavailable");
        string partial=Recorder.Active.RecordingPath;
        string shipSelector=Environment.GetEnvironmentVariable("REPLAY_NAVAL_SHIP")??"Frigate1";
        string gunSelector=Environment.GetEnvironmentVariable("REPLAY_NAVAL_GUN")??"127mm";
        var def=(ShipDefinition)Encyclopedia.Lookup.First(p=>p.Value is ShipDefinition&&p.Key==shipSelector).Value;
        var ship=NetworkSceneSingleton<Spawner>.i.SpawnShip(def.unitPrefab,new GlobalPosition(1000,0,1000),Quaternion.identity,null,null,1f,false);
        await Until(()=>ship!=null&&Guns(ship).Any(g=>(g.info?.weaponName??"").IndexOf(gunSelector,StringComparison.OrdinalIgnoreCase)>=0),20,"Selected ship gun unavailable");
        await Wait(2);
        var gun=Guns(ship).First(g=>(g.info?.weaponName??"").IndexOf(gunSelector,StringComparison.OrdinalIgnoreCase)>=0);
        var turret=gun.weaponStation.GetTurret();if(turret==null||gun.muzzles==null||gun.muzzles.Length==0)throw new Exception("Argus turret or muzzle unavailable");
        turret.enabled=false;turret.traverseAngle=38;turret.elevationAngle=-12;turret.transform.localEulerAngles=new Vector3(0,38,0);turret.elevationTransform.localEulerAngles=new Vector3(-12,0,0);
        await Wait(.3f);
        Vector3 muzzle=gun.muzzles[0].position;Vector3 direction=gun.muzzles[0].forward;
        uint shipId=ship.persistentID.Id;
        var tracked=typeof(Recorder).GetMethod("IsTracked",BindingFlags.Instance|BindingFlags.NonPublic);
        log("NAVAL CASE "+shipSelector+" "+gunSelector+" invoking native shot actor="+shipId+" tracked="+tracked.Invoke(Recorder.Active,new object[]{ship})+" current station="+(turret.currentWeaponStation==null?"none":turret.currentWeaponStation.Number.ToString())+" guided="+(gun.guidedProjectile==null?"none":gun.guidedProjectile.jsonKey));gun.SpawnBullet(0f);await Wait(1);
        await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file))throw new Exception("Argus recording missing: "+ReplayManager.i.LastRecordingStopStatus);
        log("ARGUS LIVE "+ReplayIntegrityCheck.Check(file));GunBulletEvent shot=null;int shots=0,turrets=0;SpawnEvent projectile=null;
        using(var input=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(input);
            for(long i=0;i<header.EventCount;i++)
            {
                var e=ReplayRecordTransport.ReadEvent(input,header.Format);
                if(e is UpdateTurretEvent te&&te.attachedUnitId==shipId)turrets++;
                if(e is SpawnEvent se&&se.ownerId==shipId&&se.unitId!=shipId){log("ARGUS LIVE owned spawn key="+se.jsonKey+" id="+se.unitId+" at="+se.Time.ToString("F2"));projectile=new SpawnEvent{unitId=se.unitId,ownerId=se.ownerId,jsonKey=se.jsonKey,Time=se.Time,pos=se.pos,rotation=se.rotation};}
                if(e is GunBulletEvent ge&&ge.unitId==shipId){shots++;if(shot==null)shot=ge;else ReplayEventFactory.Return(e);}
                else ReplayEventFactory.Return(e);
            }
        }
        if(turrets==0||(shot==null&&projectile==null))throw new Exception("Actual Argus shot or turret samples absent from recording; shots="+shots+" turrets="+turrets);
        if(gun.guidedProjectile!=null)
        {
            if(projectile==null||shot==null||!shot.sound||shot.active)throw new Exception("Guided gun did not record both projectile and presentation");
            var deltaMissile=projectile.pos.ToLocalPosition()-muzzle;
            log("ARGUS LIVE projectile muzzle offset="+deltaMissile.magnitude.ToString("F2")+"m forward="+Vector3.Dot(deltaMissile,direction).ToString("F2")+"m");
            await ReplayManager.i.StartPlaying(Path.GetFileName(file));var replay=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||replay==null)throw new Exception("Argus recording failed to reopen");
            replay.Pause();await replay.TimelineJump(projectile.Time+.03,0,false);await UniTask.NextFrame();
            var missile=replay.FindActor(projectile.unitId);if(missile==null)throw new Exception("Recorded Argus projectile missing in playback");
            var actor=replay.FindActor(shipId);var guidedGun=Guns(actor).First(g=>(g.info?.weaponName??"").IndexOf(gunSelector,StringComparison.OrdinalIgnoreCase)>=0);
            float muzzleError=Vector3.Distance(guidedGun.muzzles[0].position,projectile.pos.ToLocalPosition());
            float aimError=Quaternion.Angle(guidedGun.muzzles[0].rotation,projectile.rotation);
            log("ARGUS LIVE playback muzzle error="+muzzleError.ToString("F2")+"m angle="+aimError.ToString("F2")+" degrees");
            if(muzzleError>2||aimError>3)throw new Exception("Recorded projectile and replay barrel diverge");
            var replayTurret=guidedGun.weaponStation.GetTurret();replayTurret.traverseAngle=-70;replayTurret.transform.localEulerAngles=new Vector3(0,-70,0);
            var guidedController=(UnitController)typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(replay);
            guidedController.Execute(shot);
            if(Math.Abs(Mathf.DeltaAngle(replayTurret.traverseAngle,38))>2)throw new Exception("Muzzle effect used an out-of-date turret orientation: "+replayTurret.traverseAngle);
            var audio=typeof(UnitController).GetField("_replayGunAudio",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(guidedController) as System.Collections.IDictionary;
            if(audio==null||!audio.Contains(guidedGun))throw new Exception("Guided cannon shot presentation did not play");
            log("NAVAL CASE PASS "+shipSelector+" "+gunSelector+" guided projectile and muzzle/audio presentation recorded and played at recorded turret orientation; turret samples="+turrets);ReplayEventFactory.Return(shot);return;
        }
        var delta=shot.position.ToLocalPosition()-muzzle;
        log("ARGUS LIVE recorded shots="+shots+" turret samples="+turrets+" first post-trace offset="+delta.magnitude.ToString("F2")+"m forward="+Vector3.Dot(delta,direction).ToString("F2")+"m");
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;
        if(!ReplayRuntime.IsPlayback||player==null)throw new Exception("Argus recording failed to reopen");
        player.Pause();await player.TimelineJump(Math.Max(0,shot.Time-.1),0,false);
        var replayShip=player.FindActor(shot.unitId);if(replayShip==null)throw new Exception("Recorded Argus missing");
        var replayGun=Guns(replayShip).First(g=>(g.info?.weaponName??"").IndexOf(gunSelector,StringComparison.OrdinalIgnoreCase)>=0);
        var controller=(UnitController)typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
        controller.Execute(shot);
        var replayMuzzle=replayGun.muzzles[0];
        float muzzlePositionError=Vector3.Distance(replayMuzzle.position,muzzle);
        float barrelAngleError=Vector3.Angle(replayMuzzle.forward,direction);
        float lateralError=Vector3.Cross(shot.position.ToLocalPosition()-replayMuzzle.position,replayMuzzle.forward).magnitude;
        log("NAVAL ALIGN "+shipSelector+" "+gunSelector+" muzzle="+muzzlePositionError.ToString("F2")+"m angle="+barrelAngleError.ToString("F2")+"deg projectile lateral="+lateralError.ToString("F2")+"m");
        if(muzzlePositionError>2||barrelAngleError>3||lateralError>3)throw new Exception("Replay barrel does not align with the recorded shot");
        if(replayGun.bulletSim==null||replayGun.bulletSim.bullets.Count==0)throw new Exception("Actual recorded Argus projectile did not play");
        var bullet=replayGun.bulletSim.bullets.Last();if(Vector3.Distance(bullet.position.ToLocalPosition(),shot.position.ToLocalPosition())>.01f)throw new Exception("Argus projectile playback origin changed");
        log("NAVAL CASE PASS "+shipSelector+" "+gunSelector+" native shot recorded, reopened and recreated at recorded post-trace projectile position");ReplayEventFactory.Return(shot);
    }
}
