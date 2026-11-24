using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class UnitController
{
    private ReplayWorldPlayback _worldPlayback;
    private ReplayWorldPlayback WorldPlayback => _worldPlayback??(_worldPlayback=new ReplayWorldPlayback(this));
    public void Execute(HangarDoorStateEvent e)
    {if(AcceptSample(e.unitId,e.Time))WorldPlayback.Add(e);}
    public void Execute(WorldEnvironmentStateEvent e)=>WorldPlayback.Add(e);
    private void UpdateWorld(double time)=>_worldPlayback?.Render(time);
    private void ForgetWorldActor(uint unitId)=>_worldPlayback?.Forget(unitId);
    private void ClearWorldPlayback(){_worldPlayback?.Clear();_worldPlayback=null;}
}
