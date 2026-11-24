using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class UnitController
{
    private readonly ActorIdentityCache _actorIdentities=new ActorIdentityCache();
    public void Execute(ActorIdentityEvent value)
    {if(value.HasValidPayload()&&AcceptSample(value.unitId,value.Time)&&_spawnTimes.TryGetValue(value.unitId,out var birth))_actorIdentities.Apply(value,birth);}
    public ActorIdentitySnapshot GetActorIdentity(uint id)=>FindActor(id)!=null&&_spawnTimes.TryGetValue(id,out var birth)?_actorIdentities.Get(id,birth):null;
    public bool TryRecordedId(Unit unit,out uint id)
    {id=0;return unit!=null&&_reverseUnitMap.TryGetValue(unit.persistentID,out id)&&TryGetUnit(id,out _,out var current)&&ReferenceEquals(current,unit);}
}
