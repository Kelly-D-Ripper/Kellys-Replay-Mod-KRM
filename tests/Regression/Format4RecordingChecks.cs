using System.Collections.Concurrent;
using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

internal static class Format4RecordingChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("format 4 recording survives 204 alternating checkpoint restores without changing file bytes",()=>
        {
            string path=Path.Combine(Path.GetTempPath(),"replay-v4-"+Guid.NewGuid().ToString("N")+".replay");
            try
            {
                using var events=new BlockingCollection<IReplayEvent>();
                events.Add(new SpawnEvent {unitId=1,ownerId=1,jsonKey="test-aircraft",factionName="blue",rotation=new Quaternion {w=1}});
                events.Add(new AircraftVisualLayoutEvent {unitId=1,layoutId=7,channels=new[]{"gear:0","sweep:0"}});
                for(int n=0;n<=3600;n++)
                {
                    double time=n*.05;
                    events.Add(new UpdatePositionEvent {unitId=1,Time=time,position=new GlobalPosition(n,0,0),rotation=new Quaternion {w=1},velocity=new Vector3(20,0,0)});
                    events.Add(new AircraftVisualStateEvent {unitId=1,layoutId=7,Time=time,blendSeconds=.05f,gearDeployed=n%200<100,poses=new[]{
                        new AircraftVisualPose {position=new Vector3(n%100,0,0),rotation=new Quaternion {w=1}},
                        new AircraftVisualPose {position=new Vector3(0,n%200,0),rotation=new Quaternion {w=1}}}});
                    if(n%20==0)
                    {
                        events.Add(new FuelTankStatusEvent {unitId=1,partId=2,Time=time,onFire=n>=1200,flameActive=n>=1200&&n<2400,suppressBurst=true});
                        events.Add(new UnitStatusEvent {unitId=1,Time=time,disabled=n>=3000,state=(byte)(n>=3000?1:0)});
                    }
                    if(n==1800)
                    {
                        events.Add(new SpawnEvent {unitId=2,ownerId=1,jsonKey="test-missile",factionName="blue",rotation=new Quaternion {w=1},Time=time});
                        events.Add(new MissileDetonationEvent {unitId=2,Time=time,position=new GlobalPosition(1800,0,0),normal=new Vector3(0,1,0),armed=true,hitArmor=true});
                        events.Add(new DespawnEvent {unitId=2,Time=time});
                    }
                }
                long count=events.Count;events.CompleteAdding();
                using(var file=File.Create(path))using(var writer=new BinaryWriter(file))
                {
                    writer.Write(12549);writer.Write((byte)4);writer.Write((byte)0);writer.Write((byte)0);
                    writer.Write((byte)0);writer.Write("generated-test-map");writer.Write(DateTime.UtcNow.Ticks);writer.Write(0);
                    writer.Write(180d);writer.Write(count);writer.Write((ushort)0);
                    if(ReplayFileWriter.Drain(events,writer)!=count)throw new Exception("recorded event count mismatch");
                }
                RealReplayAudit.AuditGenerated(path);
            }
            finally { if(File.Exists(path))File.Delete(path); }
        });
    }
}
