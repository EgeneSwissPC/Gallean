using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// The block editor of one event (docs/map-editor-events.md): a palette of coloured blocks on the left, the script in
// the middle (a "When" hat with condition groups joined by "or", then a "Then" stack of effects) and the settings in
// plain words on the right. A click on a palette block adds it (conditions to the last group); dragging adds it where
// it is dropped, moves blocks around and, dropped on the palette, removes them. OK hands the event back as one undo
// step; nothing on the map changes before that.
internal sealed partial class MapEditorEventScreenViewModel : AbstractScreenViewModel
{
    private readonly MapEditorEventContext context;
    private readonly CustomMapEvent original;
    private readonly Func<CustomMapEvent, string?> onConfirm;
    private readonly Action onDelete;
    private bool loading = true;

    public MapEditorEventScreenViewModel(GameViewModel game, MapEditorEventContext context, CustomMapEvent ev, Func<CustomMapEvent, string?> onConfirm, Action onDelete)
    {
        Game = game;
        this.context = context;
        original = ev;
        this.onConfirm = onConfirm;
        this.onDelete = onDelete;
        Title = ev.Name;

        foreach (WorldRace race in context.Races)
        {
            RaceToggles.Add(new MapEditorRaceToggleViewModel(race, MapEditorLabels.RaceShort(race), ev.Races.Contains(race), Refresh));
            ConcernToggles.Add(new MapEditorRaceToggleViewModel(race, MapEditorLabels.RaceShort(race), ev.Concerns.Contains(race), Refresh));
        }

        UseConcerns = ev.ConcernsRaces is not null;
        Once = ev.Once;
        Enabled = ev.Enabled;
        ChanceText = ev.Chance.ToString(CultureInfo.InvariantCulture);
        OrderText = ev.Order.ToString(CultureInfo.InvariantCulture);

        foreach (ImmutableArray<CustomMapCondition> group in ev.Groups.IsEmpty ? [[]] : ev.Groups)
        {
            MapEditorEventGroupViewModel row = AddGroupRow();

            foreach (CustomMapCondition condition in group)
            {
                row.Blocks.Add(MapEditorEventBlockViewModel.FromCondition(condition, context, Refresh));
            }
        }

        foreach (CustomMapEffect effect in ev.Effects)
        {
            Effects.Add(MapEditorEventBlockViewModel.FromEffect(effect, context, Refresh, PreviewMessage));
        }

        loading = false;
        Refresh();
    }

    public GameViewModel Game { get; }

    public string Title { get; }

    public IReadOnlyList<MapEditorPaletteGroup> ConditionPalette { get; } = MapEditorEventBlockViewModel.CreatePalette(conditions: true);

    public IReadOnlyList<MapEditorPaletteGroup> EffectPalette { get; } = MapEditorEventBlockViewModel.CreatePalette(conditions: false);

    public ObservableCollection<MapEditorRaceToggleViewModel> RaceToggles { get; } = [];

    public ObservableCollection<MapEditorRaceToggleViewModel> ConcernToggles { get; } = [];

    public ObservableCollection<MapEditorEventGroupViewModel> Groups { get; } = [];

    public ObservableCollection<MapEditorEventBlockViewModel> Effects { get; } = [];

    public override bool BlurBackdrop => true;

    [ObservableProperty]
    public partial bool UseConcerns { get; set; }

