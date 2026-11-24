using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class RollingQuitProbe
{
    static string Marker=>Environment.GetEnvironmentVariable("REPLAY_ROLLQUIT_MARKER")??throw new Exception("Missing rolling quit marker");
    public static async UniTask Record(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Rolling save on exit test");
        TimeScaleManager.Scale=1;RollingRecordingController.Instance.SetEnabled(true);
        float deadline=Time.realtimeSinceStartup+15;while(!RollingRecordingController.Instance.Capturing){if(Time.realtimeSinceStartup>deadline)throw new Exception("Rolling recorder unavailable");await UniTask.NextFrame();}
        await UniTask.Delay(TimeSpan.FromSeconds(1));var recorder=Recorder.Active;
        uint pendingId=0;
        if(Environment.GetEnvironmentVariable("REPLAY_ROLLQUIT_WAIT")=="1")
        {
            var def=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
            var craft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
            if(craft==null)throw new Exception("Pending rolling quit aircraft unavailable");pendingId=craft.persistentID.Id;craft.loadout=null;
        }
        string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays","rolling-quit-"+Guid.NewGuid().ToString("N")+".replay");File.WriteAllText(Marker,file+"\n"+pendingId);
        var saving=recorder.SaveRolling(file);if(saving.IsCompleted)throw new Exception("Slow writer did not stay in flight");
        log("ROLLING QUIT active clip write; requesting normal application quit");
    }
    public static async UniTask Verify(Action<string> log)
    {
        var marker=File.ReadAllLines(Marker);string file=marker[0];uint pendingId=marker.Length>1?uint.Parse(marker[1]):0;
        if(!File.Exists(file)||File.Exists(file+".partial"))throw new Exception("Rolling clip did not finish during normal quit");
        string result=ReplayIntegrityCheck.Check(file);
        if(pendingId!=0)
        {
            bool spawned=false;using(var input=new BinaryReader(File.OpenRead(file))){var header=ReplayRecordingHeader.Read(input);for(long n=0;n<header.EventCount;n++){var e=ReplayRecordTransport.ReadEvent(input,header.Format);if(e is SpawnEvent spawn&&spawn.unitId==pendingId)spawned=true;ReplayEventFactory.Return(e);}}
            if(!spawned)throw new Exception("Waiting quit clip lost its pending aircraft");
        }
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(Path.GetFileName(file));
        if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("Rolling quit clip failed to reopen");
        log("ROLLING QUIT PASS slow clip write finished on normal quit, "+result+" and reopened");
    }
}
