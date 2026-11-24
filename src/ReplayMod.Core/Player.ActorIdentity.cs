using ReplayMod.Data;

namespace ReplayMod.Core;

public partial class Player
{
    public ActorIdentitySnapshot GetActorIdentity(uint id)=>_unitCotroller?.GetActorIdentity(id);
    public bool TryRecordedId(Unit unit,out uint id)
    {id=0;return _unitCotroller!=null&&_unitCotroller.TryRecordedId(unit,out id);}
}
