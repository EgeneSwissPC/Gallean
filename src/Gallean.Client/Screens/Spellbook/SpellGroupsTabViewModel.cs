using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: the "Spell groups" tab next to the Spellbook. Each group is cast like one spell: its spells go
// one after another, in order. A group with a spell cast on the map asks for one target on the map first.
internal sealed partial class SpellGroupsTabViewModel : ManagementTabViewModel
{
    public SpellGroupsTabViewModel(GameModel gameModel, GameViewModel gameViewModel, ManagementScreenViewModel screen)
        : base(gameModel, gameViewModel)
    {
        Screen = screen;

        RefreshView();
    }

    public ManagementScreenViewModel Screen { get; }

    public override string TabHeader => Tr("Spell groups");
    public override AssetSoundKey OpenSound => new AssetSoundKey.BookOpen();
    public override AssetSoundKey CloseSound => new AssetSoundKey.BookClose();
    public override ResourceSetProjection ResourceProjection => ResourceSetProjection.Mana;

    [ObservableProperty]
    public partial IReadOnlyList<SpellGroupRowViewModel> Groups { get; private set; } = [];

    protected override void OnSelectionChanged(bool isSelected)
    {
        if (isSelected)
        {
            RefreshView();
        }
    }

    protected override void OnSnapshotChanged(GameSnapshot current)
    {
        RefreshView();
    }

    internal async Task CastAsync(SpellGroupRowViewModel row)
    {
        if (Game.RootScreen is not WorldScreenViewModel world || !row.Status.IsAvailable)
        {
            return;
        }

        if (row.FirstTargeted is { } targeted)
        {
            Screen.Close();
            world.SelectAction(new WorldCursorAction.CastSpellGroup(row.Name, row.Spells, targeted));
        }
        else
        {
            Screen.Close();
            await world.CastSpellGroupAsync(row.Spells, target: null);
        }
    }

    internal void Delete(SpellGroupRowViewModel row)
    {
        SpellGroupStore.Remove(WorldState.Players[ViewPlayerId].Race, row.Index);
        RefreshView();
    }

    internal void Refresh()
    {
        RefreshView();
    }

    private void RefreshView()
    {
        WorldPlayer player = WorldState.Players[ViewPlayerId];
        ICommandResult<WorldCommandError, WorldEvent, WorldGetSpellbookInfoCommand.Result> info =
            GameModel.World.DryRunCommand(new WorldGetSpellbookInfoCommand(ViewPlayerId));
        Dictionary<WorldSpellDefinitionId, WorldCommandError?> casts = info.Failed ? [] : info.Data.Acquired.ToDictionary(cast => cast.SpellId, cast => cast.Error);
        IReadOnlyList<SpellGroup> groups = SpellGroupStore.Load(player.Race);
        List<SpellGroupRowViewModel> rows = [];

        for (int index = 0; index < groups.Count; index++)
        {
            SpellGroup group = groups[index];
            WorldResourceSet cost = WorldResourceSet.Empty;
            int ready = 0;
            WorldSpellDefinitionId? firstTargeted = null;
            List<SpellGroupSpellViewModel> spells = [];

            foreach ((WorldSpellDefinitionId spellId, int position) in group.Spells.Select((id, i) => (id, i + 1)))
            {
                if (TryGetSpell(spellId) is not { } spell)
                {
                    continue;
                }

                bool known = casts.TryGetValue(spellId, out WorldCommandError? error);
                bool castable = known && error is null;

                WorldResourceSet c = spell.CastingCost;
                cost = new WorldResourceSet(cost.Gold + c.Gold, cost.DeathMana + c.DeathMana, cost.RuneMana + c.RuneMana,
                    cost.LifeMana + c.LifeMana, cost.InfernalMana + c.InfernalMana, cost.GroveMana + c.GroveMana);
                ready += castable ? 1 : 0;

                if (spell.AreaRadius is not null)
                {
                    firstTargeted ??= spellId;
                }

                spells.Add(new SpellGroupSpellViewModel(position, castable, SpellDetailsViewModel.Create(spellId, GameModel.Definitions, GameModel.Texts)));
            }

            string? reason = ready == 0 ? Tr("None of these spells can be cast now") : null;
            string readyText = Tr("{0} of {1} spells can be cast now", ready, spells.Count);

            rows.Add(new SpellGroupRowViewModel(
                this,
                index,
                group.Name,
                group.Spells,
                firstTargeted,
                spells,
                readyText,
                new OfferStatusViewModel(new OfferPriceViewModel(cost, player.Resources), reason)));
        }

        Groups = rows;
    }

    // A group saved in another mod or version can name a spell this game doesn't have: it is left out.
    private WorldSpellDefinition? TryGetSpell(WorldSpellDefinitionId spellId)
    {
        try
        {
            return GameModel.Definitions.GetSpell(spellId);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }
}

internal sealed record SpellGroupSpellViewModel(int Position, bool IsCastable, SpellDetailsViewModel Details);

internal sealed partial class SpellGroupRowViewModel(
    SpellGroupsTabViewModel tab,
    int index,
    string name,
    ImmutableArray<WorldSpellDefinitionId> spells,
    WorldSpellDefinitionId? firstTargeted,
    IReadOnlyList<SpellGroupSpellViewModel> spellItems,
    string readyText,
    OfferStatusViewModel status) : ObservableObject
{
    public int Index { get; } = index;
    public string Name { get; } = name;
    public ImmutableArray<WorldSpellDefinitionId> Spells { get; } = spells;
    public WorldSpellDefinitionId? FirstTargeted { get; } = firstTargeted;
    public IReadOnlyList<SpellGroupSpellViewModel> SpellItems { get; } = spellItems;
    public string ReadyText { get; } = readyText;
    public OfferStatusViewModel Status { get; } = status;
    public string CastText => Tr("Cast");

    [RelayCommand]
    private Task CastAsync()
    {
        return tab.CastAsync(this);
    }

    [RelayCommand]
    private void Delete()
    {
        tab.Delete(this);
    }
}
