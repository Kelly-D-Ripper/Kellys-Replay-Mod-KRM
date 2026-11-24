using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class RollingPendingProbe
{
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Rolling pending aircraft test");
        TimeScaleManager.Scale=1;RollingRecordingController.Instance.SetEnabled(true);RollingRecordingController.Instance.SetRetention(30);
        float deadline=Time.realtimeSinceStartup+15;while(!RollingRecordingController.Instance.Capturing){if(Time.realtimeSinceStartup>deadline)throw new Exception("Rolling recorder unavailable");await UniTask.NextFrame();}
        var recorder=Recorder.Active;await UniTask.Delay(TimeSpan.FromSeconds(1));
        bool staleResource=Environment.GetEnvironmentVariable("REPLAY_STALE_RESOURCE")=="1";
        const string staleKey="ReplayProbePastUnit_NoLongerInClip";
        if(staleResource)((System.Collections.Generic.List<string>)AccessTools.Field(typeof(Recorder),"_usedUnits").GetValue(recorder)).Add(staleKey);
        var def=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
        var craft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
        if(craft==null||craft.loadout?.weapons==null)throw new Exception("Rolling test aircraft missing");
        uint id=craft.persistentID.Id;craft.SetGear(true);
        bool weapons=Environment.GetEnvironmentVariable("REPLAY_ROLLING_WEAPONS")=="1";
        if(weapons)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(2));
            var gun=UnityEngine.Object.FindObjectsOfType<Gun>().FirstOrDefault(g=>g.attachedUnit==craft&&g.guidedProjectile==null);
            var ejector=UnityEngine.Object.FindObjectsOfType<FlareEjector>().FirstOrDefault(e=>e.aircraft==craft&&e.flarePrefab!=null&&e.ejectionPoints!=null&&e.ejectionPoints.Length>0);
            if(gun==null||ejector==null)throw new Exception("Native Brawler gun or flare ejector unavailable");
            AccessTools.Method(typeof(Recorder),"HandleUnitUnregister").Invoke(recorder,new object[]{craft});
            var original=craft.loadout;craft.loadout=null;AccessTools.Method(typeof(Recorder),"HandleUnitRegister").Invoke(recorder,new object[]{craft,craft.persistentID});craft.loadout=original;
            craft.SetGear(true);gun.SpawnBullet(0);
            var point=ejector.ejectionPoints[0].transform;var obj=UnityEngine.Object.Instantiate(ejector.flarePrefab,point.position,point.rotation);var flare=obj.GetComponent<IRFlare>();
            if(flare==null)throw new Exception("Native flare component unavailable");flare.LaunchFlare(craft,point,craft.rb.velocity+Vector3.down*20);
        }
        bool badDamage=Environment.GetEnvironmentVariable("REPLAY_ROLLING_BAD_DAMAGE")=="1";
        if(badDamage)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(2));
            AccessTools.Method(typeof(Recorder),"HandleUnitUnregister").Invoke(recorder,new object[]{craft});
            var original=craft.loadout;craft.loadout=null;AccessTools.Method(typeof(Recorder),"HandleUnitRegister").Invoke(recorder,new object[]{craft,craft.persistentID});craft.loadout=original;
            int previousFaults=recorder.CaptureFailureCount;var damage=ReplayEventFactory.GetEvent<PartDamageEvent>();damage.Time=0;damage.unitId=id;damage.hitPoints=float.NaN;
            bool queued=(bool)AccessTools.Method(typeof(Recorder),"QueueEventForUnit").Invoke(recorder,new object[]{craft,damage});
            if(queued||recorder.CaptureFailureCount!=previousFaults+1||!recorder.IsRecording)throw new Exception("Invalid pending damage was not skipped and warned");
            craft.SetGear(true);
        }
        bool reRegistered=weapons||badDamage;
        string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays","rolling-pending-"+Guid.NewGuid().ToString("N")+".replay");
        bool paused=Environment.GetEnvironmentVariable("REPLAY_ROLLING_PAUSED")=="1";
        if(paused){var method=AccessTools.Method(typeof(GameplayUI),"PauseGame");method.Invoke(method.IsStatic?null:UnityEngine.Object.FindObjectOfType<GameplayUI>(),null);if(!GameplayUI.GameIsPaused)throw new Exception("Native pause did not open");}
        float pauseScale=TimeScaleManager.Scale;
        bool delayed=Environment.GetEnvironmentVariable("REPLAY_ROLLING_DELAYED")=="1"||paused;
        bool fallback=Environment.GetEnvironmentVariable("REPLAY_ROLLING_FALLBACK")=="1";
        bool invalid=Environment.GetEnvironmentVariable("REPLAY_ROLLING_INVALID")=="1";
        bool ends=Environment.GetEnvironmentVariable("REPLAY_ROLLING_ENDS")=="1"||invalid;
        var loadout=craft.loadout;
        if(delayed||fallback||ends)craft.loadout=null;
        if(fallback)
        {
            var pending=(System.Collections.IDictionary)AccessTools.Field(typeof(Recorder),"_pendingUnitRegistrations").GetValue(recorder);
            var entry=pending[craft.persistentID];if(entry==null)throw new Exception("Pending aircraft missing");
            AccessTools.Field(entry.GetType(),"since").SetValue(entry,Time.realtimeSinceStartup-1);
        }
        // Snapshot in the same frame as native registration, before Update.
        var saving=recorder.SaveRolling(file);
        if((delayed||ends)&&saving.IsCompleted)throw new Exception("Rolling save did not wait for initialization");
        if(delayed||fallback||ends)craft.loadout=loadout;
        if(ends)
        {
            if(invalid)AccessTools.Field(typeof(Recorder),"_recordingMission").SetValue(recorder,new object());
            RollingRecordingController.Instance.SetEnabled(false);await ReplayManager.i.SwitchState(ModStates.Idle);
            if(invalid&&(recorder.CaptureFailureCount==0||ReplayManager.i.LastRecordingStopStatus.IndexOf("details may be missing",StringComparison.OrdinalIgnoreCase)<0))throw new Exception("Mission loss did not warn about pending actor");
            await saving;
            if(!File.Exists(file)||File.Exists(file+".partial")||!ReplayIntegrityCheck.Check(file).Contains("passed"))throw new Exception("Stopped rolling save did not finish safely");
            log("ROLLING WAIT "+(invalid?"MISSION LOSS":"END")+" PASS stopped recording completed the waiting clip and verified it"+(invalid?" with a missing-details warning":""));return;
        }
        await saving;
        if(fallback&&recorder.CaptureFailureCount==0)throw new Exception("Rolling fallback did not report incomplete loadout");
        if(!File.Exists(file)||!ReplayIntegrityCheck.Check(file).Contains("passed"))throw new Exception("Rolling clip missing or invalid");
        if(staleResource)
        {
            bool fixedExpected=Environment.GetEnvironmentVariable("REPLAY_STALE_EXPECT_FIXED")=="1";
            if(fixedExpected)
            {
                ReplayRecordingHeader newHeader;using(var input=new BinaryReader(File.OpenRead(file)))newHeader=ReplayRecordingHeader.Read(input);
                if(newHeader.Resources.Contains(staleKey)||ReplayIntegrityCheck.Inspect(file).UnitDefinitions.Contains(staleKey))throw new Exception("New rolling clip retained stale resource");
                string old=Environment.GetEnvironmentVariable("REPLAY_OLD_STALE_CLIP");if(string.IsNullOrEmpty(old)||old!=Path.GetFileName(old))throw new Exception("Old format-10 clip fixture missing");
                string oldPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",old);ReplayRecordingHeader oldHeader;using(var input=new BinaryReader(File.OpenRead(oldPath)))oldHeader=ReplayRecordingHeader.Read(input);
                if(!oldHeader.Resources.Contains(staleKey)||ReplayIntegrityCheck.Inspect(oldPath).UnitDefinitions.Contains(staleKey))throw new Exception("Existing stale-header fixture or inspection incorrect");
                RollingRecordingController.Instance.SetEnabled(false);
                await ReplayManager.i.StartPlaying(old);if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("Existing stale clip did not open");
                await ReplayManager.i.StartPlaying(Path.GetFileName(file));if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("New rolling clip did not open");
                var replay=ReplayManager.i.Playback;replay.Pause();await replay.TimelineJump(Math.Min(.5,replay.replayDuration),0,false);
                log("STALE RESOURCE FIX PASS existing stale-header clip and new clip opened; new header omits obsolete session resources");return;
            }
            bool headerContains=ReplayIntegrityCheck.Inspect(file).UnitDefinitions.Contains(staleKey);bool rejected=false;
            try{await ReplayManager.i.StartPlaying(Path.GetFileName(file));}catch(Exception error){rejected=error.ToString().Contains(staleKey);}
            log("STALE RESOURCE headerContains="+headerContains+" preflightRejected="+rejected+" recorderContinues="+recorder.IsRecording);
            if(!headerContains||!rejected||!recorder.IsRecording)throw new Exception("Old-unit false missing-resource case did not reproduce");
            RollingRecordingController.Instance.SetEnabled(false);await ReplayManager.i.SwitchState(ModStates.Idle);log("STALE RESOURCE REPRODUCED clip blocked by a unit not present in its event stream");return;
        }
        int spawns=0,gearAfter=0,gearBefore=0,shots=0,flares=0;double lastSpawn=-1;
        using(var input=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(input);for(long n=0;n<header.EventCount;n++)
            {var e=ReplayRecordTransport.ReadEvent(input,header.Format);if(e is SpawnEvent spawn&&spawn.unitId==id){spawns++;lastSpawn=spawn.Time;}if(e is SetGearEvent gear&&gear.unitId==id&&gear.deployed){if(spawns==0)gearBefore++;else if(!reRegistered||spawns>=2)gearAfter++;}if(e is GunBulletEvent shot&&shot.unitId==id&&spawns>=2)shots++;if(e is CountermeasureEvent cm&&cm.unitId==id&&cm.phase==CountermeasurePhase.Launch&&spawns>=2)flares++;ReplayEventFactory.Return(e);}
        }
        if(spawns!=(reRegistered?2:1)||gearAfter==0||gearBefore!=0||(weapons&&(shots==0||flares==0)))throw new Exception("Rolling save omitted pending spawn or gear: spawns="+spawns+" gearAfter="+gearAfter+" gearBefore="+gearBefore);
        if(!recorder.IsRecording||!recorder.IsRolling)throw new Exception("Saving recent clip stopped rolling recording");
        if(paused){if(!GameplayUI.GameIsPaused||Math.Abs(TimeScaleManager.Scale-pauseScale)>.001)throw new Exception("Rolling save changed the game's pause state");var method=AccessTools.Method(typeof(GameplayUI),"ResumeGame");method.Invoke(method.IsStatic?null:UnityEngine.Object.FindObjectOfType<GameplayUI>(),null);TimeScaleManager.Scale=1;}
        RollingRecordingController.Instance.SetEnabled(false);await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(Path.GetFileName(file));
        if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("Rolling clip failed to reopen");
        var player=ReplayManager.i.Playback;player.Pause();await player.TimelineJump(lastSpawn,0,false);
        if(!(player.FindActor(id) is Aircraft restored)||!restored.gearDeployed)throw new Exception("Rolling clip did not restore the aircraft and gear");
        log("ROLLING "+(badDamage?"BAD DAMAGE":weapons?"WEAPONS":paused?"PAUSED":fallback?"FALLBACK":delayed?"DELAYED":"PENDING")+" PASS same-frame clip has spawned aircraft and gear"+(weapons?", gunfire and flare":"")+"; buffer continued and clip reopened");
    }
}
