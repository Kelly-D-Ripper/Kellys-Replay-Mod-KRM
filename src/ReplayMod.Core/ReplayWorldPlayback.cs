using System;
using System.Collections.Generic;
using System.Globalization;
using ReplayMod.Data;
using ReplayMod.Director;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

// A single controller owns recorded world presentation. No live scene state or
// shared weather asset is changed until a recorded sample is actually displayed.
public sealed class ReplayWorldPlayback
{
    public const int MaximumHangars = 2048;
    private sealed class DoorTimeline
    {
        public uint unitId;
        public string path;
        public Unit actor;
        public Hangar hangar;
        public float firstAttemptAt=-1, nextAttemptAt;
        public bool warned;
        public readonly List<HangarDoorSnapshot> samples = new List<HangarDoorSnapshot>();
    }
    private readonly UnitController controller;
    private readonly Dictionary<string, DoorTimeline> doors = new Dictionary<string, DoorTimeline>(StringComparer.Ordinal);
    private readonly HashSet<Hangar> ownedHangars = new HashSet<Hangar>();
    private readonly List<WorldStateSnapshot> environments = new List<WorldStateSnapshot>();
    private static readonly Func<HangarDoorSnapshot,double> DoorTime = s=>s.time;
    private static readonly Func<WorldStateSnapshot,double> EnvironmentTime = s=>s.time;
    private LevelInfo level;
    private WorldStateSnapshot originalEnvironment, displayedEnvironment;
    private float lightingHour,lightingConditions;
    private bool forceLighting;
    private int weatherIndex = -1;
    private double displayedTime;
    public static ReplayWorldPlayback Current { get; private set; }
    public static bool HasRecordedEnvironment => ReplayRuntime.IsPlayback && Current?.displayedEnvironment!=null;
    private static bool CustomEnvironment => ReplayDirector.i?.Project?.environment?.enabled==true;
    public int DoorCount => doors.Count;
    public ReplayWorldPlayback(UnitController controller) { this.controller=controller; }

