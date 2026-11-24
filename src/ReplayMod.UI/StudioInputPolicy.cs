namespace ReplayMod.UI;

// Opening the studio must not depend on a stale global IMGUI control belonging
// to a hidden menu. Ordinary replay actions retain the broader typing guard.
internal static class StudioInputPolicy
{
    internal static bool TextEntryActive(bool selectedActive,bool inputEnabled,bool focused,bool canvasVisible)
        =>selectedActive&&inputEnabled&&focused&&canvasVisible;

    internal static bool CanToggle(bool cleanCapture,bool capturingShortcut,bool assignmentGuard,
        bool studioVisible,bool activeTextEntry,bool globalImGuiFocus)
    {
        if(capturingShortcut||assignmentGuard)return false;
        if(cleanCapture)return true;
        if(activeTextEntry)return false;
        return !studioVisible||!globalImGuiFocus;
    }

    internal static bool BlocksReplayActions(bool capturingShortcut,bool assignmentGuard,
        bool activeTextEntry,bool globalImGuiFocus)
        =>capturingShortcut||assignmentGuard||activeTextEntry||globalImGuiFocus;

    internal static bool UsableNativeEvents(bool activeSystem,bool enabledModule)
        =>activeSystem&&enabledModule;
}
