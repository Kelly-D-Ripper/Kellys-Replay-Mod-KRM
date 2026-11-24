using System;
using System.IO;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using ReplayMod.Director;
using UnityEngine;

public static class MissingResourceProbe
{
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        string name="missing-resource-"+Guid.NewGuid().ToString("N")+".replay";
        string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",name);
        const string absent="test_missing_late_unit_56f7ac2e";
        using(var stream=new FileStream(file,FileMode.CreateNew,FileAccess.ReadWrite))
        using(var output=new BinaryWriter(stream))
        {
            var header=new ReplayRecordingHeader{MapPath=key.Path,MapType=(byte)key.Type,Resources=Array.Empty<string>()};
            header.Write(output);
            output.Write(ReplayRecordTransport.EncodeEvent(new SpawnEvent{Time=8,unitId=908,ownerId=908,jsonKey=absent,factionName="no faction",rotation=Quaternion.identity}));
            header.FinalizeHeader(output,10,1);
        }
        string check=ReplayIntegrityCheck.Check(file);
        var previous=ReplayManager.i.Playback;bool wasPlayback=ReplayRuntime.IsPlayback;
        try{await ReplayManager.i.StartPlaying(name);}catch(InvalidDataException error)
        {if(error.Message.IndexOf(absent,StringComparison.Ordinal)<0)throw;}
        string status=ReplayDirector.i.Status??"";
        if((wasPlayback&&(!ReplayRuntime.IsPlayback||!ReferenceEquals(previous,ReplayManager.i.Playback)))||
            (!wasPlayback&&ReplayRuntime.IsPlayback)||status.IndexOf(absent,StringComparison.Ordinal)<0||status.IndexOf("missing unit",StringComparison.OrdinalIgnoreCase)<0)
            throw new Exception("Missing later unit not reported before playback or original session changed: "+status);
        log("MISSING RESOURCE PASS valid replay with later unknown unit rejected by name before scene load; "+check);
        string legacy="missing-legacy-resource-"+Guid.NewGuid().ToString("N")+".replay";string legacyPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",legacy);
        using(var output=new BinaryWriter(File.Create(legacyPath))){var header=new ReplayRecordingHeader{Format=9,MapPath=key.Path,MapType=(byte)key.Type,Resources=new[]{absent}};header.Write(output);output.Write(ReplayRecordTransport.EncodeEvent(new LandingEvent{Time=1,unitId=1}));header.FinalizeHeader(output,2,1);}
        previous=ReplayManager.i.Playback;bool legacyRejected=false;try{await ReplayManager.i.StartPlaying(legacy);}catch(InvalidDataException error){legacyRejected=error.Message.Contains(absent);}
        if(!legacyRejected||!ReferenceEquals(previous,ReplayManager.i.Playback))throw new Exception("Legacy required header resource was not rejected or playback changed");
        log("MISSING RESOURCE PASS format-9 header requirement preserved and current replay retained");
    }
}
