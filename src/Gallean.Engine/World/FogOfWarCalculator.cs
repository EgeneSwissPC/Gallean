using System.Collections.Immutable;
using System.Numerics;
using Gallean.Common;

namespace Gallean.Engine;

internal static class FogOfWarCalculator
{
    public static ImmutableArray<WorldPosition> Diff(ImmutableBitmap before, ImmutableBitmap after)
    {
        ImmutableArray<WorldPosition>.Builder revealed = ImmutableArray.CreateBuilder<WorldPosition>();

        if (ReferenceEquals(before, after))
        {
            return revealed.ToImmutable();
        }

        // Word by word: on a 1024x1024 map this runs on every army step, and almost every word is unchanged.
        ReadOnlySpan<ulong> beforeWords = before.GetWords();
        ReadOnlySpan<ulong> afterWords = after.GetWords();

        for (int wordIndex = 0; wordIndex < afterWords.Length; wordIndex++)
        {
            ulong added = afterWords[wordIndex] & ~beforeWords[wordIndex];

            while (added != 0)
            {
                int index = (wordIndex * 64) + BitOperations.TrailingZeroCount(added);
                revealed.Add(new WorldPosition(index % after.Width, index / after.Width));
                added &= added - 1;
            }
        }

        return revealed.ToImmutable();
    }

    public static ImmutableBitmap Update(
        WorldState state,
        WorldPlayerId playerId,
        IEngineContext context)
    {
        ImmutableBitmap.Builder builder = state.Players[playerId].FogOfWar.ToBuilder();

        foreach (WorldStack stack in state.Stacks.Values)
        {
            if (stack.OwnerId == playerId)
            {
                StackStats stats = context.UnitStats.GetWorldStackStats(stack);
                RevealAroundPosition(builder, stack.Position, stats.VisionRadius);
            }
        }

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            if (FortificationCalculator.GetOwner(fortification) == playerId)
            {
                int visionRadius = FortificationCalculator.GetVisionRadius(fortification, context);

                foreach (WorldPosition tile in fortification.Footprint.EnumerateTiles())
                {
                    RevealAroundPosition(builder, tile, visionRadius);
                }
            }
        }

        OwnLandVisionCalculator.Reveal(builder, state, playerId);

        return builder.ToImmutable();
    }

    private static void RevealAroundPosition(ImmutableBitmap.Builder builder, WorldPosition center, int radius)
    {
        int minY = Math.Max(0, center.Y - radius);
        int maxY = Math.Min(builder.Height - 1, center.Y + radius);
        int minX = Math.Max(0, center.X - radius);
        int maxX = Math.Min(builder.Width - 1, center.X + radius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                builder.Set(x, y);
            }
        }
    }
}
