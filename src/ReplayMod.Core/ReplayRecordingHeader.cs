using System;
using System.IO;
using System.Text;

namespace ReplayMod.Core;

public sealed class ReplayRecordingHeader
{
    // Format 10 adds seekable mounted-cargo presentation state.
    // Older readers reject the version before encountering an unfamiliar event.
    // Format 11 adds engine core state; format 12 adds per-part missile mount state.
    public const byte CurrentFormat=12;
    // Format 10 is indexed from its complete spawn-event stream. Header resource
    // lists can include actors that were trimmed out of a rolling clip.
    public bool RequiresHeaderResources => Format<10;
    public const int CompletedMarker=0x36525043;
    public const double MaximumDurationSeconds=7*24*60*60;
    public const int MaximumStringBytes=4096,MaximumMetadataBytes=64*1024,MaximumResources=4096,MaximumHeaderBytes=1024*1024;
    private static readonly Encoding utf8=new UTF8Encoding(false,true);
    public byte Format=CurrentFormat,MapType;
    public string MapPath="",Metadata="{}";
    public long RecordedTicks=DateTime.UtcNow.Ticks,EventCount;
    public double Duration;
    public string[] Resources=Array.Empty<string>();
    public bool Complete;
    internal bool DamagedCompletionMarker;
    public string GameAssemblyPath,RecorderAssemblyPath;
    private bool fingerprintsAdded;
    public long MarkerOffset,DurationOffset,CountOffset,DataOffset;
    public static string ReadString(BinaryReader reader,int maximum=MaximumStringBytes)
    {
        uint length=0;int shift=0;
        for(int n=0;n<5;n++)
        {
            byte part=reader.ReadByte();if(n==4&&(part&0xf0)!=0)throw new InvalidDataException("Invalid replay string length");
            length|=(uint)(part&127)<<shift;
            if((part&128)==0)
            {
                if(length>maximum)throw new InvalidDataException("Replay string exceeds limit");
                byte[] bytes=reader.ReadBytes((int)length);if(bytes.Length!=length)throw new EndOfStreamException();return utf8.GetString(bytes);
            }
            shift+=7;
        }
        throw new InvalidDataException("Invalid replay string length");
    }
    public static ReplayRecordingHeader Read(BinaryReader reader,bool requireComplete=true)
        =>Read(reader,requireComplete,false);
    // Only the interrupted-file recovery path may tolerate a torn commit marker.
    // The immutable header and event frames are still validated before publication.
    internal static ReplayRecordingHeader ReadForRecovery(BinaryReader reader)
        =>Read(reader,false,true);
    private static ReplayRecordingHeader Read(BinaryReader reader,bool requireComplete,bool allowDamagedMarker)
    {
        long start=reader.BaseStream.Position;
        if(reader.ReadInt32()!=12549)throw new InvalidDataException("Unknown replay file format");
        var result=new ReplayRecordingHeader {Format=reader.ReadByte()};reader.ReadByte();reader.ReadByte();
        if(result.Format<1||result.Format>CurrentFormat)throw new InvalidDataException("Unsupported replay format version: "+result.Format);
        result.MapType=reader.ReadByte();result.MapPath=ReadString(reader);result.RecordedTicks=reader.ReadInt64();
        if(result.RecordedTicks<DateTime.MinValue.Ticks||result.RecordedTicks>DateTime.MaxValue.Ticks)throw new InvalidDataException("Invalid replay recording date");
        result.MarkerOffset=reader.BaseStream.Position;int marker=reader.ReadInt32();result.Complete=result.Format<6||marker==CompletedMarker;
        if(result.Format>=6&&marker!=0&&marker!=CompletedMarker)
        {
            if(!allowDamagedMarker)throw new InvalidDataException("Invalid recording completion marker");
            result.DamagedCompletionMarker=true;
        }
        result.DurationOffset=reader.BaseStream.Position;result.Duration=reader.ReadDouble();result.CountOffset=reader.BaseStream.Position;result.EventCount=reader.ReadInt64();
        if(!ValidDuration(result.Duration)||result.EventCount<0)
        {
            // These fields are written before the commit marker. A crash can
            // tear either write; recovery can reconstruct them from CRC-checked
            // event frames, but completed files must still pass strict checks.
            if(requireComplete||result.Format<6||result.Complete)
                throw new InvalidDataException("Invalid replay duration or count (maximum duration is seven days)");
            result.Duration=0;result.EventCount=0;
        }
        int count=reader.ReadUInt16();if(count>MaximumResources)throw new InvalidDataException("Too many replay resources");
        result.Resources=new string[count];for(int n=0;n<count;n++){result.Resources[n]=ReadString(reader);if(reader.BaseStream.Position-start>MaximumHeaderBytes)throw new InvalidDataException("Replay header exceeds limit");}
        if(result.Format>=6)
        {
            int length=reader.ReadInt32();uint expected=reader.ReadUInt32();if(length<0||length>MaximumMetadataBytes)throw new InvalidDataException("Replay metadata exceeds limit");
            byte[] metadata=reader.ReadBytes(length);if(metadata.Length!=length)throw new EndOfStreamException("Incomplete replay metadata");
            if(ReplayRecordTransport.Crc32(metadata,0,metadata.Length)!=expected)throw new InvalidDataException("Replay metadata checksum mismatch");
            result.Metadata=utf8.GetString(metadata);
            if(reader.ReadUInt32()!=result.ImmutableChecksum())throw new InvalidDataException("Replay header checksum mismatch");
        }
        result.DataOffset=reader.BaseStream.Position;
        if(result.DataOffset-start>MaximumHeaderBytes)throw new InvalidDataException("Replay header exceeds limit");
        if(requireComplete&&!result.Complete)throw new InvalidDataException("This recording was interrupted. Use Library → Recover recordings to create a playable copy.");
        return result;
    }
    public void Write(BinaryWriter writer,bool complete=false)
    {
        if(!ValidDuration(Duration)||EventCount<0||RecordedTicks<DateTime.MinValue.Ticks||RecordedTicks>DateTime.MaxValue.Ticks)throw new InvalidDataException("Invalid recording duration, count or date");
        if(!fingerprintsAdded&&(GameAssemblyPath!=null||RecorderAssemblyPath!=null))
        {Metadata=ReplayRecordingProvenance.AddFingerprints(Metadata,GameAssemblyPath,RecorderAssemblyPath);fingerprintsAdded=true;}
        if((Format<6||Format>CurrentFormat)||Resources==null||Resources.Length>MaximumResources||utf8.GetByteCount(MapPath??"")>MaximumStringBytes)throw new InvalidDataException("Invalid recording header");
        byte[] metadata=utf8.GetBytes(Metadata??"{}");if(metadata.Length>MaximumMetadataBytes)throw new InvalidDataException("Recording metadata exceeds limit");
        long size=metadata.Length+utf8.GetByteCount(MapPath??"")+64;
        foreach(string resource in Resources){int bytes=utf8.GetByteCount(resource??"");if(bytes>MaximumStringBytes)throw new InvalidDataException("Recording resource name exceeds limit");size+=bytes+5;}
        if(size>MaximumHeaderBytes)throw new InvalidDataException("Recording header exceeds limit");
        writer.Write(12549);writer.Write(Format);writer.Write((byte)0);writer.Write((byte)0);writer.Write(MapType);writer.Write(MapPath??"");writer.Write(RecordedTicks);
        MarkerOffset=writer.BaseStream.Position;writer.Write(complete?CompletedMarker:0);DurationOffset=writer.BaseStream.Position;writer.Write(Duration);CountOffset=writer.BaseStream.Position;writer.Write(EventCount);
        writer.Write((ushort)Resources.Length);foreach(string resource in Resources){if(utf8.GetByteCount(resource??"")>MaximumStringBytes)throw new InvalidDataException("Recording resource name exceeds limit");writer.Write(resource??"");}
        writer.Write(metadata.Length);writer.Write(ReplayRecordTransport.Crc32(metadata,0,metadata.Length));writer.Write(metadata);writer.Write(ImmutableChecksum());DataOffset=writer.BaseStream.Position;
    }
    private uint ImmutableChecksum()
    {
        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,utf8,true))
        {writer.Write(Format);writer.Write(MapType);writer.Write(MapPath??"");writer.Write(RecordedTicks);writer.Write((ushort)Resources.Length);foreach(string resource in Resources)writer.Write(resource??"");writer.Write(Metadata??"{}");writer.Flush();return ReplayRecordTransport.Crc32(stream.GetBuffer(),0,(int)stream.Length);}
    }
    public void FinalizeHeader(BinaryWriter writer,double duration,long count,bool complete=true)
    {
        if(!ValidDuration(duration)||count<0)throw new InvalidDataException("Invalid recording completion (maximum duration is seven days)");
        long end=writer.BaseStream.Position;
        writer.BaseStream.Position=DurationOffset;writer.Write(duration);writer.BaseStream.Position=CountOffset;writer.Write(count);writer.Flush();
        // Commit last. An interrupted header update remains explicitly recoverable.
        writer.BaseStream.Position=MarkerOffset;writer.Write(complete?CompletedMarker:0);writer.Flush();writer.BaseStream.Position=end;
    }
    private static bool ValidDuration(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value)&&value>=0&&value<=MaximumDurationSeconds;
}
