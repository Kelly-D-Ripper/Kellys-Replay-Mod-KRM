using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;
using UnityEngine;

public static class SeekStressProbe
{
    struct Pose {internal Vector3 position;internal Quaternion rotation;internal float[] coreRpm;}
    static Dictionary<uint,Pose> Snapshot(Player player)=>player.Actors().Where(p=>p.Value is Aircraft||p.Value is Ship).ToDictionary(p=>p.Key,p=>new Pose{position=p.Value.transform.position,rotation=p.Value.transform.rotation,coreRpm=p.Value is Aircraft a&&a.engineStates!=null?a.engineStates.Select(e=>e==null?0:e.GetRPM()).ToArray():Array.Empty<float>()});
    static Dictionary<string,int> MaterialCounts()=>Resources.FindObjectsOfTypeAll<Material>().Where(m=>m!=null).GroupBy(m=>m.name).ToDictionary(g=>g.Key,g=>g.Count());
    static void LogMaterialGrowth(Dictionary<string,int> initial,int n,Action<string> log)
    {
        var current=MaterialCounts();
        log("STRESS MATERIAL GROWTH "+n+" "+string.Join("; ",current.Select(p=>new{p.Key,Delta=p.Value-(initial.TryGetValue(p.Key,out int before)?before:0)}).Where(p=>p.Delta>0).OrderByDescending(p=>p.Delta).Take(12).Select(p=>p.Key+"="+p.Delta)));
    }
    static void LogMaterialOwners(HashSet<int> initial,Action<string> log)
    {
        var added=Resources.FindObjectsOfTypeAll<Material>().Where(m=>m!=null&&!initial.Contains(m.GetInstanceID())).ToArray();
        var ids=new HashSet<int>(added.Select(m=>m.GetInstanceID()));var renderers=new Dictionary<int,List<string>>();
        foreach(var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
        {
            if(renderer==null)continue;
            foreach(var material in renderer.sharedMaterials)
            {
                if(material==null||!ids.Contains(material.GetInstanceID()))continue;
                if(!renderers.TryGetValue(material.GetInstanceID(),out var owners))renderers[material.GetInstanceID()]=owners=new List<string>();
                owners.Add(renderer.gameObject.name+"@"+renderer.gameObject.scene.name+"/loaded="+renderer.gameObject.scene.isLoaded);
            }
        }
        var claimed=new HashSet<int>();var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var materialOwners=Resources.FindObjectsOfTypeAll<MaterialNavOwnerProbe>().Where(o=>o!=null).ToArray();
        var cleanupField=typeof(LiveryBehaviour).GetField("materialCleanup",flags);
        var setField=typeof(MaterialCleanup).GetField("needCleanup",flags);
        foreach(var livery in Resources.FindObjectsOfTypeAll<LiveryBehaviour>())
        {
            if(livery==null)continue;var cleanup=cleanupField?.GetValue(livery);if(cleanup==null)continue;
            if(setField?.GetValue(cleanup) is IEnumerable<Material> set)foreach(var m in set)if(m!=null&&ids.Contains(m.GetInstanceID()))claimed.Add(m.GetInstanceID());
        }
        foreach(var group in added.GroupBy(m=>m.name).OrderByDescending(g=>g.Count()).Take(30))
        {
            int linked=group.Count(m=>renderers.ContainsKey(m.GetInstanceID()));
            int tracked=group.Count(m=>claimed.Contains(m.GetInstanceID()));
            int damageCreated=group.Count(m=>MaterialDamageTraceProbe.Created.ContainsKey(m.GetInstanceID()));
            int navCreated=group.Count(m=>MaterialNavTraceProbe.Materials.Contains(m.GetInstanceID()));
            int meshCreated=group.Count(m=>MaterialMeshTraceProbe.Materials.Contains(m.GetInstanceID()));
            int navOrphans=group.Count(m=>MaterialNavTraceProbe.Materials.Contains(m.GetInstanceID())&&!renderers.ContainsKey(m.GetInstanceID()));
            int meshOrphans=group.Count(m=>MaterialMeshTraceProbe.Materials.Contains(m.GetInstanceID())&&!renderers.ContainsKey(m.GetInstanceID()));
            var orphanOwners=group.Where(m=>MaterialNavTraceProbe.Materials.Contains(m.GetInstanceID())&&!renderers.ContainsKey(m.GetInstanceID()))
                .SelectMany(m=>materialOwners.Where(o=>o.Owns(m)).Select(o=>o.gameObject.name+"@"+o.gameObject.scene.name+"/loaded="+o.gameObject.scene.isLoaded+"/active="+o.gameObject.activeInHierarchy)).Distinct().Take(4);
            var orphanReasons=group.Where(m=>MaterialNavTraceProbe.Materials.Contains(m.GetInstanceID())&&!renderers.ContainsKey(m.GetInstanceID()))
                .Select(m=>MaterialNavTraceProbe.Reasons.TryGetValue(m.GetInstanceID(),out var reason)?reason:"unknown").Distinct().Take(4);
            var examples=group.Where(m=>renderers.ContainsKey(m.GetInstanceID())).SelectMany(m=>renderers[m.GetInstanceID()]).Distinct().Take(4);
            log("STRESS MATERIAL OWNERS "+group.Key+" added="+group.Count()+" rendererLinked="+linked+" liveryTracked="+tracked+" damageCreated="+damageCreated+" nav="+navCreated+" navOrphans="+navOrphans+" mesh="+meshCreated+" meshOrphans="+meshOrphans+" examples="+string.Join(",",examples)+" owner="+string.Join(",",orphanOwners)+" origin="+string.Join(",",orphanReasons));
        }
    }
    public static async UniTask Run(Player player,Action<string> log)
    {
        var targets=Environment.GetEnvironmentVariable("REPLAY_SEEK_STRESS_FILE")!=null
            ? new[]{player.replayDuration*.05,player.replayDuration*.35,player.replayDuration*.65,player.replayDuration*.95}
            : new[]{2d,25d,50d,75d};
        string fractions=Environment.GetEnvironmentVariable("REPLAY_SEEK_STRESS_FRACTIONS");
        if(!string.IsNullOrWhiteSpace(fractions))
        {
            targets=fractions.Split(',').Select(x=>double.Parse(x,System.Globalization.CultureInfo.InvariantCulture)*player.replayDuration).ToArray();
            if(targets.Length!=4||targets.Any(t=>t<0||t>player.replayDuration))throw new Exception("Expected four valid seek fractions");
        }
        var reference=new Dictionary<double,Dictionary<uint,Pose>>();
        foreach(var time in targets)
        {
            await player.TimelineJump(time,0,false);await UniTask.NextFrame();await UniTask.NextFrame();
            if(player.IsSeeking||!player.IsPaused||!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception("Baseline seek failed at "+time);
            reference[time]=Snapshot(player);if(reference[time].Count==0)throw new Exception("No aircraft or ships at "+time);
            log("STRESS baseline time="+time+" actors="+reference[time].Count);
        }
        if(Environment.GetEnvironmentVariable("REPLAY_TEST_NAV_CLEANUP")=="1")
        {MaterialNavTraceProbe.VerifySharedMaterialGuard();log("STRESS TEST NAV shared-material guard PASS");}
        if(Environment.GetEnvironmentVariable("REPLAY_VALIDATE_NAV_CLEANUP")=="1")
        {MaterialNavTraceProbe.VerifyProductionGuards();log("STRESS PRODUCTION NAV shared, detached and inactive guards PASS");}
        var watch=Stopwatch.StartNew();long initialMemory=GC.GetTotalMemory(true);float maxDistance=0,maxAngle=0,maxRpmDifference=0;
        int cleanupEvery=int.TryParse(Environment.GetEnvironmentVariable("REPLAY_STRESS_CLEANUP_EVERY"),out var interval)?Math.Max(0,interval):0;
        int startMaterials=Resources.FindObjectsOfTypeAll<Material>().Length;double cleanupSeconds=0,maxCleanup=0;
        bool traceMaterials=Environment.GetEnvironmentVariable("REPLAY_STRESS_MATERIAL_TRACE")=="1";
        bool checkCores=Environment.GetEnvironmentVariable("REPLAY_STRESS_IGNORE_CORES")!="1";
        var initialMaterials=traceMaterials?MaterialCounts():null;
        bool detailed=Environment.GetEnvironmentVariable("REPLAY_STRESS_MATERIAL_DETAILED")=="1";
        var initialMaterialIds=detailed?new HashSet<int>(Resources.FindObjectsOfTypeAll<Material>().Where(m=>m!=null).Select(m=>m.GetInstanceID())):null;
        int iterations=int.TryParse(Environment.GetEnvironmentVariable("REPLAY_STRESS_ITERATIONS"),out int requested)?Math.Max(8,Math.Min(64,requested)):64;
        for(int i=0;i<iterations;i++)
        {
            double time=targets[(i*3+1)%targets.Length];
            await player.TimelineJump(time,0,false);await UniTask.NextFrame();await UniTask.NextFrame();
            if(player.IsSeeking||!player.IsPaused||Math.Abs(player.currentVirtualTime-time)>.001||!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception("Seek failed on iteration "+i+": "+player.PlaybackFailure);
            if(Environment.GetEnvironmentVariable("REPLAY_TEST_NAV_CLEANUP")=="1"||Environment.GetEnvironmentVariable("REPLAY_VALIDATE_NAV_CLEANUP")=="1")
                foreach(var manager in UnityEngine.Object.FindObjectsOfType<NavLights>())
                    if(manager!=null&&manager.navLights!=null)foreach(var lamp in manager.navLights)
                        if(lamp?.renderer!=null&&lamp.renderer.gameObject.activeInHierarchy&&(lamp.baseMaterial==null||lamp.renderer.sharedMaterial==null))throw new Exception("Active navigation lamp lost its material after seek "+i);
            var actual=Snapshot(player);var expected=reference[time];
            if(actual.Count!=expected.Count||expected.Keys.Any(id=>!actual.ContainsKey(id)))throw new Exception("Actor set changed on iteration "+i+" at "+time+" expected="+expected.Count+" actual="+actual.Count);
            foreach(var pair in expected)
            {
                float distance=Vector3.Distance(pair.Value.position,actual[pair.Key].position),angle=Quaternion.Angle(pair.Value.rotation,actual[pair.Key].rotation);
                maxDistance=Math.Max(maxDistance,distance);maxAngle=Math.Max(maxAngle,angle);
                if(distance>1||angle>1)throw new Exception("Actor "+pair.Key+" diverged on iteration "+i+" at "+time+": "+distance+"m / "+angle+"deg");
                if(checkCores&&pair.Value.coreRpm.Length!=actual[pair.Key].coreRpm.Length)throw new Exception("Core engine count changed on iteration "+i);
                for(int n=0;checkCores&&n<pair.Value.coreRpm.Length;n++)
                {
                    float difference=Math.Abs(pair.Value.coreRpm[n]-actual[pair.Key].coreRpm[n]);
                    if(float.IsNaN(difference)||float.IsInfinity(difference)||difference>150)throw new Exception("Actor "+pair.Key+" core "+n+" diverged after seek: "+difference+" RPM");
                    maxRpmDifference=Math.Max(maxRpmDifference,difference);
                }
            }
            if(cleanupEvery>0&&(i+1)%cleanupEvery==0)
            {
                var cleanupWatch=Stopwatch.StartNew();var operation=Resources.UnloadUnusedAssets();while(!operation.isDone)await UniTask.NextFrame();cleanupWatch.Stop();
                cleanupSeconds+=cleanupWatch.Elapsed.TotalSeconds;maxCleanup=Math.Max(maxCleanup,cleanupWatch.Elapsed.TotalSeconds);
            }
            if(Environment.GetEnvironmentVariable("REPLAY_STRESS_TRACE_EACH")=="1")LogMaterialGrowth(initialMaterials,i+1,log);
            if((i+1)%16==0){log("STRESS "+(i+1)+"/"+iterations+" completed; checkpoints="+player.SeekCheckpointCount+" managedMB="+(GC.GetTotalMemory(false)/1048576));if(traceMaterials)LogMaterialGrowth(initialMaterials,i+1,log);}
        }
        long finalMemory=GC.GetTotalMemory(true);
        log("STRESS PASS "+iterations+" alternating seeks, actor identities and poses stable; coreRPMChecked="+checkCores+" max "+maxDistance.ToString("F3")+"m/"+maxAngle.ToString("F3")+"deg/"+maxRpmDifference.ToString("F1")+" RPM; elapsed="+watch.Elapsed.TotalSeconds.ToString("F1")+"s; managed memory "+(initialMemory/1048576)+"→"+(finalMemory/1048576)+" MiB");
        log("STRESS MATERIALS "+startMaterials+"→"+Resources.FindObjectsOfTypeAll<Material>().Length+" cleanupEvery="+cleanupEvery+" total="+cleanupSeconds.ToString("F2")+"s max="+maxCleanup.ToString("F2")+"s");
        if(Environment.GetEnvironmentVariable("REPLAY_TEST_NAV_CLEANUP")=="1")log("STRESS TEST NAV CLEANUP registered="+MaterialNavTraceProbe.OwnersAdded+" unchanged="+MaterialNavTraceProbe.ExistingLeftAlone+" destroyed="+MaterialNavTraceProbe.Destroyed);
        if(detailed)LogMaterialOwners(initialMaterialIds,log);
        if(Environment.GetEnvironmentVariable("REPLAY_STRESS_CLEANUP_FINAL")=="1")
        {
            var cleanupWatch=Stopwatch.StartNew();var operation=Resources.UnloadUnusedAssets();while(!operation.isDone)await UniTask.NextFrame();cleanupWatch.Stop();
            log("STRESS FINAL UNLOAD materials="+Resources.FindObjectsOfTypeAll<Material>().Length+" seconds="+cleanupWatch.Elapsed.TotalSeconds.ToString("F2"));
            if(traceMaterials)LogMaterialGrowth(initialMaterials,iterations+1,log);
            if(detailed)LogMaterialOwners(initialMaterialIds,log);
        }
        if(ProductionContinuationProbe.Enabled){if(ProductionContinuationProbe.ReplayWrapped==0||ProductionContinuationProbe.LiveWrapped!=0)throw new Exception("Production continuation scope was not exercised safely");log("STRESS PRODUCTION CONTINUATIONS "+ProductionContinuationProbe.Stats);}
    }
}
