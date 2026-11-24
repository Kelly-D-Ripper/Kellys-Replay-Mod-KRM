using System;
using System.IO;
using System.Text;
using ReplayMod.Core;
using ReplayMod.Data;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.AircraftVisualLayout)]
public sealed class AircraftVisualLayoutEvent : IReplayEvent
{
    public uint unitId,layoutId;
    public string[] channels=Array.Empty<string>();
    public double Time {get;set;}
    public EventType EventType=>EventType.AircraftVisualLayout;
    public void Execute(object worker=null){if(worker is UnitController controller)controller.Execute(this);}
    public void Write(BinaryWriter writer)
    {
        if(channels.Length>AircraftVisualSnapshot.MaxChannels)throw new InvalidDataException("Too many aircraft visual channels");
        writer.Write(Time);writer.Write(unitId);writer.Write(layoutId);writer.Write((ushort)channels.Length);
        foreach(var channel in channels)
        {
            var bytes=Encoding.UTF8.GetBytes(channel??string.Empty);
            if(bytes.Length==0||bytes.Length>640)throw new InvalidDataException("Invalid aircraft visual channel name");
            writer.Write((ushort)bytes.Length);writer.Write(bytes);
        }
    }
    public void Read(BinaryReader reader)
    {
        Time=reader.ReadDouble();unitId=reader.ReadUInt32();layoutId=reader.ReadUInt32();int count=reader.ReadUInt16();
        if(count>AircraftVisualSnapshot.MaxChannels)throw new InvalidDataException("Too many aircraft visual channels");
        channels=new string[count];var unique=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<count;i++)
        {
            int length=reader.ReadUInt16();if(length==0||length>640)throw new InvalidDataException("Invalid aircraft visual channel name");
            byte[] bytes=reader.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();
            channels[i]=Encoding.UTF8.GetString(bytes);
            if(!unique.Add(channels[i]))throw new InvalidDataException("Duplicate aircraft visual channel");
        }
    }
    public void Reset(){Time=0;unitId=layoutId=0;channels=Array.Empty<string>();}
}
