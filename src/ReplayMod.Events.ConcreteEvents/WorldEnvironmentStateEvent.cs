using System.IO;
using ReplayMod.Core;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.WorldEnvironmentState)]
public sealed class WorldEnvironmentStateEvent : IReplayEvent
{
    public float timeOfDay, timeFactor, conditions, cloudHeight, windSpeed, windTurbulence, windHeading, windMainHeading, windRandomArc;
    public Vector3 windVelocity, windOffset;
    public float? moonPhase;
    public bool hasCloudPhase;
    public Vector2 cloudDisplacement;
    public double Time { get; set; }
    public EventType EventType => EventType.WorldEnvironmentState;
    public void Execute(object worker = null) { if (worker is UnitController controller) controller.Execute(this); }
    public void Write(BinaryWriter writer)
    {
        if (!WorldStateSnapshot.From(this).Valid()) throw new InvalidDataException("Invalid replay environment state");
        writer.Write(Time); writer.Write(timeOfDay); writer.Write(timeFactor); writer.Write(conditions); writer.Write(cloudHeight);
        writer.Write(windSpeed); writer.Write(windTurbulence); writer.Write(windHeading); writer.Write(windMainHeading); writer.Write(windRandomArc);
        WriteVector(writer, windVelocity); WriteVector(writer, windOffset);
        writer.Write(moonPhase.HasValue); if (moonPhase.HasValue) writer.Write(moonPhase.Value);
        writer.Write(hasCloudPhase); writer.Write(cloudDisplacement.x); writer.Write(cloudDisplacement.y);
    }
    public void Read(BinaryReader reader)
    {
        Time = reader.ReadDouble(); timeOfDay = reader.ReadSingle(); timeFactor = reader.ReadSingle(); conditions = reader.ReadSingle(); cloudHeight = reader.ReadSingle();
        windSpeed = reader.ReadSingle(); windTurbulence = reader.ReadSingle(); windHeading = reader.ReadSingle(); windMainHeading = reader.ReadSingle(); windRandomArc = reader.ReadSingle();
        windVelocity = ReadVector(reader); windOffset = ReadVector(reader);
        moonPhase = reader.ReadBoolean() ? reader.ReadSingle() : (float?)null;
        hasCloudPhase = reader.ReadBoolean(); cloudDisplacement = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        if (!WorldStateSnapshot.From(this).Valid()) throw new InvalidDataException("Invalid replay environment state");
    }
    private static void WriteVector(BinaryWriter writer, Vector3 value) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); }
    private static Vector3 ReadVector(BinaryReader reader) => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    public void Reset()
    {
        Time = 0; timeOfDay = timeFactor = conditions = cloudHeight = windSpeed = windTurbulence = windHeading = windMainHeading = windRandomArc = 0;
        windVelocity = windOffset = default(Vector3); moonPhase = null; hasCloudPhase = false; cloudDisplacement = default(Vector2);
    }
}
