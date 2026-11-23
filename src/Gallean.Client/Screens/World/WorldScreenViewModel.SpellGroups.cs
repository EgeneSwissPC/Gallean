using Gallean.Engine;
using static Gallean.Client.WorldEventQueue;

namespace Gallean.Client;

// Gallean Upgrade: casting spell groups, and keeping the spell cursor after a cast so the same spell (or group)
// can be cast again with the next click (mages may cast each spell twice a turn). Esc or right-click ends it.
internal sealed partial class WorldScreenViewModel
{
    // Casts the spells one after another, in order. Targeted spells land on target; without a target they are
    // skipped. A spell that can't be cast (mana, already cast this turn) is skipped and the rest still go.
    // Returns how many spells were cast.
    internal async Task<int> CastSpellGroupAsync(IReadOnlyList<WorldSpellDefinitionId> spells, WorldPosition? target)
    {
        if (IsBusy && !CanCastDuringAnimations)
        {
            return 0;
        }

        SkipSpellAnimations();

        int cast = 0;
        List<string> skipped = [];

        foreach (WorldSpellDefinitionId spellId in spells)
        {
            if (WorldState.ScenarioState.IsEnded)
            {
                break;
            }

            WorldSpellDefinition spell = GameModel.Definitions.GetSpell(spellId);
            string name = GameModel.Texts.GetGlobalText(spell.NameTextId).Text;
            WorldSpellTarget? spellTarget = null;

            if (spell.AreaRadius is not null)
            {
                if (target is not { } position)
                {
                    skipped.Add(Tr("{0}: needs a target on the map", name));
                    continue;
                }

                spellTarget = new WorldSpellTarget(position, Radius: 0);
            }

            ICommandResult<WorldCommandError, WorldEvent, Gallean.Common.Empty> result =
                await GameModel.World.DispatchCommandAsync(new WorldCastSpellCommand(spellId, spellTarget));

            if (result.Failed)
            {
                skipped.Add(Tr("{0}: {1}", name, WorldCommandErrorFormatter.Format(result.Error.Value)));
            }
            else
            {
                cast++;
            }
        }

        if (skipped.Count > 0 && cast == 0)
        {
            // Nothing happened (most often a click on a tile the spells can't target): an error like a single spell's.
            Game.PlaySound(new AssetSoundKey.Beep());
            Game.Alert.Show(new Alert(AlertType.Error, IconMoniker.Warning, Tr("No spell of the group was cast"), string.Join(Environment.NewLine, skipped)));
        }
        else if (skipped.Count > 0)
        {
            Game.ShowInfo(IconMoniker.Spellbook, Tr("{0} of {1} spells cast", cast, spells.Count), string.Join(Environment.NewLine, skipped));
        }

        return cast;
    }

    private async Task DispatchSpellActionAsync(WorldCursorAction action, WorldPosition position)
    {
        if (action is WorldCursorAction.CastSpellGroup group)
        {
            ClearSelectedAction();
            await CastSpellGroupAsync(group.Spells, position);

            if (group.Spells.Any(CanCastSpellNow))
            {
                SelectAction(action);
            }
        }
        else if (action is WorldCursorAction.CastSpell spell)
        {
            ClearSelectedAction();
            SkipSpellAnimations();

            ICommandResult<WorldCommandError, WorldEvent, Gallean.Common.Empty> result = await GameModel.World.DispatchCommandAsync(action.CreateCommand(position));

            if (result.Failed)
            {
                Game.ShowError(WorldCommandErrorFormatter.Format(result.Error.Value));
            }

            // After an error the spell stays selected, as in upstream (385a887a); after a cast while it can be cast again.
            if (result.Failed || CanCastSpellNow(spell.SpellId))
            {
                SelectAction(action);
            }
        }
    }

    // The next spell may be cast while the last one's animation still plays (the click isn't lost): only the
    // engine work and the turn matter, not the queue of map animations.
    private bool CanCastDuringAnimations => !base.IsBusy && Snapshot.IsViewPlayerTurn && activeMove is null;

    // A new cast cuts the spell animations still playing or waiting (from an earlier spell or group) short, so
    // the new spell shows at once instead of after the old ones. Other animations (moves, battles) stay.
    internal void SkipSpellAnimations()
    {
        eventQueue.RemoveQueued(entry => entry is Entry.SpellCast or Entry.GlobalSpellCast);

        if (eventQueue.PlayingEntry is Entry.SpellCast or Entry.GlobalSpellCast)
        {
            StopEntry();
        }

        ProcessEventQueue();
    }

    internal Entry? PlayingEntry => eventQueue.PlayingEntry;

    internal int QueuedSpellAnimations => eventQueue.CountQueued(entry => entry is Entry.SpellCast or Entry.GlobalSpellCast);

    // Known, affordable and not yet cast the maximum number of times this turn.
    private bool CanCastSpellNow(WorldSpellDefinitionId spellId)
    {
        if (WorldState.ScenarioState.IsEnded || !ReferenceEquals(Game.RootScreen, this))
        {
            return false;
        }

        ICommandResult<WorldCommandError, WorldEvent, WorldGetSpellbookInfoCommand.Result> result =
            GameModel.World.DryRunCommand(new WorldGetSpellbookInfoCommand(ViewPlayerId));

        return !result.Failed && result.Data.Acquired.Any(cast => cast.SpellId == spellId && cast.Error is null);
    }
}
