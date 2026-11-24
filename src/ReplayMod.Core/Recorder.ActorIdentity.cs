using System;
using System.Collections.Generic;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using NuclearOption.Networking;

namespace ReplayMod.Core;

public partial class Recorder
{
    private readonly Dictionary<Aircraft,ActorIdentitySnapshot> _recordedActorIdentities=new Dictionary<Aircraft,ActorIdentitySnapshot>();
    private readonly HashSet<uint> _actorIdentityWarnings=new HashSet<uint>();
    private double _lastIdentityCapture=double.NegativeInfinity;
    private void RecordActorIdentity(Unit unit,bool force=false)
    {
        if(!(unit is Aircraft aircraft)||!_acceptEvents||!SessionValid||!IsTracked(aircraft))return;
        ActorIdentityEvent pending=null;
        try
        {
            var player=aircraft.Player;
            bool human=player!=null&&ReferenceEquals(player.Aircraft,aircraft);
            // Player.GetDisplayName can populate its cache using Steam/auth data.
            // Read only the game's existing filtered Other-context name instead.
            string name=human?player._playerNameCache?.GetDisplayName(PlayerNameContext.Other)??"":"";
            var identity=new ActorIdentitySnapshot(name,unit.UniqueName,human);
            if(!force&&_recordedActorIdentities.TryGetValue(aircraft,out var previous)&&identity.Same(previous))return;
            pending=ReplayEventFactory.GetEvent<ActorIdentityEvent>();pending.Time=_virtualTime;pending.unitId=aircraft.persistentID.Id;
            pending.humanControlled=identity.HumanControlled;pending.playerName=identity.PlayerName;pending.callsign=identity.Callsign;
            var queued=pending;pending=null;QueueEvent(queued);_recordedActorIdentities[aircraft]=identity;
        }
        catch(Exception error)
        {
            if(pending!=null)ReplayEventFactory.Return(pending);CaptureFailureCount++;
            uint id=aircraft.persistentID.Id;
            if(_actorIdentityWarnings.Count<1024&&_actorIdentityWarnings.Add(id))Plugin.logger.LogWarning("Recorded actor identity unavailable for replay unit #"+id+": "+error.Message);
        }
    }
    private void CaptureActorIdentitiesLate()
    {
        if(_virtualTime-_lastIdentityCapture<.5)return;_lastIdentityCapture=_virtualTime;
        foreach(var unit in _unitsOnTrack.Values)if(unit is Aircraft)RecordActorIdentity(unit);
    }
    private void ForgetRecordingActorIdentity(Unit unit)
    {if(unit is Aircraft aircraft)_recordedActorIdentities.Remove(aircraft);}
    private void ClearRecordingActorIdentities()
    {_recordedActorIdentities.Clear();_actorIdentityWarnings.Clear();_lastIdentityCapture=double.NegativeInfinity;}
}
