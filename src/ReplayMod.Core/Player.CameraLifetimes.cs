using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class Player
{
    private readonly CameraActorLifetimeIndex _cameraLifetimes=new CameraActorLifetimeIndex();
    private void ObserveCameraLifetime(IReplayEvent e,long sequence)
    {
        if(e is SpawnEvent spawn)_cameraLifetimes.Spawn(spawn.unitId,e.Time,sequence);
        else if(e is DespawnEvent despawn)_cameraLifetimes.Despawn(despawn.unitId,e.Time);
    }
    public bool TryGetActorLifetime(uint actor,long generation,double time,out ReplayActorLifetime lifetime)
        =>generation>0?_cameraLifetimes.TryGeneration(actor,generation,out lifetime):_cameraLifetimes.TryAt(actor,time,out lifetime);
    public long CurrentActorGeneration(uint actor)=>_cameraLifetimes.VisibleGenerationAt(actor,currentVirtualTime);
}
