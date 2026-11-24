using System;
using System.IO;
using System.Text;
using ReplayMod.Core;
using ReplayMod.Data;

namespace ReplayMod.Events.ConcreteEvents;

// Format 8. This stores already-resolved native display text, never account IDs.
[ReplayEvent(EventType.ActorIdentity)]
public sealed class ActorIdentityEvent:IReplayEvent
{
    private static readonly UTF8Encoding StrictUtf8=new UTF8Encoding(false,true);
    public uint unitId;
    public bool humanControlled;
    public string playerName="",callsign="";
    public double Time {get;set;}
    public EventType EventType=>EventType.ActorIdentity;
    public void Execute(object worker=null){if(worker is UnitController controller)controller.Execute(this);}
    public bool HasValidPayload()=>unitId!=0&&!double.IsNaN(Time)&&!double.IsInfinity(Time)&&Time>=0&&
        ActorIdentityText.Valid(playerName)&&ActorIdentityText.Valid(callsign)&&(humanControlled||playerName.Length==0);
    private void Validate(){if(!HasValidPayload())throw new InvalidDataException("Invalid recorded actor identity");}
    public void Write(BinaryWriter writer)
    {Validate();writer.Write(Time);writer.Write(unitId);writer.Write((byte)(humanControlled?1:0));WriteText(writer,playerName);WriteText(writer,callsign);}
    public void Read(BinaryReader reader)
    {
        Time=reader.ReadDouble();unitId=reader.ReadUInt32();byte human=reader.ReadByte();
        if(human>1)throw new InvalidDataException("Invalid recorded actor identity owner flag");humanControlled=human==1;
        playerName=ReadText(reader);callsign=ReadText(reader);Validate();
    }
    private static void WriteText(BinaryWriter writer,string value)
    {var bytes=StrictUtf8.GetBytes(value);writer.Write((ushort)bytes.Length);writer.Write(bytes);}
    private static string ReadText(BinaryReader reader)
    {
        int size=reader.ReadUInt16();if(size>ActorIdentityText.MaxBytes)throw new InvalidDataException("Recorded identity text exceeds limit");
        var bytes=reader.ReadBytes(size);if(bytes.Length!=size)throw new EndOfStreamException();
        try{return StrictUtf8.GetString(bytes);}catch(DecoderFallbackException error){throw new InvalidDataException("Recorded identity has invalid UTF-8",error);}
    }
    public void Reset(){Time=0;unitId=0;humanControlled=false;playerName=callsign="";}
}
