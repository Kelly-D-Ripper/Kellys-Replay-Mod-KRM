using System;
using System.IO;
using System.Text;
using ReplayMod.Events;

namespace ReplayMod.Core;

// Formats 6-8 wrap semantic event codecs. File offsets always point
// to a frame header, so seeks and checkpoints use the same boundaries as scans.
public static class ReplayRecordTransport
{
    public const int MaximumEventBytes=8*1024*1024;
    public const int FrameHeaderBytes=8;
    private static readonly uint[] crcTable=CreateCrcTable();
    public static uint Crc32(byte[] bytes,int offset,int count)
    {
        uint crc=uint.MaxValue;
        for(int n=offset;n<offset+count;n++)crc=crcTable[(crc^bytes[n])&255]^(crc>>8);
        return ~crc;
    }
    private static uint[] CreateCrcTable()
    {
        var result=new uint[256];for(uint n=0;n<256;n++){uint value=n;for(int bit=0;bit<8;bit++)value=(value&1)!=0?0xedb88320u^(value>>1):value>>1;result[n]=value;}return result;
    }
    public static byte[] Frame(byte[] body)
    {
        if(body==null||body.Length<9||body.Length>MaximumEventBytes)throw new InvalidDataException("Replay event frame size is outside its limit");
        var result=new byte[body.Length+FrameHeaderBytes];
        using(var stream=new MemoryStream(result,true))using(var writer=new BinaryWriter(stream)){writer.Write(body.Length);writer.Write(Crc32(body,0,body.Length));writer.Write(body);}
        return result;
    }
    public static byte[] ReadBody(BinaryReader reader)
    {
        int length=reader.ReadInt32();uint expected=reader.ReadUInt32();
        if(length<9||length>MaximumEventBytes)throw new InvalidDataException("Replay event frame size is outside its limit");
        byte[] body=reader.ReadBytes(length);if(body.Length!=length)throw new EndOfStreamException("Replay ends inside an event frame");
        if(Crc32(body,0,body.Length)!=expected)throw new InvalidDataException("Replay event checksum mismatch");
        ValidateEnvelope(body);return body;
    }
    public static double ValidateEnvelope(byte[] body)
    {
        if(body==null||body.Length<9||body.Length>MaximumEventBytes||body[0]==0||!Enum.IsDefined(typeof(EventType),(int)body[0]))throw new InvalidDataException("Unknown or incomplete replay event frame");
        double time=BitConverter.ToDouble(body,1);
        if(double.IsNaN(time)||double.IsInfinity(time)||time<0||time>ReplayRecordingHeader.MaximumDurationSeconds)throw new InvalidDataException("Replay event has an invalid timestamp (maximum duration is seven days)");
        return time;
    }
    public static IReplayEvent ReadEvent(BinaryReader reader,byte format)
    {
        if(format<6)
        {
            var type=(EventType)reader.ReadByte();
            if(type==EventType.EngineCoreState)throw new InvalidDataException("Engine core state requires replay format 11");
            if(type==EventType.MissileMountState)throw new InvalidDataException("Missile mount state requires replay format 12");
            return ReplayEventFactory.CreateAndRead(type,reader);
        }
        return DecodeBody(ReadBody(reader),format);
    }
    public static IReplayEvent DecodeBody(byte[] body,byte? format=null)
    {
        ValidateEnvelope(body);IReplayEvent result=null;
        using(var stream=new MemoryStream(body,false))using(var input=new ReplayFrameReader(stream,Encoding.UTF8))
        {
            try
            {
                var kind=(EventType)input.ReadByte();double time=input.ReadDouble();
                result=ReplayEventFactory.CreateAndRead(kind,input);
                if(format.HasValue&&format.Value<11&&result is ReplayMod.Events.ConcreteEvents.EngineCoreStateEvent)
                    throw new InvalidDataException("Engine core state requires replay format 11");
                if(format.HasValue&&format.Value<12&&result is ReplayMod.Events.ConcreteEvents.MissileMountStateEvent)
                    throw new InvalidDataException("Missile mount state requires replay format 12");
                if(result.Time!=time)throw new InvalidDataException("Replay event timestamp does not match its transport frame");
                if(stream.Position!=stream.Length)throw new InvalidDataException("Replay event frame contains trailing data");
                return result;
            }
            catch{if(result!=null)ReplayEventFactory.Return(result);throw;}
        }
    }
    public static byte[] EncodeEvent(IReplayEvent value)
    {
        using(var stream=new LimitedEventStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
        {writer.Write((byte)value.EventType);writer.Write(value.Time);value.Write(writer);writer.Flush();byte[] body=stream.ToArray();ValidateEnvelope(body);return Frame(body);}
    }
    public static byte[] FrameLegacyEvent(byte[] raw)
    {
        if(raw==null||raw.Length==0||raw.Length>MaximumEventBytes-8)throw new InvalidDataException("Invalid semantic event snapshot size");
        using(var stream=new MemoryStream(raw,false))using(var reader=new ReplayFrameReader(stream,Encoding.UTF8))
        {
            var value=ReplayEventFactory.CreateAndRead((EventType)reader.ReadByte(),reader);
            try
            {
                if(stream.Position!=stream.Length)throw new InvalidDataException("Semantic event snapshot contains trailing data");
                var body=new byte[raw.Length+8];body[0]=raw[0];Array.Copy(BitConverter.GetBytes(value.Time),0,body,1,8);Buffer.BlockCopy(raw,1,body,9,raw.Length-1);
                ValidateEnvelope(body);return Frame(body);
            }
            finally{ReplayEventFactory.Return(value);}
        }
    }
    private sealed class LimitedEventStream:MemoryStream
    {
        private void Require(int count){if(count<0||Position+count>MaximumEventBytes)throw new InvalidDataException("Replay event exceeds the frame limit");}
        public override void Write(byte[] buffer,int offset,int count){Require(count);base.Write(buffer,offset,count);}
        public override void WriteByte(byte value){Require(1);base.WriteByte(value);}
    }
}
