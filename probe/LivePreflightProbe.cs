using System;
using System.IO;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class LivePreflightProbe
{
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);
        await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Live replay preflight test");
        TimeScaleManager.Scale=1;
        await ReplayManager.i.SwitchState(ModStates.Record);
        var recorder=Recorder.Active;
        if(recorder==null||!recorder.IsRecording||!MissionManager.IsRunning)throw new Exception("Live fixture not recording");
        var mission=MissionManager.CurrentMission;string partial=recorder.RecordingPath;
        string name="invalid-live-"+Guid.NewGuid().ToString("N")+".replay";
        File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",name),new byte[]{1,2,3});
        async UniTask Rejected(string replay,string expected)
        {
            bool rejected=false;
            try{await ReplayManager.i.StartPlaying(replay);}catch(InvalidDataException error){rejected=error.Message.IndexOf(expected,StringComparison.OrdinalIgnoreCase)>=0;}
            if(!rejected||!MissionManager.IsRunning||!ReferenceEquals(mission,MissionManager.CurrentMission)||Recorder.Active!=recorder||!recorder.IsRecording)
                throw new Exception("Invalid replay interrupted the live mission or recording: "+replay);
        }
        await Rejected(name,"could not be opened");
        const string absent="test_missing_live_unit_3ce704";
        string missing="missing-live-"+Guid.NewGuid().ToString("N")+".replay";
        using(var f=new FileStream(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",missing),FileMode.CreateNew,FileAccess.ReadWrite))
        using(var w=new BinaryWriter(f))
        {
            var header=new ReplayRecordingHeader{MapPath=key.Path,MapType=(byte)key.Type,Resources=Array.Empty<string>()};header.Write(w);
            w.Write(ReplayRecordTransport.EncodeEvent(new SpawnEvent{Time=3,unitId=908,ownerId=908,jsonKey=absent,factionName="no faction",rotation=Quaternion.identity}));
            header.FinalizeHeader(w,5,1);
        }
        await Rejected(missing,absent);
        string mapName="missing-map-live-"+Guid.NewGuid().ToString("N")+".replay";
        using(var f=new FileStream(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",mapName),FileMode.CreateNew,FileAccess.ReadWrite))
        using(var w=new BinaryWriter(f))
        {var header=new ReplayRecordingHeader{MapPath="test_missing_terrain_34f7ac",MapType=(byte)key.Type};header.Write(w);header.FinalizeHeader(w,5,0);}
        await Rejected(mapName,"Map is unavailable");
        log("LIVE PREFLIGHT PASS corrupt, missing-unit and missing-map replays rejected without ending mission or active recording");
        string valid=Environment.GetEnvironmentVariable("REPLAY_PREFLIGHT_GOOD")??"test.replay";
        await ReplayManager.i.StartPlaying(valid);
        string final=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null||!File.Exists(final)||!ReplayIntegrityCheck.Check(final).Contains("passed"))
            throw new Exception("Valid replay transition did not save the live recording and open playback");
        log("LIVE PREFLIGHT PASS valid replay stops and finalizes recording and opens normally");
        var player=ReplayManager.i.Playback;player.Pause();await player.TimelineJump(3,0,false);
        var replayMission=MissionManager.CurrentMission;
        bool replayRejected=false;
        try{await ReplayManager.i.StartPlaying(missing);}catch(InvalidDataException error){replayRejected=error.Message.IndexOf(absent,StringComparison.Ordinal)>=0;}
        if(!replayRejected||!ReplayRuntime.IsPlayback||!ReferenceEquals(player,ReplayManager.i.Playback)||!ReferenceEquals(replayMission,MissionManager.CurrentMission)||Math.Abs(player.currentVirtualTime-3)>.001)
            throw new Exception("Invalid replay interrupted the existing replay session");
        log("LIVE PREFLIGHT PASS invalid replay preserves existing replay and selected time");
    }
}
