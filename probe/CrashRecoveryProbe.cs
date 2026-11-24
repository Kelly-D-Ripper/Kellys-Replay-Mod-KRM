using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class CrashRecoveryProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    static string Marker=>Environment.GetEnvironmentVariable("REPLAY_CRASH_MARKER")??throw new Exception("Missing isolated crash marker");
    static string Hash(string path){using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream));}
    public static async UniTask Record(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Interrupted recording test");
        TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
        if(Recorder.Active==null||!Recorder.Active.IsRecording)throw new Exception("Crash test recording failed to start");
        if(Environment.GetEnvironmentVariable("REPLAY_CRASH_JET")=="1")
        {
            var def=(AircraftDefinition)Encyclopedia.Lookup["Fighter1"];
            var faction=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
            var craft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,faction,null,1f,1f);
            if(craft==null)throw new Exception("Crash fixture jet did not spawn");
        }
        string partial=Recorder.Active.RecordingPath;await Wait(6);
        File.WriteAllText(Marker,partial);log("CRASH TEST isolated recording ready for supervised termination");
        while(true)await UniTask.NextFrame();
    }
    public static async UniTask RecoverTornHeader(Action<string> log)
    {
        string folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays"));
        string original=Path.GetFullPath(File.ReadAllText(Marker));
        if(!original.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Crash fixture is outside isolated replay folder");
        string partial=Path.Combine(folder,"torn-header-fixture-"+Guid.NewGuid().ToString("N")+".replay.partial");
        File.Copy(original,partial,false);
        ReplayRecordingHeader header;using(var reader=new BinaryReader(File.OpenRead(partial)))header=ReplayRecordingHeader.Read(reader,false);
        using(var file=new FileStream(partial,FileMode.Open,FileAccess.Write,FileShare.None))using(var writer=new BinaryWriter(file))
        {file.Position=header.DurationOffset;writer.Write(double.NaN);file.Position=header.CountOffset;writer.Write(-1L);}
        string hash=Hash(partial);var result=await ReplayRecordingRecovery.RecoverAsync(partial);
        if(hash!=Hash(partial)||result.EventCount<2)throw new Exception("Torn-header recovery changed original or lost the prefix");
        log("TORN HEADER "+ReplayIntegrityCheck.Check(result.Path));
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(Path.GetFileName(result.Path));
        var playback=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||playback==null)throw new Exception("Torn-header recovered replay failed to open");
        playback.Pause();await playback.TimelineJump(Math.Min(1,result.Duration),0,false);
        log("TORN HEADER PASS recovered "+result.EventCount+" real recorded events from damaged duration and count; source preserved and copy opened and sought");
    }
    public static async UniTask RecoverTornMarker(Action<string> log)
    {
        string folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays"));
        string original=Path.GetFullPath(File.ReadAllText(Marker));
        if(!original.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Crash fixture is outside isolated replay folder");
        string partial=Path.Combine(folder,"torn-marker-fixture-"+Guid.NewGuid().ToString("N")+".replay.partial");
        File.Copy(original,partial,false);
        ReplayRecordingHeader header;using(var reader=new BinaryReader(File.OpenRead(partial)))header=ReplayRecordingHeader.Read(reader,false);
        using(var file=new FileStream(partial,FileMode.Open,FileAccess.Write,FileShare.None))
        {file.Position=header.MarkerOffset;byte[] marker=BitConverter.GetBytes(ReplayRecordingHeader.CompletedMarker);file.Write(marker,0,2);file.Flush(true);}
        string hash=Hash(partial);bool rejected=false;
        try{ReplayIntegrityCheck.Check(partial);}catch(InvalidDataException){rejected=true;}
        if(!rejected)throw new Exception("Normal replay validation accepted a torn marker");
        var result=await ReplayRecordingRecovery.RecoverAsync(partial);
        if(hash!=Hash(partial)||result.EventCount<2||result.Reason.IndexOf("marker",StringComparison.OrdinalIgnoreCase)<0)throw new Exception("Torn-marker recovery changed original or lost the prefix");
        log("TORN MARKER "+ReplayIntegrityCheck.Check(result.Path));
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(Path.GetFileName(result.Path));
        var playback=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||playback==null)throw new Exception("Torn-marker recovered replay failed to open");
        playback.Pause();await playback.TimelineJump(Math.Min(1,result.Duration),0,false);
        log("TORN MARKER PASS recovered "+result.EventCount+" real recorded events; strict loading rejected damage, original preserved, copy opened and sought");
    }
    public static async UniTask RecoverCorruptFrame(Action<string> log)
    {
        string folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays"));
        string original=Path.GetFullPath(Path.Combine(folder,"Terrain1_2026-11-17_06-45-37_d22af805.replay"));
        ReplayIntegrityCheck.Check(original);string fixture=Path.Combine(folder,"corrupt-frame-fixture-"+Guid.NewGuid().ToString("N")+".replay.partial");File.Copy(original,fixture,false);
        long damagedIndex,damagedOffset;
        using(var stream=new FileStream(fixture,FileMode.Open,FileAccess.ReadWrite,FileShare.None))using(var reader=new BinaryReader(stream))using(var writer=new BinaryWriter(stream))
        {
            var header=ReplayRecordingHeader.Read(reader);damagedIndex=header.EventCount*3/4;
            for(long i=0;i<damagedIndex;i++)ReplayRecordTransport.ReadBody(reader);
            damagedOffset=stream.Position+4;stream.Position=damagedOffset;uint crc=reader.ReadUInt32();stream.Position=damagedOffset;writer.Write(crc^1u);writer.Flush();stream.Flush(true);
        }
        string hash=Hash(fixture);bool rejected=false;
        try{ReplayIntegrityCheck.Check(fixture);}catch(InvalidDataException error){rejected=error.Message.IndexOf("checksum",StringComparison.OrdinalIgnoreCase)>=0;}
        if(!rejected)throw new Exception("Corrupted checksum was not detected");
        var result=await ReplayRecordingRecovery.RecoverAsync(fixture);
        if(hash!=Hash(fixture)||result.EventCount<1||result.EventCount>=damagedIndex||result.DiscardedBytes<=0)throw new Exception("Corrupt frame recovery prefix or source preservation failed");
        log("CORRUPT FRAME detected byte="+damagedOffset+" event="+damagedIndex+" recovered="+result.EventCount+" discardedBytes="+result.DiscardedBytes+" "+ReplayIntegrityCheck.Check(result.Path));
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(Path.GetFileName(result.Path));
        var playback=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||playback==null)throw new Exception("Recovered corrupt-frame replay failed to open");
        playback.Pause();await playback.TimelineJump(Math.Min(1,result.Duration),0,false);
        log("CORRUPT FRAME PASS checksum damage detected; verified earlier prefix recovered, source preserved and copy opened and sought");
    }
    public static async UniTask Recover(Action<string> log)
    {
        string partial=File.ReadAllText(Marker);string original=Hash(partial);
        var result=await ReplayRecordingRecovery.RecoverAsync(partial);
        if(original!=Hash(partial))throw new Exception("Original interrupted recording changed during recovery");
        if(result.EventCount<1||!ReplayIntegrityCheck.Check(result.Path).Contains("passed"))throw new Exception("Recovered recording failed integrity");
        if(Environment.GetEnvironmentVariable("REPLAY_EXPECT_ENGINE_CORE")=="1")
        {
            int cores=0;using(var reader=new BinaryReader(File.OpenRead(result.Path)))
            {
                var header=ReplayRecordingHeader.Read(reader);if(header.Format!=11)throw new Exception("Expected format-11 interrupted recording");
                for(long i=0;i<header.EventCount;i++)if(ReplayRecordTransport.ReadEvent(reader,header.Format) is EngineCoreStateEvent)cores++;
            }
            if(cores<1)throw new Exception("Recovered recording lost all jet core samples");
            log("CRASH TEST recovered format-11 engine core samples="+cores);
        }
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(Path.GetFileName(result.Path));
        if(!ReplayRuntime.IsPlayback||ReplayManager.i.Playback==null)throw new Exception("Recovered recording failed to open");
        log("CRASH TEST PASS forced process termination; recovered "+result.EventCount+" complete events, original preserved and recovered copy opened");
    }
}
