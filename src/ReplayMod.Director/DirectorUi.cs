using UnityEngine;

namespace ReplayMod.Director;

public partial class ReplayDirector
{
    public bool ContainsPointer(Vector2 mouse) => MainMenu.IsPointerOverUI;
}
