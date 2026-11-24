using System.Collections.Generic;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public partial class UnitController
{
    private readonly Dictionary<uint,CargoParachuteTimeline> _cargoParachutes=new Dictionary<uint,CargoParachuteTimeline>();
    private readonly Dictionary<uint,bool> _cargoParachuteApplied=new Dictionary<uint,bool>();
    public void Execute(CargoParachuteStateEvent e)
    {
        if(!AcceptSample(e.unitId,e.Time))return;
        if(!_cargoParachutes.TryGetValue(e.unitId,out var timeline))_cargoParachutes[e.unitId]=timeline=new CargoParachuteTimeline();
        timeline.Add(e);
    }
    private void UpdateCargoParachutes(double time)
    {
        foreach(var pair in _cargoParachutes)
        {
            var unit=FindActor(pair.Key);
            if(unit==null||!pair.Value.Sample(time,out var first,out var next,out var fraction))continue;
            // Ground units commonly have a parachute prefab but were never
            // dropped. Avoid searching their hierarchy on every replay frame.
            if(!first.present&&_cargoParachuteApplied.TryGetValue(pair.Key,out bool present)&&!present)continue;
            if(ReplayCargoParachutes.Apply(unit,first,next,fraction))_cargoParachuteApplied[pair.Key]=first.present;
        }
    }
    private void ForgetCargoParachutes(uint id){_cargoParachutes.Remove(id);_cargoParachuteApplied.Remove(id);}
    private void ClearCargoParachutes(){_cargoParachutes.Clear();_cargoParachuteApplied.Clear();}
}
