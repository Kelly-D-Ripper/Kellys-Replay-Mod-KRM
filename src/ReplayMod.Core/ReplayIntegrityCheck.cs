using System;
using System.Collections.Generic;
using System.IO;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public sealed class ReplayInspectionResult
{
    public string Message;
    public byte MapType;
    public string MapPath;
    public readonly HashSet<string> UnitDefinitions=new HashSet<string>(StringComparer.Ordinal);
}

public static class ReplayIntegrityCheck
{
    // Check the same framed events, CRCs, timestamps and completion count used
    // by playback. This cannot prove that the recorder captured every scene event.
    public static string Check(string path)=>Inspect(path,false).Message;

    public static ReplayInspectionResult Inspect(string path,bool collectUnits=true)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536);
        using var reader=new BinaryReader(file);
        var header=ReplayRecordingHeader.Read(reader);
        var result=new ReplayInspectionResult{MapType=header.MapType,MapPath=header.MapPath};
        if(collectUnits&&header.RequiresHeaderResources)foreach(var resource in header.Resources)result.UnitDefinitions.Add(resource??string.Empty);
        var validation=new ReplayTimelineValidation(header.Duration,header.EventCount,header.Format<3);
        while(file.Position<file.Length)
        {
            IReplayEvent e=null;
            try
            {
                e=ReplayRecordTransport.ReadEvent(reader,header.Format);validation.Add(e.Time,e is UpdateTurretEvent);
                if(collectUnits&&e is SpawnEvent spawn)result.UnitDefinitions.Add(spawn.jsonKey??string.Empty);
            }
            finally{if(e!=null)ReplayEventFactory.Return(e);}
        }
        validation.Complete();
        result.Message="Integrity check passed: "+validation.Count+" events."+(header.Format<6?" This older format has no frame checksums.":" Event checksums verified.");
        return result;
    }
}
