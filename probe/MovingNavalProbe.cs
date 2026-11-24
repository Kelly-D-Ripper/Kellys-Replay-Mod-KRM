using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class MovingNavalProbe
{
    static async UniTask Until(Func<bool> condition,float seconds,string failure){float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new Exception(failure);await UniTask.NextFrame();}}
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    static Gun[] Guns(Unit unit)=>unit.partLookup.Where(p=>p!=null).SelectMany(p=>p.GetComponentsInChildren<Gun>(true)).Where(g=>g!=null&&g.attachedUnit==unit).Distinct().ToArray();
    public static async UniTask Run(MapKey key,string shipKey,string gunName,Action<string> log)
    {
        RollingRecordingController.Instance.SetEnabled(false);await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Moving naval gun recording");
        await Until(()=>RollingRecordingController.IsUsableGameplay(),60,"Live scene unavailable");TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
        var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording||recorder.IsRolling)throw new Exception("Full recorder unavailable");string partial=recorder.RecordingPath;
        var def=(ShipDefinition)Encyclopedia.Lookup[shipKey];var ship=NetworkSceneSingleton<Spawner>.i.SpawnShip(def.unitPrefab,new GlobalPosition(1000,0,1000),Quaternion.identity,null,null,1f,false);
        await Until(()=>ship!=null&&Guns(ship).Any(g=>(g.info?.weaponName??"").IndexOf(gunName,StringComparison.OrdinalIgnoreCase)>=0),20,"Gun unavailable");await Wait(2);
        var gun=Guns(ship).First(g=>(g.info?.weaponName??"").IndexOf(gunName,StringComparison.OrdinalIgnoreCase)>=0);var turret=gun.weaponStation.GetTurret();if(turret==null||gun.muzzles==null||gun.muzzles.Length==0)throw new Exception("Turret unavailable");
        turret.enabled=false;var live=new List<(Vector3 position,Quaternion rotation,float angle)>();float started=Time.realtimeSinceStartup;int fired=0;
        while(Time.realtimeSinceStartup-started<3.2f)
        {
            float elapsed=Time.realtimeSinceStartup-started;float angle=-55+elapsed*35;float elevation=-8;
            turret.traverseAngle=angle;turret.elevationAngle=elevation;turret.transform.localEulerAngles=new Vector3(0,angle,0);turret.elevationTransform.localEulerAngles=new Vector3(elevation,0,0);
            if(fired<5&&elapsed>=.4f+fired*.5f){var muzzle=gun.muzzles[0];live.Add((muzzle.position,muzzle.rotation,angle));gun.SpawnBullet(0);fired++;}
            await UniTask.NextFrame();
        }
        await Wait(.5f);int faults=recorder.CaptureFailureCount;uint shipId=ship.persistentID.Id;await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;if(!File.Exists(file))throw new Exception("Moving turret recording missing");ReplayIntegrityCheck.Check(file);
        var shots=new List<GunBulletEvent>();var projectiles=new List<SpawnEvent>();using(var reader=new BinaryReader(File.OpenRead(file)))
        {var header=ReplayRecordingHeader.Read(reader);for(long i=0;i<header.EventCount;i++){var e=ReplayRecordTransport.ReadEvent(reader,header.Format);if(e is GunBulletEvent shot&&shot.unitId==shipId)shots.Add(shot);else if(e is SpawnEvent spawn&&spawn.ownerId==shipId&&spawn.unitId!=shipId){projectiles.Add(new SpawnEvent{Time=spawn.Time,unitId=spawn.unitId,pos=spawn.pos,rotation=spawn.rotation});ReplayEventFactory.Return(e);}else ReplayEventFactory.Return(e);}}
        if(faults!=0||shots.Count!=5||live.Count!=5||(gun.guidedProjectile!=null&&projectiles.Count!=5))throw new Exception("Moving shots missing: recorded="+shots.Count+" live="+live.Count+" faults="+faults);
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||player==null)throw new Exception("Moving gun replay failed to open");player.Pause();
        float worstAngle=0,worstPosition=0;
        foreach(int i in new[]{3,0,4,1,2})
        {
            var shot=shots[i];await player.TimelineJump(Math.Max(0,shot.Time-.01),0,false);var actor=player.FindActor(shipId);if(actor==null)throw new Exception("Recorded ship missing");
            var replayGun=Guns(actor).First(g=>(g.info?.weaponName??"").IndexOf(gunName,StringComparison.OrdinalIgnoreCase)>=0);
            var controller=(UnitController)typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);controller.Execute(shot);
            float angleError=Quaternion.Angle(replayGun.muzzles[0].rotation,live[i].rotation),positionError=Vector3.Distance(replayGun.muzzles[0].position,live[i].position);
            worstAngle=Math.Max(worstAngle,angleError);worstPosition=Math.Max(worstPosition,positionError);
            log("MOVING NAVAL "+gunName+" shot="+i+" liveAngle="+live[i].angle.ToString("F1")+" angleError="+angleError.ToString("F2")+" positionError="+positionError.ToString("F2"));
            if(angleError>3||positionError>2)throw new Exception("Shot muzzle diverged while turret was moving");
            if(gun.guidedProjectile==null)
            {
                if(replayGun.bulletSim==null||replayGun.bulletSim.bullets.Count==0)throw new Exception("Recorded ballistic shot absent");
                var bullet=replayGun.bulletSim.bullets.Last();if(Vector3.Distance(bullet.position.ToLocalPosition(),shot.position.ToLocalPosition())>.01f)throw new Exception("Ballistic projectile origin differs from recorded event");
            }
            else
            {
                var missile=projectiles[i];float launchError=Vector3.Distance(missile.pos.ToLocalPosition(),live[i].position),launchAngle=Quaternion.Angle(missile.rotation,live[i].rotation);
                if(launchError>2||launchAngle>3)throw new Exception("Guided projectile recorded away from live muzzle");
                await player.TimelineJump(missile.Time,0,false);var actorMissile=player.FindActor(missile.unitId);if(actorMissile==null)throw new Exception("Guided projectile absent at launch");
                float replayError=Vector3.Distance(actorMissile.transform.position,missile.pos.ToLocalPosition());
                log("MOVING NAVAL guided shot="+i+" launchOrigin="+launchError.ToString("F2")+" replayOrigin="+replayError.ToString("F2"));
                if(replayError>2)throw new Exception("Guided projectile replayed away from recorded origin");
            }
        }
        foreach(var shot in shots)ReplayEventFactory.Return(shot);
        log("MOVING NAVAL PASS "+shipKey+" "+gunName+" five shots during traverse, shuffled seeks, maxAngle="+worstAngle.ToString("F2")+" maxPosition="+worstPosition.ToString("F2"));
    }
}
