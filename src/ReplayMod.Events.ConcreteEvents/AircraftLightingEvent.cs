using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ReplayMod.Core;
using ReplayMod.Data;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.AircraftLighting)]
public sealed class AircraftLightingEvent:IReplayEvent
{
    public uint unitId;
    public string[] keys=Array.Empty<string>();
    public byte[] flags=Array.Empty<byte>(),controls=Array.Empty<byte>();
    public double Time{get;set;}
    public EventType EventType=>EventType.AircraftLighting;
    public void Execute(object worker=null){if(worker is UnitController controller)controller.Execute(this);}
    public void Write(BinaryWriter writer)
    {
        Validate();writer.Write(Time);writer.Write(unitId);writer.Write((ushort)keys.Length);
        for(int i=0;i<keys.Length;i++){var bytes=Encoding.UTF8.GetBytes(keys[i]);writer.Write((ushort)bytes.Length);writer.Write(bytes);writer.Write(flags[i]);writer.Write(controls[i]);}
    }
    public void Read(BinaryReader reader)
    {
        Time=reader.ReadDouble();unitId=reader.ReadUInt32();int count=reader.ReadUInt16();if(count>AircraftLightingPolicy.MaxLights)throw new InvalidDataException("Aircraft light count exceeds limit");
        keys=new string[count];flags=new byte[count];controls=new byte[count];
        for(int i=0;i<count;i++)
        {int length=reader.ReadUInt16();if(length>AircraftLightingPolicy.MaxKeyBytes)throw new InvalidDataException("Aircraft light address exceeds limit");var bytes=reader.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();keys[i]=new UTF8Encoding(false,true).GetString(bytes);flags[i]=reader.ReadByte();controls[i]=reader.ReadByte();}
        Validate();
    }
    public bool HasValidPayload()
    {
        if(unitId==0||double.IsNaN(Time)||double.IsInfinity(Time)||Time<0||keys==null||flags==null||controls==null||keys.Length>AircraftLightingPolicy.MaxLights||keys.Length!=flags.Length||keys.Length!=controls.Length)return false;
        var unique=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<keys.Length;i++)if(string.IsNullOrEmpty(keys[i])||Encoding.UTF8.GetByteCount(keys[i])>AircraftLightingPolicy.MaxKeyBytes||!unique.Add(keys[i])||(flags[i]&~7)!=0||controls[i]>3)return false;
        return true;
    }
    private void Validate(){if(!HasValidPayload())throw new InvalidDataException("Invalid aircraft lighting snapshot");}
    public void Reset(){Time=0;unitId=0;keys=Array.Empty<string>();flags=controls=Array.Empty<byte>();}
}