    public void Add(HangarDoorStateEvent e)
    {
        string key=e.unitId+":"+e.hangarPath;
        if(!doors.TryGetValue(key,out var timeline))
        {
            if(doors.Count>=MaximumHangars)return;
            doors.Add(key,timeline=new DoorTimeline {unitId=e.unitId,path=e.hangarPath});
        }
        SnapshotHistory.Insert(timeline.samples,HangarDoorSnapshot.From(e),DoorTime);
    }
    public void Add(WorldEnvironmentStateEvent e) => SnapshotHistory.Insert(environments,WorldStateSnapshot.From(e),EnvironmentTime);
    public void Render(double time)
    {
        if(!WorldStateSnapshot.Finite(time))return;
        Current=this;displayedTime=time;
        if(SnapshotHistory.Locate(environments,time,EnvironmentTime,out int a,out int b,out _))
        {
            displayedEnvironment=WorldStateSnapshot.At(environments[a],environments[b],time);
            SnapshotHistory.Trim(environments,a,8);
            BindLevel();
            if(!CustomEnvironment)ApplyEnvironment(displayedEnvironment,false);
        }
        foreach(var timeline in doors.Values)
        {
            if(!SnapshotHistory.Locate(timeline.samples,time,DoorTime,out a,out b,out _))continue;
            var actor=controller.FindActor(timeline.unitId);
            if(actor==null || !ReferenceEquals(actor,timeline.actor))
            {
                if(timeline.hangar!=null)ownedHangars.Remove(timeline.hangar);
                timeline.hangar=null;timeline.actor=actor;timeline.firstAttemptAt=-1;timeline.nextAttemptAt=0;timeline.warned=false;
            }
            if(actor==null)continue;
            if(timeline.hangar==null && Time.realtimeSinceStartup>=timeline.nextAttemptAt)
            {
                float now=Time.realtimeSinceStartup;
                if(timeline.firstAttemptAt<0)timeline.firstAttemptAt=now;
                timeline.hangar=ResolveHangar(actor,timeline.path);
                if(timeline.hangar!=null)
                {
                    ownedHangars.Add(timeline.hangar);
                    timeline.hangar.cancelMoveDoors?.Cancel();
                    if(timeline.hangar.loopSource!=null)timeline.hangar.loopSource.Stop();
                }
                else
                {
                    // Unit children and hangars can initialize after spawn.
                    // Retry slowly even while paused; a permanent failure is
                    // still reported once rather than on every replay frame.
                    timeline.nextAttemptAt=now+1f;
                    if(!timeline.warned&&now-timeline.firstAttemptAt>=5f)
                    {timeline.warned=true;Plugin.logger.LogWarning("Recorded hangar is unavailable on replay actor "+timeline.unitId+" ("+timeline.path+").");}
                }
            }
            if(timeline.hangar==null)continue;
            ApplyDoor(timeline.hangar,HangarDoorSnapshot.At(timeline.samples[a],timeline.samples[b],time));
            SnapshotHistory.Trim(timeline.samples,a,8);
        }
    }
    public void Forget(uint unitId)
    {
        var remove=new List<string>();
        foreach(var pair in doors)if(pair.Value.unitId==unitId)
        { if(pair.Value.hangar!=null)ownedHangars.Remove(pair.Value.hangar);remove.Add(pair.Key); }
        foreach(var key in remove)doors.Remove(key);
    }
    public void Clear()
    {
        // Scene reload normally destroys the old level. If it remains alive,
        // restore the exact environment captured before this controller owned it.
        if(level!=null && originalEnvironment!=null)ApplyEnvironment(originalEnvironment,true);
        doors.Clear();ownedHangars.Clear();environments.Clear();level=null;originalEnvironment=displayedEnvironment=null;weatherIndex=-1;
        if(ReferenceEquals(Current,this))Current=null;
    }
    private void BindLevel()
    {
        var current=NetworkSceneSingleton<LevelInfo>.i;
        if(current==null || ReferenceEquals(current,level))return;
        level=current;originalEnvironment=CaptureEnvironment(current,displayedTime);weatherIndex=-1;forceLighting=true;
    }
    private void ApplyEnvironment(WorldStateSnapshot state,bool force)
    {
        if(level==null || state==null)return;
        level.Networkconditions=state.conditions;level.NetworkcloudHeight=state.cloudHeight;
        level.NetworktimeOfDay=state.timeOfDay;level.timeFactor=state.timeFactor;
        level.windRandomArc=state.windRandomArc;
        bool headingChanged=level.GetWindHeading()!=state.windHeading;
        if(headingChanged)level.SetWindHeading(state.windHeading);
        level.windMainHeading=state.windMainHeading;
        if(level.windTurbulence!=state.windTurbulence)level.SetWindTurbulence(state.windTurbulence);
        if(headingChanged || level.windSpeed!=state.windSpeed)level.SetWindSpeed(state.windSpeed);
        level.NetworkwindSpeed=state.windSpeed;level.NetworkwindVelocity=state.windVelocity;level.windOffset=state.windOffset;
        level.NetworkmoonPhase=state.moonPhase;
        if(state.moonPhase.HasValue)level.SetMoonPhase(state.moonPhase.Value);
        bool updateLighting=force||forceLighting||Math.Abs(state.timeOfDay-lightingHour)>.025f||Math.Abs(state.conditions-lightingConditions)>.001f;
        if(updateLighting)
        {
            level.UpdateTimeOfDayLighting(true);level.UpdateSkybox(true);
            lightingHour=state.timeOfDay;lightingConditions=state.conditions;forceLighting=false;
        }
        ApplyCloudPhase(level.cloudLayer,state,updateLighting);
    }
    private void ApplyCloudPhase(CloudLayer cloud,WorldStateSnapshot state,bool refreshWeather)
    {
        if(cloud==null || cloud.layerMaterial==null || cloud.weatherSets==null || cloud.weatherSets.Length==0)return;
        int index=Mathf.Clamp(Mathf.FloorToInt(level.conditions*cloud.weatherSets.Length),0,cloud.weatherSets.Length-1);
        if(refreshWeather || weatherIndex!=index)
        {cloud.UpdateWeatherSets();weatherIndex=index;}
        cloud.transform.position=new Vector3(0,Datum.LocalSeaY+level.cloudHeight,0);
        if(!state.hasCloudPhase)return;
        cloud.windDisplacement=state.cloudDisplacement;
        cloud.layerMaterial.SetVector(CloudLayer.id_CloudsOriginOffset,new Vector2(Datum.originPosition.x+state.cloudDisplacement.x,Datum.originPosition.z+state.cloudDisplacement.y));
        var cookies=cloud.currentWeatherSet?.cookies;
        var camera=SceneSingleton<CameraStateManager>.i;
        if(cookies==null || cookies.Length==0 || cookies[0]==null || camera==null)return;
        int slice=Mathf.Clamp(Mathf.FloorToInt((camera.transform.position.y-(cloud.transform.position.y+200))*cookies.Length/200),0,cookies.Length-1);
        if(cookies[slice]!=null)level.SetCookie(cookies[slice],cookies[0].width*cloud.densityMapScale*.75f,state.cloudDisplacement);
    }
    public static bool OwnsHangar(Hangar hangar) => ReplayRuntime.IsPlayback && hangar!=null && Current!=null && Current.ownedHangars.Contains(hangar);
    public static bool OwnsEnvironment(LevelInfo candidate) => HasRecordedEnvironment && candidate!=null && ReferenceEquals(Current.level,candidate);
    public static void RefreshNativeWind(LevelInfo candidate)
    {if(OwnsEnvironment(candidate))candidate.windOffset=Current.displayedEnvironment.windOffset;}
    public static void PrepareNativeCloud(CloudLayer cloud)
    {
        if(!HasRecordedEnvironment || CustomEnvironment || !ReferenceEquals(Current.level?.cloudLayer,cloud) || !Current.displayedEnvironment.hasCloudPhase)return;
        cloud.windDisplacement=Current.displayedEnvironment.cloudDisplacement;
    }
    public static void RefreshNativeCloud(CloudLayer cloud)
    {
        if(!HasRecordedEnvironment || CustomEnvironment || !ReferenceEquals(Current.level?.cloudLayer,cloud))return;
        Current.ApplyCloudPhase(cloud,Current.displayedEnvironment,false);
    }
    public static bool RestoreRecordedEnvironment(LevelInfo candidate)
    {
        if(!OwnsEnvironment(candidate))return false;
        Current.ApplyEnvironment(Current.displayedEnvironment,true);return true;
    }
    public static WorldStateSnapshot CaptureEnvironment(LevelInfo source,double time)
    {
        var cloud=source.cloudLayer;
        return new WorldStateSnapshot {time=time,timeOfDay=source.timeOfDay,timeFactor=source.timeFactor,conditions=source.conditions,cloudHeight=source.cloudHeight,
            windSpeed=source.windSpeed,windTurbulence=source.windTurbulence,windHeading=source.GetWindHeading(),windMainHeading=source.windMainHeading,windRandomArc=source.windRandomArc,
            windVelocity=source.windVelocity,windOffset=source.windOffset,moonPhase=source.moonPhase,
            hasCloudPhase=cloud!=null && cloud.layerMaterial!=null,cloudDisplacement=cloud!=null?cloud.windDisplacement:default(Vector2)};
    }
    public static HangarDoorSnapshot CaptureDoor(Hangar source,double time) => new HangarDoorSnapshot {time=time,openAmount=Mathf.Clamp01(source.doorState.openAmount),
        currentSpeed=source.doorCurrentSpeed,opening=source.doorState.opening,available=source.available,selfDisabled=source.selfDisabled};
    private static void ApplyDoor(Hangar hangar,HangarDoorSnapshot state)
    {
        hangar.NetworkdoorState=new Hangar.DoorState {opening=state.opening,openAmount=state.openAmount};
        hangar.Networkavailable=state.available;hangar.selfDisabled=state.selfDisabled;hangar.doorCurrentSpeed=state.currentSpeed;
        if(hangar.doors!=null)foreach(var door in hangar.doors)if(door?.transform!=null)door.Move(state.openAmount);
        if(hangar.lights!=null)foreach(var light in hangar.lights)light?.Enable(!state.selfDisabled && state.openAmount>0);
    }
    public static string HangarPath(Unit unit,Hangar hangar)
    {
        var indices=new List<int>();var transform=hangar.transform;int depth=0;
        while(transform!=unit.transform)
        {
            if(transform==null || ++depth>64)return null;
            indices.Add(transform.GetSiblingIndex());transform=transform.parent;
        }
        indices.Reverse();
        var components=hangar.GetComponents<Hangar>();int component=Array.IndexOf(components,hangar);
        if(component<0)return null;
        return string.Join("/",indices)+"@"+component.ToString(CultureInfo.InvariantCulture);
    }
    private static Hangar ResolveHangar(Unit unit,string path)
    {
        int marker=path.LastIndexOf('@');
        if(marker<0 || !int.TryParse(path.Substring(marker+1),NumberStyles.None,CultureInfo.InvariantCulture,out int component) || component<0)return null;
        var transform=unit.transform;
        if(marker>0)foreach(string text in path.Substring(0,marker).Split('/'))
        {
            if(!int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out int sibling)||sibling<0||sibling>=transform.childCount)return null;
            transform=transform.GetChild(sibling);
        }
        var hangars=transform.GetComponents<Hangar>();
        return component<hangars.Length && ReferenceEquals(hangars[component].attachedUnit,unit)?hangars[component]:null;
    }
}