    [ObservableProperty]
    public partial bool Once { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial string ChanceText { get; set; } = "100";

    [ObservableProperty]
    public partial string OrderText { get; set; } = "0";

    [ObservableProperty]
    public partial bool IsAdvancedOpen { get; set; }

    // What keeps the event out of the saved map, in words; empty when it is saved.
    [ObservableProperty]
    public partial string ProblemText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRaceProblem { get; private set; }

    public bool HasProblems => ProblemText.Length > 0;

    public bool HasNoEffects => Effects.Count == 0;

    partial void OnUseConcernsChanged(bool value) => Refresh();

    partial void OnOnceChanged(bool value) => Refresh();

    partial void OnEnabledChanged(bool value) => Refresh();

    partial void OnChanceTextChanged(string value) => Refresh();

    partial void OnOrderTextChanged(string value) => Refresh();

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    // The event as the blocks and settings describe it now.
    public CustomMapEvent BuildEvent()
    {
        ImmutableArray<WorldRace> races = [.. RaceToggles.Where(toggle => toggle.IsChecked).Select(toggle => toggle.Race)];
        ImmutableArray<WorldRace>? concerns = UseConcerns ? [.. ConcernToggles.Where(toggle => toggle.IsChecked).Select(toggle => toggle.Race)] : null;

        return original with
        {
            Races = races,
            ConcernsRaces = concerns,
            Once = Once,
            Enabled = Enabled,
            Chance = ParseNumber(ChanceText, original.Chance, 1, CustomMap.MaxEventChance),
            Order = ParseNumber(OrderText, original.Order, -999, 999),
            Groups = [.. Groups.Select(group => group.Blocks.Select(block => block.ToCondition()).ToImmutableArray())],
            Effects = [.. Effects.Select(block => block.ToEffect())]
        };
    }

    // A click on a palette block: conditions go to the last group, effects to the end of Then.
    [RelayCommand]
    private void AddBlock(MapEditorPaletteBlock palette)
    {
        if (palette.IsCondition)
        {
            InsertBlock(palette, Groups[^1].Blocks, Groups[^1].Blocks.Count);
        }
        else
        {
            InsertBlock(palette, Effects, Effects.Count);
        }
    }

    // Adds a palette block at index of target; a condition can't go into Then or an effect into a group.
    public MapEditorEventBlockViewModel? InsertBlock(MapEditorPaletteBlock palette, IList<MapEditorEventBlockViewModel> target, int index)
    {
        if (palette.IsCondition != IsConditionList(target))
        {
            return null;
        }

        MapEditorEventBlockViewModel block = MapEditorEventBlockViewModel.Create(palette.Kind, context, Refresh, PreviewMessage);
        target.Insert(Math.Clamp(index, 0, target.Count), block);
        Refresh();

        return block;
    }

    // Moves a block (drag) to index of target: within a list, between "or" groups, never between When and Then.
    public bool MoveBlock(MapEditorEventBlockViewModel block, IList<MapEditorEventBlockViewModel> target, int index)
    {
        if (block.IsCondition != IsConditionList(target) || FindList(block) is not { } source)
        {
            return false;
        }

        int from = source.IndexOf(block);
        source.RemoveAt(from);

        if (ReferenceEquals(source, target) && from < index)
        {
            index--;
        }

        target.Insert(Math.Clamp(index, 0, target.Count), block);
        Refresh();

        return true;
    }

    [RelayCommand]
    private void RemoveBlock(MapEditorEventBlockViewModel block)
    {
        FindList(block)?.Remove(block);
        Refresh();
    }

    // "or": a new empty condition group.
    [RelayCommand]
    private void AddGroup()
    {
        AddGroupRow();
        Refresh();
    }

    public void RemoveGroup(MapEditorEventGroupViewModel group)
    {
        if (Groups.Count > 1)
        {
            Groups.Remove(group);
            RenumberGroups();
            Refresh();
        }
    }

    [RelayCommand]
    private void ToggleAdvanced()
    {
        IsAdvancedOpen = !IsAdvancedOpen;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (onConfirm(BuildEvent()) is { } error)
        {
            Game.ShowError(error);
            return;
        }

        CloseSelf();
    }

    [RelayCommand]
    private void Delete()
    {
        CloseSelf();
        onDelete();
    }

    public IList<MapEditorEventBlockViewModel>? FindList(MapEditorEventBlockViewModel block)
    {
        if (Effects.Contains(block))
        {
            return Effects;
        }

        return Groups.FirstOrDefault(group => group.Blocks.Contains(block))?.Blocks;
    }

    private bool IsConditionList(IList<MapEditorEventBlockViewModel> list)
    {
        return !ReferenceEquals(list, Effects);
    }

    private MapEditorEventGroupViewModel AddGroupRow()
    {
        MapEditorEventGroupViewModel group = new(this, Groups.Count == 0);
        Groups.Add(group);

        return group;
    }

    private void RenumberGroups()
    {
        for (int i = 0; i < Groups.Count; i++)
        {
            Groups[i].IsFirst = i == 0;
        }
    }

    // Marks the blocks the engine would refuse and says why the event wouldn't be saved.
    private void Refresh()
    {
        if (loading)
        {
            return;
        }

        CustomMapEvent ev = BuildEvent();
        IReadOnlyList<CustomMapEventProblem> problems = context.Map.GetEventProblems(ev);
        List<string> reasons = [];

        foreach (MapEditorEventGroupViewModel group in Groups)
        {
            foreach (MapEditorEventBlockViewModel block in group.Blocks)
            {
                CustomMapCondition condition = block.ToCondition();
                block.HasProblem = problems.Any(problem => Equals(problem.Block, condition));
            }
        }

        foreach (MapEditorEventBlockViewModel block in Effects)
        {
            CustomMapEffect effect = block.ToEffect();
            block.HasProblem = problems.Any(problem => Equals(problem.Block, effect));
        }

        HasRaceProblem = problems.Any(problem => problem.Kind == CustomMapEventProblemKind.NoRace);

        if (HasRaceProblem)
        {
            reasons.Add(Tr("pick a race under When"));
        }

        if (problems.Any(problem => problem.Kind == CustomMapEventProblemKind.NoCondition))
        {
            reasons.Add(Tr("add a condition"));
        }

        if (problems.Any(problem => problem.Kind == CustomMapEventProblemKind.MissingTarget))
        {
            reasons.Add(Tr("fill in or fix the red blocks"));
        }

        if (problems.Any(problem => problem.Kind == CustomMapEventProblemKind.SecondObjective))
        {
            reasons.Add(Tr("only one objective change per event"));
        }

        ProblemText = reasons.Count == 0 ? string.Empty : Tr("Not saved with the map yet: {0}.", string.Join(", ", reasons));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasNoEffects));
    }

    // The game's own story popup with the block's text (default or one translation) and portrait.
    private void PreviewMessage(MapEditorEventBlockViewModel block, string? language)
    {
        if (block.ToEffect() is not CustomMapMessageEffect message)
        {
            return;
        }

        string text = language is null ? message.Text.Default : message.Text.Get(language);
        ScenarioPopupText popup = ScenarioPopupText.Parse(text.Length == 0 ? Tr("(empty message)") : text);
        ImmutableArray<ScenarioTextSpan> spans = new ScenarioTextFormatter(context.EditorGame.Texts.Names).Format(popup.Speech);
        AssetSpriteKey? portraitKey = message.Portrait is { } portrait ? new AssetSpriteKey.UnitEventPortrait(new ScenarioEventPortraitId(portrait)) : null;
        string speaker = popup.Speaker ?? (message.Portrait is { } id ? context.UnitName(id) : string.Empty);

        Game.OpenScreen(new ScenarioEventPopupViewModel([new ScenarioEventPopupMessage(spans, speaker, portraitKey, message.PortraitLeft)]));
    }

    private static int ParseNumber(string text, int fallback, int minimum, int maximum)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }
}

// One "or" group of conditions.
internal sealed partial class MapEditorEventGroupViewModel : ObservableObject
{
    private readonly MapEditorEventScreenViewModel screen;

    public MapEditorEventGroupViewModel(MapEditorEventScreenViewModel screen, bool isFirst)
    {
        this.screen = screen;
        IsFirst = isFirst;
        Blocks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoBlocks));
    }

    public ObservableCollection<MapEditorEventBlockViewModel> Blocks { get; } = [];

    public bool HasNoBlocks => Blocks.Count == 0;

    [ObservableProperty]
    public partial bool IsFirst { get; set; }

    public MapEditorEventScreenViewModel Screen => screen;

    [RelayCommand]
    private void Remove() => screen.RemoveGroup(this);
}

internal sealed partial class MapEditorRaceToggleViewModel(WorldRace race, string label, bool isChecked, Action changed) : ObservableObject
{
    public WorldRace Race { get; } = race;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;

    partial void OnIsCheckedChanged(bool value) => changed();
}
