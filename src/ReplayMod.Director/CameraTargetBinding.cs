using System.Collections.Generic;

namespace ReplayMod.Director;

// Recorded IDs may be reused. A shot follows the selected actor lifetime until
// the user selects another actor, or a seek deliberately reconstructs the world.
public sealed class CameraTargetBinding<T> where T:class
{
    private readonly Dictionary<uint,T> lifetimes=new Dictionary<uint,T>();
    public void Select(uint id,T actor)
    {
        if(actor==null)lifetimes.Remove(id);else lifetimes[id]=actor;
    }
    public bool TryResolve(uint id,T current,out T actor)
    {
        actor=null;
        if(current==null)return false;
        if(!lifetimes.TryGetValue(id,out var selected)){lifetimes[id]=current;actor=current;return true;}
        if(!ReferenceEquals(selected,current))return false;
        actor=current;return true;
    }
    public void Reset()=>lifetimes.Clear();
}
