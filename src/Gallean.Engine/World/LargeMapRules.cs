namespace Gallean.Engine;

/// <summary>
/// Gallean Upgrade: rules that apply only to large maps (256x256 and bigger), so the original
/// campaigns and scenarios (144x144 at most) play exactly as before.
/// Used by own-land vision (<see cref="OwnLandVisionCalculator"/>) and summon upkeep.
/// </summary>
internal static class LargeMapRules
{
    public const int MinMapSize = 256;

    public static bool AppliesTo(WorldMap map)
    {
        return map.Width >= MinMapSize && map.Height >= MinMapSize;
    }
}
