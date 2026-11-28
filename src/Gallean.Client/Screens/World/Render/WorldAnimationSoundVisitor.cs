using Gallean.Common;
using static Gallean.Client.WorldRenderState;

namespace Gallean.Client;

internal sealed class WorldAnimationSoundVisitor : IWorldAnimationVisitor<Empty, AssetSoundKey?>, IStaticInstance<WorldAnimationSoundVisitor>
{
    public AssetSoundKey? Visit(StackMove animation, Empty context) => animation.SoundKey;
    // One step sound for the whole group, not one per army.
    public AssetSoundKey? Visit(StackMoveGroup animation, Empty context) => animation.Moves.Count > 0 ? animation.Moves[0].SoundKey : null;
    public AssetSoundKey? Visit(Effect animation, Empty context) => animation.SoundKey;
    public AssetSoundKey? Visit(SpellCast animation, Empty context) => animation.SoundKey;
    public AssetSoundKey? Visit(Pause animation, Empty context) => null;
    public AssetSoundKey? Visit(StackBattle animation, Empty context) => null;
    public AssetSoundKey? Visit(BattleTransition animation, Empty context) => null;
}
