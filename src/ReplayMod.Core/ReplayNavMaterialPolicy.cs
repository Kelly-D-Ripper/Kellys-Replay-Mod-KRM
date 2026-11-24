namespace ReplayMod.Core;

internal static class ReplayNavMaterialPolicy
{
    // Only a material newly instantiated by the native light initializer is
    // ours to release. Existing shared materials retain their native owner.
    internal static bool ShouldTrack(bool playback,bool rendererPresent,bool changed)
        =>playback&&rendererPresent&&changed;

    // If a wreck part has detached, its former ancestor no longer owns the
    // renderer's lifetime; let the renderer's own cleanup handle it instead.
    internal static bool CanRelease(bool ancestor,bool rendererExists,bool stillChild)
        =>!ancestor||!rendererExists||stillChild;
}
