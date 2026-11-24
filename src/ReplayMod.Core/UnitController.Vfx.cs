using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class UnitController
{
    public void PrepareVfxActor(uint id,double time)=>PositionActorForEffect(id,time);
    public void Execute(FuelTankStatusEvent e){if(e.ruptured||e.onFire||e.flameActive)MarkMotionDamaged(e.unitId);ReplayVfxPlayback.Apply(this,e);}
    public void Execute(MissileDetonationEvent e)=>ReplayVfxPlayback.Apply(this,e);
    public void Execute(UnitStatusEvent e){if(e.disabled)MarkMotionDamaged(e.unitId);ReplayVfxPlayback.Apply(this,e);ApplyRecordedBuildingStatus(e);}
}
