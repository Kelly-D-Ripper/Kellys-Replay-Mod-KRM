namespace ReplayMod.Core;

public static class ReplaySpawnPolicy
{
    public const string NoFaction = "no faction";

    // Scenery ignores HQ. The native vehicle, aircraft, ship and missile paths
    // can also spawn with an explicitly neutral HQ. Dismounted pilots and unknown
    // fallback definitions remain guarded because their update paths may need it.
    // A missing recorded named faction must not silently become neutral.
    public static bool CanSpawnWithoutFaction(bool isBuilding, bool isScenery, bool isContainer, string recordedFaction, bool isNeutralVehicle = false)
        => isScenery || ((isBuilding || isContainer || isNeutralVehicle) && recordedFaction == NoFaction);
}
