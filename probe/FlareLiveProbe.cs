using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class FlareLiveProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Live flare visual test");
        TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);var recorder=Recorder.Active;
        if(recorder==null||!recorder.IsRecording)throw new Exception("Flare recording unavailable");string partial=recorder.RecordingPath;
        var def=(AircraftDefinition)Encyclopedia.Lookup["CAS1"];var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
        var craft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);
        await Wait(2);
        var ejector=UnityEngine.Object.FindObjectsOfType<FlareEjector>().FirstOrDefault(e=>e.aircraft==craft&&e.flarePrefab!=null&&e.ejectionPoints!=null&&e.ejectionPoints.Length>0&&e.ejectionPoints[0].transform!=null);
        if(ejector==null)throw new Exception("No native flare ejector");
        var point=ejector.ejectionPoints[0].transform;var obj=UnityEngine.Object.Instantiate(ejector.flarePrefab,point.position,point.rotation);var flare=obj.GetComponent<IRFlare>();
        if(flare==null)throw new Exception("No native flare component");flare.LaunchFlare(craft,point,craft.rb.velocity+Vector3.down*20);
        await Wait(2);int faults=recorder.CaptureFailureCount;await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file))throw new Exception("Flare recording missing");ReplayIntegrityCheck.Check(file);
        uint id=0;double launch=-1;int samples=0;CountermeasureTimeline timeline=null;
        using(var input=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(input);
            for(long n=0;n<header.EventCount;n++)
            {
                var e=ReplayRecordTransport.ReadEvent(input,header.Format);
                if(e is CountermeasureEvent cm&&cm.kind==CountermeasureKind.Flare&&cm.phase==CountermeasurePhase.Launch&&id==0)
                {id=cm.countermeasureId;launch=cm.Time;timeline=new CountermeasureTimeline(cm,true);}
                else if(e is CountermeasureEvent sample&&id!=0&&sample.countermeasureId==id){timeline.Add(sample);if(sample.phase==CountermeasurePhase.Sample)samples++;}
                ReplayEventFactory.Return(e);
            }
        }
        if(id==0||samples<5||faults!=0)throw new Exception("Flare recording incomplete id="+id+" samples="+samples+" faults="+faults);
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||player==null)throw new Exception("Flare replay did not open");player.Pause();
        for(int n=0;n<2;n++)
        {
            await player.TimelineJump(Math.Max(0,launch-.1),0,false);await UniTask.NextFrame();
            if(GameObject.Find("Replay countermeasure "+id)!=null)throw new Exception("Flare exists before launch");
            double at=launch+1;await player.TimelineJump(at,0,false);await UniTask.NextFrame();
            var root=GameObject.Find("Replay countermeasure "+id);if(root==null||!timeline.TryAt(at,out var pose))throw new Exception("Recorded flare visual unavailable after seek");
            float distance=Vector3.Distance(root.transform.position,pose.position.ToLocalPosition());
            var native=root.GetComponentInChildren<IRFlare>(true);if(native==null||native.enabled)throw new Exception("Replay flare native guidance component missing or active");
            var lights=root.GetComponentsInChildren<Light>(true);var particles=root.GetComponentsInChildren<ParticleSystem>(true);
            int lit=lights.Count(l=>l!=null&&l.enabled&&l.gameObject.activeInHierarchy),visible=particles.Where(p=>p!=null).Sum(p=>p.particleCount);
            log("FLARE LIVE seek="+n+" distance="+distance.ToString("F2")+"m lit="+lit+" particles="+visible+" systems="+particles.Length);
            if(distance>2||lit==0||visible==0)throw new Exception("Flare pose, light or particles missing after seek");
        }
        log("FLARE LIVE PASS native launch and "+samples+" trajectory samples recorded; replay pose, light and particles restored on two seeks");
    }
}
