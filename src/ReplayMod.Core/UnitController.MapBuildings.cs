using System;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class UnitController
{
    private ReplayMapPlayback _mapPlayback;
    private ReplayBuildingPresentation _buildingPresentation;
    private int _mapBuildingWarnings;
    private void TrackMapBuildingActor(uint id,Unit unit)
    {
        try
        {
            if(_mapPlayback==null)_mapPlayback=new ReplayMapPlayback(this);
            _mapPlayback.Track(id,unit);
        }
        catch(Exception error){WarnMapBuildingPresentation("map actor binding",error);}
        try
        {
            if(_buildingPresentation==null)_buildingPresentation=new ReplayBuildingPresentation(this);
            _buildingPresentation.Track(id,unit);
        }
        catch(Exception error){WarnMapBuildingPresentation("building presentation binding",error);}
    }
    public void Execute(BuildingStateEvent e)
    {if(AcceptSample(e.unitId,e.Time))_buildingPresentation?.Apply(e);}
    private void ApplyRecordedBuildingStatus(UnitStatusEvent e)
    {if(AcceptSample(e.unitId,e.Time))_buildingPresentation?.ApplyLegacy(e);}
    private void RefreshReplayMapBuildings(double time)
    {
        try{_mapPlayback?.Refresh(Time.realtimeSinceStartup);}
        catch(Exception error){WarnMapBuildingPresentation("map refresh",error);}
        RefreshReplayBuildingPresentation(time);
    }
    private void RefreshReplayBuildingPresentation(double time)
    {
        try{_buildingPresentation?.Render(time);}
        catch(Exception error){WarnMapBuildingPresentation("building visibility",error);}
    }
    private bool RestoreReplayBuildingPose(uint id,Unit unit,double time)
    {
        try{return _buildingPresentation?.TryPose(id,unit,time)==true;}
        catch(Exception error)
        {
            WarnMapBuildingPresentation("building pose",error);
            try{_buildingPresentation?.Forget(id);}catch(Exception cleanup){WarnMapBuildingPresentation("building pose release",cleanup);}
            return false;
        }
    }
    private void ForgetMapBuildingActor(uint id)
    {
        try{_mapPlayback?.Forget(id);}catch(Exception error){WarnMapBuildingPresentation("map actor release",error);}
        try{_buildingPresentation?.Forget(id);}catch(Exception error){WarnMapBuildingPresentation("building actor release",error);}
    }
    private void ClearMapBuildingPlayback()
    {
        var map=_mapPlayback;var buildings=_buildingPresentation;
        try{map?.Clear();}catch(Exception error){WarnMapBuildingPresentation("map cleanup",error);}
        finally
        {
            try{buildings?.Clear();}catch(Exception error){WarnMapBuildingPresentation("building cleanup",error);}
            finally{_mapPlayback=null;_buildingPresentation=null;}
        }
    }
    private void WarnMapBuildingPresentation(string operation,Exception error)
    {
        if(_mapBuildingWarnings>=8)return;_mapBuildingWarnings++;
        string message=error.Message??error.GetType().Name;if(message.Length>180)message=message.Substring(0,180);
        try{Plugin.SafeLogWarning("Replay "+operation+" failed; actor playback continues: "+message);}catch(Exception){}
    }
}
