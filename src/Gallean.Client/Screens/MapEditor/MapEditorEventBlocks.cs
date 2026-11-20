using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// The blocks of the event screen (docs/map-editor-events.md). A block is one condition or effect shown as a sentence:
// text pieces and fields (drop-downs, number boxes, texts with translations) in a row, on its category's colour.
// Blocks are built from the engine's CustomMapCondition/CustomMapEffect and turn back into them, so the screen never
// keeps its own copy of the rules.
internal enum MapEditorBlockKind
{
    OnTurn,
    EveryTurns,
    EnterZone,
    OwnCity,
    EnterCity,
    LootRuin,
    DestroyArmy,
    ArmyExists,
    OwnItem,
    LeaderItem,
    Relation,
    Alliance,
    Land,
    VisitSite,
    ArmyToZone,
    ArmyToCity,
    ItemToZone,
    Variable,
    Message,
    Objective,
    Win,
    Lose,
    SwitchEvent,
    RevealZone,
    GiveItem,
    CreateArmy,
    RemoveArmy,
    MoveArmy,
    MoveBeside,
    ChangeOwner,
    OrderSimple,
    OrderGoTo,
    OrderDefendZone,
    OrderDefendCity,
    OrderSecureCity,
    OrderAttack,
    Battle,
    CastSpell,
    CastSpellZone,
    GiveSpell,
    Ally,
    SetRelation,
    RemoveMountains,
    RemoveLandmark,
    ChangeLandmark,
    ChangeTerrain,
    SetVariable,
    DestroyItem
}

internal enum MapEditorBlockCategory
{
    Time,
    Zones,
    Places,
    Armies,
    Items,
    Messages,
    Flow
}

// One palette entry: what a click (or a drag onto the script) adds.
internal sealed record MapEditorPaletteBlock(MapEditorBlockKind Kind, string Label, MapEditorBlockCategory Category)
{
    public bool IsCondition => MapEditorEventBlockViewModel.IsConditionKind(Kind);

    public IBrush Brush => MapEditorEventBlockViewModel.GetBrush(Category);
}

internal sealed record MapEditorPaletteGroup(string Title, IReadOnlyList<MapEditorPaletteBlock> Blocks);

// What the blocks' drop-downs offer: the map's objects by their editor IDs, plus the game's names.
internal sealed class MapEditorEventContext(
    CustomMap map,
    GameModel editorGame,
    Action<Action<ItemDefinitionId>> pickItem,
    Action<Action<UnitDefinitionId?>> pickPortrait,
    Action<MapEditorPickKind, Action<object>>? pickOnMap = null,
    Action<Action<WorldSpellDefinitionId>>? pickSpell = null,
    IReadOnlyList<MapEditorLandmarkOption>? landmarkTypes = null)
{
    public CustomMap Map { get; } = map;

    public GameModel EditorGame { get; } = editorGame;

    public IReadOnlyList<MapEditorOption<object?>> Locations =>
        [.. Map.Locations.OrderBy(location => location.Name, StringComparer.CurrentCultureIgnoreCase).Select(location => new MapEditorOption<object?>(location.Id, location.Name))];

    public IReadOnlyList<MapEditorOption<object?>> Cities =>
    [
        .. Map.Players.Select(player => new MapEditorOption<object?>(new CustomMapCity.Capital(player.Race), Tr("{0} capital", MapEditorLabels.RaceShort(player.Race)))),
        .. Map.Villages.OrderBy(village => village.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(village => new MapEditorOption<object?>(new CustomMapCity.Village(village.Id), Tr("{0} ({1}, {2})", village.Name, village.TopLeft.X, village.TopLeft.Y)))
    ];

    public IReadOnlyList<MapEditorOption<object?>> Ruins =>
        [.. Map.Ruins.OrderBy(ruin => ruin.Name, StringComparer.CurrentCultureIgnoreCase).Select(ruin => new MapEditorOption<object?>(ruin.Id, Tr("{0} ({1}, {2})", ruin.Name, ruin.TopLeft.X, ruin.TopLeft.Y)))];

    public IReadOnlyList<MapEditorOption<object?>> Armies =>
        [.. Map.Stacks.OrderBy(stack => stack.Id).Select(stack => new MapEditorOption<object?>(stack.Id, ArmyLabel(stack)))];

    public IReadOnlyList<MapEditorOption<object?>> Templates =>
        [.. Map.Templates.OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase).Select(template => new MapEditorOption<object?>(template.Id, template.Name))];

    // What an army effect can act on: the army that fired the event (when allowed), placed armies, every army made
    // from a template.
    public IReadOnlyList<MapEditorOption<object?>> ArmyRefs(bool triggerer)
    {
        return
        [
            .. triggerer ? [new MapEditorOption<object?>(new CustomMapArmyRef.Triggerer(), Tr("the army that fired it"))] : Array.Empty<MapEditorOption<object?>>(),
            .. Map.Stacks.OrderBy(stack => stack.Id).Select(stack => new MapEditorOption<object?>(new CustomMapArmyRef.Army(stack.Id), ArmyLabel(stack))),
            .. Map.Templates.OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(template => new MapEditorOption<object?>(new CustomMapArmyRef.Template(template.Id), Tr("every {0} army", template.Name)))
        ];
    }

    public IReadOnlyList<MapEditorOption<object?>> Events =>
        [.. Map.Events.OrderBy(ev => ev.Name, StringComparer.CurrentCultureIgnoreCase).Select(ev => new MapEditorOption<object?>(ev.Id, ev.Name))];

    // Races on the map plus the neutrals.
    public IReadOnlyList<WorldRace> Races => [.. Map.Players.Select(player => player.Race), WorldRace.Neutral];

    public IReadOnlyList<MapEditorOption<object?>> PlayableRaces =>
        [.. Map.Players.Select(player => new MapEditorOption<object?>(player.Race, MapEditorLabels.RaceShort(player.Race)))];

    public void PickItem(Action<ItemDefinitionId> picked) => pickItem(picked);

    public void PickSpell(Action<WorldSpellDefinitionId> picked) => pickSpell?.Invoke(picked);

    public string SpellName(WorldSpellDefinitionId id) => MapEditorLibrary.GetSpellName(EditorGame, id);

    public IReadOnlyList<MapEditorOption<object?>> Sites =>
        [.. Map.Sites.OrderBy(site => site.Name, StringComparer.CurrentCultureIgnoreCase).Select(site => new MapEditorOption<object?>(site.Id, Tr("{0} ({1}, {2})", site.Name, site.TopLeft.X, site.TopLeft.Y)))];

    public IReadOnlyList<MapEditorOption<object?>> PlacedLandmarks =>
    [
        .. Map.Landmarks.OrderBy(landmark => landmark.Id).Select(landmark => new MapEditorOption<object?>(
            landmark.Id,
            Tr("{0} ({1}, {2})", landmarkTypes?.FirstOrDefault(option => option.Definition.Id == landmark.Type)?.Name ?? landmark.Type.Value, landmark.TopLeft.X, landmark.TopLeft.Y)))
    ];

    public IReadOnlyList<MapEditorOption<object?>> LandmarkTypes =>
        [.. (landmarkTypes ?? []).OrderBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase).Select(option => new MapEditorOption<object?>(option.Definition.Id, option.Caption))];

    public IReadOnlyList<MapEditorOption<object?>> Variables =>
        [.. Map.Variables.OrderBy(variable => variable.Name, StringComparer.CurrentCultureIgnoreCase).Select(variable => new MapEditorOption<object?>(variable.Id, variable.Name))];

    public IReadOnlyList<MapEditorOption<object?>> Terrains =>
    [
        .. Map.Players.Select(player => new MapEditorOption<object?>(CustomMap.GetRaceTerrain(player.Race), Tr("{0} land", MapEditorLabels.RaceShort(player.Race)))),
        new MapEditorOption<object?>(WorldMapTerrain.Neutral, Tr("neutral land"))
    ];

    public bool CanPickOnMap => pickOnMap is not null;

    public void PickOnMap(MapEditorPickKind kind, Action<object> picked) => pickOnMap?.Invoke(kind, picked);

    public void PickPortrait(Action<UnitDefinitionId?> picked) => pickPortrait(picked);

    public string ItemName(ItemDefinitionId id) => MapEditorLibrary.GetItemName(EditorGame, id);

    public string UnitName(string id) => MapEditorLibrary.GetUnitName(EditorGame, new UnitDefinitionId(id));

    private string ArmyLabel(CustomMapStack stack)
    {
        CustomMapUnit? leader = stack.Slots.FirstOrDefault(unit => unit is { Leadership: > 0 }) ?? stack.Slots.FirstOrDefault(unit => unit is not null);
        string name = leader is null ? Tr("Army") : MapEditorLibrary.GetUnitName(EditorGame, leader.TypeId);

        return Tr("{0} · {1} ({2}, {3})", name, MapEditorLabels.RaceShort(stack.Owner), stack.Position.X, stack.Position.Y);
    }
}

internal abstract partial class MapEditorBlockPart : ObservableObject
{
    // Parts that only matter for some settings (the second range of a variable test) hide when unused.
    [ObservableProperty]
    public partial bool IsHidden { get; set; }
}

internal sealed class MapEditorBlockText(string text) : MapEditorBlockPart
{
    public string Text { get; } = text;
}

// A drop-down. An empty list or a missing target shows "(none)" and the block turns red.
internal sealed partial class MapEditorBlockChoice : MapEditorBlockPart
{
    private readonly MapEditorEventContext? context;

    public MapEditorBlockChoice(IReadOnlyList<MapEditorOption<object?>> options, object? value, Action changed, MapEditorEventContext? context = null, MapEditorPickKind? pick = null)
    {
        Changed = changed;
        Options = options;
        Selected = options.FirstOrDefault(option => Equals(option.Value, value)) ?? (value is null ? options.FirstOrDefault() : null);
        this.context = context;
        Pick = pick;
    }

    // The map object kind a "Pick" button picks on the map (null: no button).
    public MapEditorPickKind? Pick { get; }

    public bool CanPick => Pick is not null && context is { CanPickOnMap: true };

    [RelayCommand]
    private void PickOnMap()
    {
        if (Pick is { } kind && context is not null)
        {
            context.PickOnMap(kind, value => Selected = Options.FirstOrDefault(option => Equals(option.Value, value)) ?? Selected);
        }
    }

    public IReadOnlyList<MapEditorOption<object?>> Options { get; }

    public object? Value => Selected?.Value;

    private Action Changed { get; }

    [ObservableProperty]
    public partial MapEditorOption<object?>? Selected { get; set; }

    partial void OnSelectedChanged(MapEditorOption<object?>? value) => Changed();
}

// A whole number; anything else keeps the last good value.
internal sealed partial class MapEditorBlockNumber(int value, int minimum, int maximum, Action changed) : MapEditorBlockPart
{
    public int Value { get; private set; } = Math.Clamp(value, minimum, maximum);

    [ObservableProperty]
    public partial string Text { get; set; } = Math.Clamp(value, minimum, maximum).ToString(CultureInfo.InvariantCulture);

    partial void OnTextChanged(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            Value = Math.Clamp(number, minimum, maximum);
            changed();
        }
    }
}

// An item: name plus a button that opens the game's item list.
internal sealed partial class MapEditorBlockItem(MapEditorEventContext context, ItemDefinitionId item, Action changed) : MapEditorBlockPart
{
    public ItemDefinitionId Item { get; private set; } = item;

    public string Name => string.IsNullOrEmpty(Item.Value) ? Tr("(choose)") : context.ItemName(Item);

    [RelayCommand]
    private void Choose()
    {
        context.PickItem(picked =>
        {
            Item = picked;
            OnPropertyChanged(nameof(Name));
            changed();
        });
    }
}

// A spell: name plus a button that opens the game's spell list.
internal sealed partial class MapEditorBlockSpell(MapEditorEventContext context, WorldSpellDefinitionId spell, Action changed) : MapEditorBlockPart
{
    public WorldSpellDefinitionId Spell { get; private set; } = spell;

    public string Name => string.IsNullOrEmpty(Spell.Value) ? Tr("(choose)") : context.SpellName(Spell);

    [RelayCommand]
    private void Choose()
    {
        context.PickSpell(picked =>
        {
            Spell = picked;
            OnPropertyChanged(nameof(Name));
            changed();
        });
    }
}

// A text the player reads: the default (English) box plus one box per added translation.
internal sealed partial class MapEditorBlockTextField : MapEditorBlockPart
{
    private readonly Action changed;

    public MapEditorBlockTextField(CustomMapText text, bool multiline, Action changed, Action<string?>? preview = null)
    {
        this.changed = changed;
        Preview = preview;
        Multiline = multiline;
        Default = text.Default;

        foreach ((string language, string translated) in text.Translations)
        {
            Translations.Add(new MapEditorTranslationViewModel(language, translated, this));
        }

        RefreshLanguages();
    }

    public bool Multiline { get; }

    // Shows the text in the game's popup: null = the default text, else that language.
    public Action<string?>? Preview { get; }

    public bool CanPreview => Preview is not null;

    public ObservableCollection<MapEditorTranslationViewModel> Translations { get; } = [];

    public ObservableCollection<string> FreeLanguages { get; } = [];

    public bool CanAddTranslation => FreeLanguages.Count > 0;

    [ObservableProperty]
    public partial string Default { get; set; }

    [ObservableProperty]
    public partial string? LanguageToAdd { get; set; }

    public CustomMapText Text => new(Default, Translations.ToImmutableSortedDictionary(row => row.Language, row => row.Text));

    partial void OnDefaultChanged(string value) => changed();

    [RelayCommand]
    private void AddTranslation()
    {
        string? language = LanguageToAdd ?? FreeLanguages.FirstOrDefault();

        if (language is null)
        {
            return;
        }

        Translations.Add(new MapEditorTranslationViewModel(language, string.Empty, this));
        RefreshLanguages();
        changed();
    }

    public void RemoveTranslation(MapEditorTranslationViewModel row)
    {
        Translations.Remove(row);
        RefreshLanguages();
        changed();
    }

    public void TranslationChanged() => changed();

    private void RefreshLanguages()
    {
        FreeLanguages.Clear();

        foreach (string language in CustomMapText.Languages.Where(language => Translations.All(row => row.Language != language)))
        {
            FreeLanguages.Add(language);
        }

        LanguageToAdd = FreeLanguages.FirstOrDefault();
        OnPropertyChanged(nameof(CanAddTranslation));
    }
}

internal sealed partial class MapEditorTranslationViewModel(string language, string text, MapEditorBlockTextField owner) : ObservableObject
{
    public string Language { get; } = language;

    public string LanguageLabel => Language.ToUpperInvariant();

    [ObservableProperty]
    public partial string Text { get; set; } = text;

    partial void OnTextChanged(string value) => owner.TranslationChanged();

    public bool CanPreview => owner.CanPreview;

    [RelayCommand]
    private void Remove() => owner.RemoveTranslation(this);

    [RelayCommand]
    private void Preview() => owner.Preview?.Invoke(Language);
}

// The message's portrait: a unit's face (as in the original maps) or none, and the side it stands on.
internal sealed partial class MapEditorBlockPortrait(MapEditorEventContext context, string? portrait, Action changed) : MapEditorBlockPart
{
    public string? Portrait { get; private set; } = portrait;

    public string Name => Portrait is null ? Tr("no portrait") : context.UnitName(Portrait);

    public bool HasPortrait => Portrait is not null;

    [RelayCommand]
    private void Choose()
    {
        context.PickPortrait(picked =>
        {
            Portrait = picked?.Value;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(HasPortrait));
            changed();
        });
    }

    [RelayCommand]
    private void Clear()
    {
        Portrait = null;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(HasPortrait));
        changed();
    }
}

// The message block's preview button (the game's own story popup).
internal sealed class MapEditorBlockPreview(Action preview) : MapEditorBlockPart
{
    public RelayCommand PreviewCommand { get; } = new(preview);
}

internal sealed partial class MapEditorEventBlockViewModel : ObservableObject
{
    private ImmutableArray<WorldRace> revealRaces = [];

    private MapEditorEventBlockViewModel(MapEditorBlockKind kind)
    {
        Kind = kind;
    }

    public MapEditorBlockKind Kind { get; }

    public MapEditorBlockCategory Category => GetCategory(Kind);

    public bool IsCondition => IsConditionKind(Kind);

    public IBrush Brush => GetBrush(Category);

    public ObservableCollection<MapEditorBlockPart> Parts { get; } = [];

    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    public static bool IsConditionKind(MapEditorBlockKind kind) => kind < MapEditorBlockKind.Message;

    public static MapEditorBlockCategory GetCategory(MapEditorBlockKind kind)
    {
        return kind switch
        {
            MapEditorBlockKind.OnTurn or MapEditorBlockKind.EveryTurns => MapEditorBlockCategory.Time,
            MapEditorBlockKind.EnterZone or MapEditorBlockKind.RevealZone or MapEditorBlockKind.ArmyToZone or MapEditorBlockKind.ItemToZone or
                MapEditorBlockKind.RemoveMountains or MapEditorBlockKind.RemoveLandmark or MapEditorBlockKind.ChangeLandmark or MapEditorBlockKind.ChangeTerrain => MapEditorBlockCategory.Zones,
            MapEditorBlockKind.OwnCity or MapEditorBlockKind.EnterCity or MapEditorBlockKind.LootRuin or MapEditorBlockKind.ArmyToCity or MapEditorBlockKind.VisitSite or
                MapEditorBlockKind.Land => MapEditorBlockCategory.Places,
            MapEditorBlockKind.DestroyArmy or MapEditorBlockKind.ArmyExists or (>= MapEditorBlockKind.CreateArmy and <= MapEditorBlockKind.Battle) => MapEditorBlockCategory.Armies,
            MapEditorBlockKind.GiveItem or MapEditorBlockKind.OwnItem or MapEditorBlockKind.LeaderItem or MapEditorBlockKind.DestroyItem or MapEditorBlockKind.CastSpell or
                MapEditorBlockKind.CastSpellZone or MapEditorBlockKind.GiveSpell => MapEditorBlockCategory.Items,
            MapEditorBlockKind.Message or MapEditorBlockKind.Objective => MapEditorBlockCategory.Messages,
            _ => MapEditorBlockCategory.Flow
        };
    }

    // Muted versions of Scratch's category colours, dark enough for the game's light text.
    public static IBrush GetBrush(MapEditorBlockCategory category)
    {
        return category switch
        {
            MapEditorBlockCategory.Time => new SolidColorBrush(Color.FromRgb(0x2F, 0x55, 0x8C)),
            MapEditorBlockCategory.Zones => new SolidColorBrush(Color.FromRgb(0x2F, 0x6E, 0x3E)),
            MapEditorBlockCategory.Places => new SolidColorBrush(Color.FromRgb(0x8A, 0x67, 0x1E)),
            MapEditorBlockCategory.Armies => new SolidColorBrush(Color.FromRgb(0x8C, 0x2F, 0x2F)),
            MapEditorBlockCategory.Items => new SolidColorBrush(Color.FromRgb(0x5E, 0x3A, 0x84)),
            MapEditorBlockCategory.Messages => new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x62)),
            _ => new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x3E))
        };
    }

    public static IReadOnlyList<MapEditorPaletteGroup> CreatePalette(bool conditions)
    {
        MapEditorPaletteBlock Block(MapEditorBlockKind kind, string label) => new(kind, label, GetCategory(kind));

        return conditions
            ?
            [
                new MapEditorPaletteGroup(Tr("Time"), [Block(MapEditorBlockKind.OnTurn, Tr("on turn N")), Block(MapEditorBlockKind.EveryTurns, Tr("every N turns"))]),
                new MapEditorPaletteGroup(Tr("Zones"),
                [
                    Block(MapEditorBlockKind.EnterZone, Tr("an army enters a zone")),
                    Block(MapEditorBlockKind.ArmyToZone, Tr("a certain army is in a zone")),
                    Block(MapEditorBlockKind.ItemToZone, Tr("an item is brought to a zone"))
                ]),
                new MapEditorPaletteGroup(Tr("Places"),
                [
                    Block(MapEditorBlockKind.OwnCity, Tr("owns a city")),
                    Block(MapEditorBlockKind.EnterCity, Tr("an army is in a city")),
                    Block(MapEditorBlockKind.ArmyToCity, Tr("a certain army is in a city")),
                    Block(MapEditorBlockKind.LootRuin, Tr("loots a ruin")),
                    Block(MapEditorBlockKind.VisitSite, Tr("visits a site")),
                    Block(MapEditorBlockKind.Land, Tr("owns a share of the land"))
                ]),
                new MapEditorPaletteGroup(Tr("Armies"), [Block(MapEditorBlockKind.DestroyArmy, Tr("destroys an army")), Block(MapEditorBlockKind.ArmyExists, Tr("an army exists"))]),
                new MapEditorPaletteGroup(Tr("Items"), [Block(MapEditorBlockKind.OwnItem, Tr("owns an item")), Block(MapEditorBlockKind.LeaderItem, Tr("an army carries an item"))]),
                new MapEditorPaletteGroup(Tr("Game"),
                [
                    Block(MapEditorBlockKind.Variable, Tr("a variable is in a range")),
                    Block(MapEditorBlockKind.Relation, Tr("relations between races")),
                    Block(MapEditorBlockKind.Alliance, Tr("two races are allies"))
                ])
            ]
            :
            [
                new MapEditorPaletteGroup(Tr("Messages"), [Block(MapEditorBlockKind.Message, Tr("show a message")), Block(MapEditorBlockKind.Objective, Tr("change the objective"))]),
                new MapEditorPaletteGroup(Tr("Game"),
                [
                    Block(MapEditorBlockKind.Win, Tr("a race wins")),
                    Block(MapEditorBlockKind.Lose, Tr("the players lose")),
                    Block(MapEditorBlockKind.SwitchEvent, Tr("switch an event on or off")),
                    Block(MapEditorBlockKind.SetVariable, Tr("change a variable")),
                    Block(MapEditorBlockKind.Ally, Tr("make two races allies")),
                    Block(MapEditorBlockKind.SetRelation, Tr("set relations between races"))
                ]),
                new MapEditorPaletteGroup(Tr("Armies"),
                [
                    Block(MapEditorBlockKind.CreateArmy, Tr("create an army")),
                    Block(MapEditorBlockKind.Battle, Tr("an army attacks")),
                    Block(MapEditorBlockKind.MoveArmy, Tr("move an army to a zone")),
                    Block(MapEditorBlockKind.MoveBeside, Tr("move an army next to it")),
                    Block(MapEditorBlockKind.RemoveArmy, Tr("remove an army")),
                    Block(MapEditorBlockKind.ChangeOwner, Tr("an army changes sides")),
                    Block(MapEditorBlockKind.OrderSimple, Tr("give an order")),
                    Block(MapEditorBlockKind.OrderGoTo, Tr("order: go to a zone")),
                    Block(MapEditorBlockKind.OrderDefendZone, Tr("order: defend a zone")),
                    Block(MapEditorBlockKind.OrderDefendCity, Tr("order: defend a city")),
                    Block(MapEditorBlockKind.OrderSecureCity, Tr("order: capture a city")),
                    Block(MapEditorBlockKind.OrderAttack, Tr("order: attack an army"))
                ]),
                new MapEditorPaletteGroup(Tr("Zones"),
                [
                    Block(MapEditorBlockKind.RevealZone, Tr("reveal a zone")),
                    Block(MapEditorBlockKind.ChangeTerrain, Tr("change the land in a zone")),
                    Block(MapEditorBlockKind.RemoveMountains, Tr("remove mountains in a zone")),
                    Block(MapEditorBlockKind.RemoveLandmark, Tr("remove a landmark")),
                    Block(MapEditorBlockKind.ChangeLandmark, Tr("change a landmark"))
                ]),
                new MapEditorPaletteGroup(Tr("Items and spells"),
                [
                    Block(MapEditorBlockKind.GiveItem, Tr("give an item")),
                    Block(MapEditorBlockKind.DestroyItem, Tr("destroy an item")),
                    Block(MapEditorBlockKind.GiveSpell, Tr("teach a spell")),
                    Block(MapEditorBlockKind.CastSpell, Tr("cast a spell on the army")),
                    Block(MapEditorBlockKind.CastSpellZone, Tr("cast a spell on a zone"))
                ])
            ];
    }

    // A new block from the palette, with the first target of each list.
    public static MapEditorEventBlockViewModel Create(MapEditorBlockKind kind, MapEditorEventContext context, Action changed, Action<MapEditorEventBlockViewModel, string?> preview)
    {
        return kind switch
        {
            MapEditorBlockKind.OnTurn => FromCondition(new CustomMapTurnCondition(5, Repeat: false), context, changed),
            MapEditorBlockKind.EveryTurns => FromCondition(new CustomMapTurnCondition(5, Repeat: true), context, changed),
            MapEditorBlockKind.EnterZone => FromCondition(new CustomMapEnterZoneCondition(First<int>(context.Locations)), context, changed),
            MapEditorBlockKind.OwnCity => FromCondition(new CustomMapOwnCityCondition(FirstCity(context)), context, changed),
            MapEditorBlockKind.EnterCity => FromCondition(new CustomMapEnterCityCondition(FirstCity(context)), context, changed),
            MapEditorBlockKind.LootRuin => FromCondition(new CustomMapLootRuinCondition(First<int>(context.Ruins)), context, changed),
            MapEditorBlockKind.DestroyArmy => FromCondition(new CustomMapDestroyArmyCondition(First<int>(context.Armies)), context, changed),
            MapEditorBlockKind.ArmyExists => FromCondition(new CustomMapArmyExistsCondition(FirstArmy(context, false), true), context, changed),
            MapEditorBlockKind.OwnItem => FromCondition(new CustomMapOwnItemCondition(new ItemDefinitionId(string.Empty)), context, changed),
            MapEditorBlockKind.LeaderItem => FromCondition(new CustomMapLeaderItemCondition(FirstArmy(context, false), new ItemDefinitionId(string.Empty)), context, changed),
            MapEditorBlockKind.Relation => FromCondition(new CustomMapRelationCondition(FirstRace(context, 0), FirstRace(context, 1), CustomMapRelation.War), context, changed),
            MapEditorBlockKind.Alliance => FromCondition(new CustomMapAllianceCondition(FirstRace(context, 0), FirstRace(context, 1)), context, changed),
            MapEditorBlockKind.Land => FromCondition(new CustomMapLandCondition(40), context, changed),
            MapEditorBlockKind.VisitSite => FromCondition(new CustomMapVisitSiteCondition(First<int>(context.Sites)), context, changed),
            MapEditorBlockKind.ArmyToZone => FromCondition(new CustomMapArmyToZoneCondition(FirstArmy(context, false), First<int>(context.Locations)), context, changed),
            MapEditorBlockKind.ArmyToCity => FromCondition(new CustomMapArmyToCityCondition(FirstArmy(context, false), FirstCity(context)), context, changed),
            MapEditorBlockKind.ItemToZone => FromCondition(new CustomMapItemToZoneCondition(new ItemDefinitionId(string.Empty), First<int>(context.Locations)), context, changed),
            MapEditorBlockKind.Variable => FromCondition(new CustomMapVariableCondition(First<int>(context.Variables), 1, 1), context, changed),
            MapEditorBlockKind.Message => FromEffect(new CustomMapMessageEffect(CustomMapText.Of(string.Empty), CustomMapMessageAudience.Triggerer, null, true), context, changed, preview),
            MapEditorBlockKind.Objective => FromEffect(new CustomMapObjectiveEffect(CustomMapText.Of(string.Empty)), context, changed, preview),
            MapEditorBlockKind.Win => FromEffect(new CustomMapWinEffect(context.Map.Players.FirstOrDefault()?.Race ?? WorldRace.Human), context, changed, preview),
            MapEditorBlockKind.Lose => FromEffect(new CustomMapLoseEffect(), context, changed, preview),
            MapEditorBlockKind.SwitchEvent => FromEffect(new CustomMapSwitchEventEffect(First<int>(context.Events), On: true), context, changed, preview),
            MapEditorBlockKind.RevealZone => FromEffect(new CustomMapRevealZoneEffect(First<int>(context.Locations), []), context, changed, preview),
            MapEditorBlockKind.GiveItem => FromEffect(new CustomMapGiveItemEffect(new ItemDefinitionId(string.Empty), ToCapital: false), context, changed, preview),
            MapEditorBlockKind.CreateArmy => FromEffect(new CustomMapCreateArmyEffect(First<int>(context.Templates), First<int>(context.Locations)), context, changed, preview),
            MapEditorBlockKind.RemoveArmy => FromEffect(new CustomMapRemoveArmyEffect(FirstArmy(context, false)), context, changed, preview),
            MapEditorBlockKind.MoveArmy => FromEffect(new CustomMapMoveArmyEffect(new CustomMapArmyRef.Triggerer(), First<int>(context.Locations)), context, changed, preview),
            MapEditorBlockKind.MoveBeside => FromEffect(new CustomMapMoveBesideEffect(FirstArmy(context, false)), context, changed, preview),
            MapEditorBlockKind.ChangeOwner => FromEffect(new CustomMapChangeOwnerEffect(FirstArmy(context, false), null), context, changed, preview),
            MapEditorBlockKind.OrderSimple => FromEffect(new CustomMapChangeOrderEffect(FirstArmy(context, false), new CustomMapArmyOrder(CustomMapOrderKind.Stand)), context, changed, preview),
            MapEditorBlockKind.OrderGoTo => FromEffect(new CustomMapChangeOrderEffect(FirstArmy(context, false), MapEditorOrders.Make(CustomMapOrderKind.MoveToZone, context.Locations.FirstOrDefault()?.Value)), context, changed, preview),
            MapEditorBlockKind.OrderDefendZone => FromEffect(new CustomMapChangeOrderEffect(FirstArmy(context, false), MapEditorOrders.Make(CustomMapOrderKind.DefendZone, context.Locations.FirstOrDefault()?.Value)), context, changed, preview),
            MapEditorBlockKind.OrderDefendCity => FromEffect(new CustomMapChangeOrderEffect(FirstArmy(context, false), MapEditorOrders.Make(CustomMapOrderKind.DefendCity, FirstCity(context))), context, changed, preview),
            MapEditorBlockKind.OrderSecureCity => FromEffect(new CustomMapChangeOrderEffect(FirstArmy(context, false), MapEditorOrders.Make(CustomMapOrderKind.SecureCity, FirstCity(context))), context, changed, preview),
            MapEditorBlockKind.OrderAttack => FromEffect(new CustomMapChangeOrderEffect(FirstArmy(context, false), MapEditorOrders.Make(CustomMapOrderKind.AttackArmy, context.Armies.FirstOrDefault()?.Value)), context, changed, preview),
            MapEditorBlockKind.Battle => FromEffect(new CustomMapBattleEffect(FirstArmy(context, false)), context, changed, preview),
            MapEditorBlockKind.CastSpell => FromEffect(new CustomMapCastSpellEffect(new WorldSpellDefinitionId(string.Empty), null), context, changed, preview),
            MapEditorBlockKind.CastSpellZone => FromEffect(new CustomMapCastSpellZoneEffect(new WorldSpellDefinitionId(string.Empty), First<int>(context.Locations), null), context, changed, preview),
            MapEditorBlockKind.GiveSpell => FromEffect(new CustomMapGiveSpellEffect(new WorldSpellDefinitionId(string.Empty)), context, changed, preview),
            MapEditorBlockKind.Ally => FromEffect(new CustomMapAllyEffect(FirstRace(context, 0), FirstRace(context, 1), Permanent: false), context, changed, preview),
            MapEditorBlockKind.SetRelation => FromEffect(new CustomMapRelationEffect(FirstRace(context, 0), FirstRace(context, 1), CustomMapRelation.War), context, changed, preview),
            MapEditorBlockKind.RemoveMountains => FromEffect(new CustomMapRemoveMountainsEffect(First<int>(context.Locations)), context, changed, preview),
            MapEditorBlockKind.RemoveLandmark => FromEffect(new CustomMapRemoveLandmarkEffect(First<int>(context.PlacedLandmarks)), context, changed, preview),
            MapEditorBlockKind.ChangeLandmark => FromEffect(new CustomMapChangeLandmarkEffect(First<int>(context.PlacedLandmarks), context.LandmarkTypes.FirstOrDefault()?.Value is WorldLandmarkDefinitionId type ? type : new WorldLandmarkDefinitionId(string.Empty)), context, changed, preview),
            MapEditorBlockKind.ChangeTerrain => FromEffect(new CustomMapChangeTerrainEffect(First<int>(context.Locations), context.Map.Players.FirstOrDefault() is { } player ? CustomMap.GetRaceTerrain(player.Race) : WorldMapTerrain.Neutral, 5), context, changed, preview),
            MapEditorBlockKind.SetVariable => FromEffect(new CustomMapVariableEffect(First<int>(context.Variables), ScenarioVariableOperation.Add, 1), context, changed, preview),
            MapEditorBlockKind.DestroyItem => FromEffect(new CustomMapDestroyItemEffect(new ItemDefinitionId(string.Empty), TriggererOnly: true), context, changed, preview),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public static MapEditorEventBlockViewModel FromCondition(CustomMapCondition condition, MapEditorEventContext context, Action changed)
    {
        switch (condition)
        {
            case CustomMapTurnCondition turn:
                return Make(turn.Repeat ? MapEditorBlockKind.EveryTurns : MapEditorBlockKind.OnTurn, turn.Repeat ? Tr("every") : Tr("on turn"), new MapEditorBlockNumber(turn.Turn, 1, 999, changed), turn.Repeat ? new MapEditorBlockText(Tr("turns")) : null);
            case CustomMapEnterZoneCondition zone:
                return Make(MapEditorBlockKind.EnterZone, Tr("an army enters"), new MapEditorBlockChoice(context.Locations, zone.LocationId, changed, context, MapEditorPickKind.Zone));
            case CustomMapOwnCityCondition own:
                return Make(MapEditorBlockKind.OwnCity, Tr("owns"), new MapEditorBlockChoice(context.Cities, own.City, changed, context, MapEditorPickKind.City));
            case CustomMapEnterCityCondition enter:
                return Make(MapEditorBlockKind.EnterCity, Tr("an army is in"), new MapEditorBlockChoice(context.Cities, enter.City, changed, context, MapEditorPickKind.City), new MapEditorBlockText(Tr("(their own city)")));
            case CustomMapLootRuinCondition loot:
                return Make(MapEditorBlockKind.LootRuin, Tr("loots"), new MapEditorBlockChoice(context.Ruins, loot.RuinId, changed, context, MapEditorPickKind.Ruin));
            case CustomMapDestroyArmyCondition destroy:
                return Make(MapEditorBlockKind.DestroyArmy, Tr("destroys"), ArmyChoice(context, destroy.Army, false, changed));
            case CustomMapOwnItemCondition own:
                return Make(MapEditorBlockKind.OwnItem, Tr("owns"), new MapEditorBlockItem(context, own.Item, changed));
            case CustomMapLeaderItemCondition leader:
                return Make(MapEditorBlockKind.LeaderItem, null, ArmyChoice(context, leader.Army, false, changed), new MapEditorBlockText(Tr("carries")), new MapEditorBlockItem(context, leader.Item, changed));
            case CustomMapRelationCondition relation:
                return Make(MapEditorBlockKind.Relation, Tr("relations of"), RaceChoice(context, relation.Race1, changed), new MapEditorBlockText(Tr("and")), RaceChoice(context, relation.Race2, changed), new MapEditorBlockText(Tr("are")), RelationChoice(relation.Level, changed));
            case CustomMapAllianceCondition alliance:
                return Make(MapEditorBlockKind.Alliance, null, RaceChoice(context, alliance.Race1, changed), new MapEditorBlockText(Tr("and")), RaceChoice(context, alliance.Race2, changed), new MapEditorBlockText(Tr("are allies")));
            case CustomMapLandCondition land:
                return Make(MapEditorBlockKind.Land, Tr("owns at least"), new MapEditorBlockNumber(land.Percent, 1, 100, changed), new MapEditorBlockText(Tr("% of the land")));
            case CustomMapVisitSiteCondition visit:
                return Make(MapEditorBlockKind.VisitSite, Tr("visits"), new MapEditorBlockChoice(context.Sites, visit.SiteId, changed, context, MapEditorPickKind.Site));
            case CustomMapArmyToZoneCondition toZone:
                return Make(MapEditorBlockKind.ArmyToZone, null, ArmyChoice(context, toZone.Army, false, changed), new MapEditorBlockText(Tr("is in")), new MapEditorBlockChoice(context.Locations, toZone.LocationId, changed, context, MapEditorPickKind.Zone));
            case CustomMapArmyToCityCondition toCity:
                return Make(MapEditorBlockKind.ArmyToCity, null, ArmyChoice(context, toCity.Army, false, changed), new MapEditorBlockText(Tr("is in")), new MapEditorBlockChoice(context.Cities, toCity.City, changed, context, MapEditorPickKind.City));
            case CustomMapItemToZoneCondition item:
                return Make(MapEditorBlockKind.ItemToZone, Tr("an army brings"), new MapEditorBlockItem(context, item.Item, changed), new MapEditorBlockText(Tr("into")), new MapEditorBlockChoice(context.Locations, item.LocationId, changed, context, MapEditorPickKind.Zone));
            case CustomMapVariableCondition variable:
            {
                MapEditorBlockPart[] second =
                [
                    new MapEditorBlockChoice(context.Variables, variable.SecondVariableId == 0 ? null : variable.SecondVariableId, changed),
                    new MapEditorBlockText(Tr("is from")),
                    new MapEditorBlockNumber(variable.SecondMinimum, -9999, 9999, changed),
                    new MapEditorBlockText(Tr("to")),
                    new MapEditorBlockNumber(variable.SecondMaximum, -9999, 9999, changed)
                ];
                MapEditorBlockChoice? join = null;

                void JoinChanged()
                {
                    foreach (MapEditorBlockPart part in second)
                    {
                        part.IsHidden = join?.Value is not (CustomMapVariableJoin.And or CustomMapVariableJoin.Or);
                    }

                    changed();
                }

                join = new MapEditorBlockChoice(
                    [new MapEditorOption<object?>(CustomMapVariableJoin.None, Tr("(nothing else)")), new MapEditorOption<object?>(CustomMapVariableJoin.And, Tr("and")), new MapEditorOption<object?>(CustomMapVariableJoin.Or, Tr("or"))],
                    variable.Join,
                    JoinChanged);

                foreach (MapEditorBlockPart part in second)
                {
                    part.IsHidden = variable.Join == CustomMapVariableJoin.None;
                }

                return Make(
                    MapEditorBlockKind.Variable,
                    null,
                    [
                        new MapEditorBlockChoice(context.Variables, variable.VariableId, changed),
                        new MapEditorBlockText(Tr("is from")),
                        new MapEditorBlockNumber(variable.Minimum, -9999, 9999, changed),
                        new MapEditorBlockText(Tr("to")),
                        new MapEditorBlockNumber(variable.Maximum, -9999, 9999, changed),
                        join,
                        .. second
                    ]);
            }
            case CustomMapArmyExistsCondition exists:
                return Make(
                    MapEditorBlockKind.ArmyExists,
                    null,
                    ArmyChoice(context, exists.Army, false, changed),
                    new MapEditorBlockChoice([new MapEditorOption<object?>(true, Tr("still exists")), new MapEditorOption<object?>(false, Tr("is gone"))], exists.Exists, changed));
            default:
                throw new ArgumentOutOfRangeException(nameof(condition), condition, null);
        }
    }

    public static MapEditorEventBlockViewModel FromEffect(CustomMapEffect effect, MapEditorEventContext context, Action changed, Action<MapEditorEventBlockViewModel, string?> preview)
    {
        switch (effect)
        {
            case CustomMapMessageEffect message:
            {
                MapEditorEventBlockViewModel block = new(MapEditorBlockKind.Message);
                block.Parts.Add(new MapEditorBlockText(Tr("show")));
                block.Parts.Add(new MapEditorBlockChoice(
                    [
                        new MapEditorOption<object?>(CustomMapMessageAudience.Triggerer, Tr("the player who fired it")),
                        new MapEditorOption<object?>(CustomMapMessageAudience.AllPlayers, Tr("all players")),
                        new MapEditorOption<object?>(CustomMapMessageAudience.ConcernedRaces, Tr("the races concerned"))
                    ],
                    message.Audience,
                    changed));
                block.Parts.Add(new MapEditorBlockTextField(message.Text, multiline: true, changed, language => preview(block, language)));
                block.Parts.Add(new MapEditorBlockPortrait(context, message.Portrait, changed));
                block.Parts.Add(new MapEditorBlockChoice([new MapEditorOption<object?>(true, Tr("on the left")), new MapEditorOption<object?>(false, Tr("on the right"))], message.PortraitLeft, changed));
                block.Parts.Add(new MapEditorBlockPreview(() => preview(block, null)));

                return block;
            }

            case CustomMapObjectiveEffect objective:
                return Make(MapEditorBlockKind.Objective, Tr("new objective"), new MapEditorBlockTextField(objective.Text, multiline: false, changed));
            case CustomMapWinEffect win:
                return Make(MapEditorBlockKind.Win, null, new MapEditorBlockChoice(context.PlayableRaces, win.Winner, changed), new MapEditorBlockText(Tr("wins")));
            case CustomMapLoseEffect:
                return Make(MapEditorBlockKind.Lose, Tr("the players lose"));
            case CustomMapSwitchEventEffect switchEvent:
                return Make(
                    MapEditorBlockKind.SwitchEvent,
                    Tr("switch"),
                    new MapEditorBlockChoice(context.Events, switchEvent.EventId, changed),
                    new MapEditorBlockChoice([new MapEditorOption<object?>(true, Tr("on")), new MapEditorOption<object?>(false, Tr("off"))], switchEvent.On, changed));
            case CustomMapRevealZoneEffect reveal:
            {
                // The block reveals for the event's races; a race list set elsewhere is kept as it was.
                MapEditorEventBlockViewModel block = Make(
                    MapEditorBlockKind.RevealZone,
                    Tr("reveal"),
                    new MapEditorBlockChoice(context.Locations, reveal.LocationId, changed, context, MapEditorPickKind.Zone),
                    new MapEditorBlockText(Tr("to")),
                    new MapEditorBlockChoice([new MapEditorOption<object?>(null, Tr("the event races")), .. context.PlayableRaces], reveal.Races.Length == 1 ? reveal.Races[0] : null, changed));
                block.revealRaces = reveal.Races;

                return block;
            }
            case CustomMapGiveItemEffect give:
                return Make(
                    MapEditorBlockKind.GiveItem,
                    Tr("give"),
                    new MapEditorBlockItem(context, give.Item, changed),
                    new MapEditorBlockChoice([new MapEditorOption<object?>(false, Tr("to the army")), new MapEditorOption<object?>(true, Tr("to its capital"))], give.ToCapital, changed));
            case CustomMapCreateArmyEffect create:
                return Make(MapEditorBlockKind.CreateArmy, Tr("create"), new MapEditorBlockChoice(context.Templates, create.TemplateId, changed), new MapEditorBlockText(Tr("at")), ZoneChoice(context, create.LocationId, changed));
            case CustomMapRemoveArmyEffect remove:
                return Make(MapEditorBlockKind.RemoveArmy, Tr("remove"), ArmyChoice(context, remove.Army, false, changed));
            case CustomMapMoveArmyEffect move:
                return Make(MapEditorBlockKind.MoveArmy, Tr("move"), ArmyChoice(context, move.Army, true, changed), new MapEditorBlockText(Tr("to")), ZoneChoice(context, move.LocationId, changed));
            case CustomMapMoveBesideEffect beside:
                return Make(MapEditorBlockKind.MoveBeside, Tr("move"), ArmyChoice(context, beside.Army, false, changed), new MapEditorBlockText(Tr("next to the army that fired it")));
            case CustomMapChangeOwnerEffect owner:
                return Make(
                    MapEditorBlockKind.ChangeOwner,
                    null,
                    ArmyChoice(context, owner.Army, false, changed),
                    new MapEditorBlockText(Tr("goes over to")),
                    new MapEditorBlockChoice(
                        [new MapEditorOption<object?>(null, Tr("the player who fired it")), .. context.PlayableRaces, new MapEditorOption<object?>(WorldRace.Neutral, MapEditorLabels.RaceShort(WorldRace.Neutral))],
                        owner.NewOwner,
                        changed));
            case CustomMapChangeOrderEffect order:
                return OrderBlock(order, context, changed);
            case CustomMapBattleEffect battle:
                return Make(MapEditorBlockKind.Battle, null, ArmyChoice(context, battle.Army, false, changed), new MapEditorBlockText(Tr("attacks the army that fired it")));
            case CustomMapCastSpellEffect cast:
                return Make(MapEditorBlockKind.CastSpell, Tr("cast"), new MapEditorBlockSpell(context, cast.Spell, changed), new MapEditorBlockText(Tr("on the army that fired it, by")), CasterChoice(context, cast.Caster, changed));
            case CustomMapCastSpellZoneEffect cast:
                return Make(MapEditorBlockKind.CastSpellZone, Tr("cast"), new MapEditorBlockSpell(context, cast.Spell, changed), new MapEditorBlockText(Tr("on")), new MapEditorBlockChoice(context.Locations, cast.LocationId, changed, context, MapEditorPickKind.Zone), new MapEditorBlockText(Tr("by")), CasterChoice(context, cast.Caster, changed));
            case CustomMapGiveSpellEffect give:
                return Make(MapEditorBlockKind.GiveSpell, Tr("teach"), new MapEditorBlockSpell(context, give.Spell, changed), new MapEditorBlockText(Tr("to the player who fired it")));
            case CustomMapAllyEffect ally:
                return Make(
                    MapEditorBlockKind.Ally,
                    Tr("make"),
                    RaceChoice(context, ally.Race1, changed),
                    new MapEditorBlockText(Tr("and")),
                    RaceChoice(context, ally.Race2, changed),
                    new MapEditorBlockChoice([new MapEditorOption<object?>(false, Tr("allies")), new MapEditorOption<object?>(true, Tr("allies for good"))], ally.Permanent, changed));
            case CustomMapRelationEffect relation:
                return Make(MapEditorBlockKind.SetRelation, Tr("set relations of"), RaceChoice(context, relation.Race1, changed), new MapEditorBlockText(Tr("and")), RaceChoice(context, relation.Race2, changed), new MapEditorBlockText(Tr("to")), RelationChoice(relation.Level, changed));
            case CustomMapRemoveMountainsEffect mountains:
                return Make(MapEditorBlockKind.RemoveMountains, Tr("remove the mountains in"), new MapEditorBlockChoice(context.Locations, mountains.LocationId, changed, context, MapEditorPickKind.Zone));
            case CustomMapRemoveLandmarkEffect remove:
                return Make(MapEditorBlockKind.RemoveLandmark, Tr("remove"), new MapEditorBlockChoice(context.PlacedLandmarks, remove.LandmarkId, changed, context, MapEditorPickKind.Landmark));
            case CustomMapChangeLandmarkEffect change:
                return Make(MapEditorBlockKind.ChangeLandmark, Tr("turn"), new MapEditorBlockChoice(context.PlacedLandmarks, change.LandmarkId, changed, context, MapEditorPickKind.Landmark), new MapEditorBlockText(Tr("into")), new MapEditorBlockChoice(context.LandmarkTypes, change.Type, changed));
            case CustomMapChangeTerrainEffect terrain:
                return Make(
                    MapEditorBlockKind.ChangeTerrain,
                    Tr("turn a square of"),
                    new MapEditorBlockNumber(terrain.Size, 1, 15, changed),
                    new MapEditorBlockText(Tr("tiles around")),
                    new MapEditorBlockChoice(context.Locations, terrain.LocationId, changed, context, MapEditorPickKind.Zone),
                    new MapEditorBlockText(Tr("into")),
                    new MapEditorBlockChoice(context.Terrains, terrain.Terrain, changed));
            case CustomMapVariableEffect variable:
                return Make(
                    MapEditorBlockKind.SetVariable,
                    null,
                    new MapEditorBlockChoice(context.Variables, variable.VariableId, changed),
                    new MapEditorBlockChoice(
                        [
                            new MapEditorOption<object?>(ScenarioVariableOperation.Add, "+"),
                            new MapEditorOption<object?>(ScenarioVariableOperation.Subtract, "-"),
                            new MapEditorOption<object?>(ScenarioVariableOperation.Multiply, "×"),
                            new MapEditorOption<object?>(ScenarioVariableOperation.Divide, "÷"),
                            new MapEditorOption<object?>(ScenarioVariableOperation.Replace, "=")
                        ],
                        variable.Operation,
                        changed),
                    new MapEditorBlockNumber(variable.Value, -9999, 9999, changed));
            case CustomMapDestroyItemEffect destroy:
                return Make(
                    MapEditorBlockKind.DestroyItem,
                    Tr("destroy"),
                    new MapEditorBlockItem(context, destroy.Item, changed),
                    new MapEditorBlockChoice([new MapEditorOption<object?>(true, Tr("carried by the army that fired it")), new MapEditorOption<object?>(false, Tr("everywhere"))], destroy.TriggererOnly, changed));
            default:
                throw new ArgumentOutOfRangeException(nameof(effect), effect, null);
        }
    }

    private static MapEditorEventBlockViewModel OrderBlock(CustomMapChangeOrderEffect order, MapEditorEventContext context, Action changed)
    {
        MapEditorBlockChoice army = ArmyChoice(context, order.Army, false, changed);

        return order.Order.Kind switch
        {
            CustomMapOrderKind.MoveToZone => Make(MapEditorBlockKind.OrderGoTo, Tr("order"), army, new MapEditorBlockText(Tr("to go to")), ZoneChoice(context, order.Order.LocationId, changed)),
            CustomMapOrderKind.DefendZone => Make(MapEditorBlockKind.OrderDefendZone, Tr("order"), army, new MapEditorBlockText(Tr("to defend")), ZoneChoice(context, order.Order.LocationId, changed)),
            CustomMapOrderKind.DefendCity => Make(MapEditorBlockKind.OrderDefendCity, Tr("order"), army, new MapEditorBlockText(Tr("to defend")), new MapEditorBlockChoice(context.Cities, order.Order.City, changed, context, MapEditorPickKind.City)),
            CustomMapOrderKind.SecureCity => Make(MapEditorBlockKind.OrderSecureCity, Tr("order"), army, new MapEditorBlockText(Tr("to capture")), new MapEditorBlockChoice(context.Cities, order.Order.City, changed, context, MapEditorPickKind.City)),
            CustomMapOrderKind.AttackArmy => Make(MapEditorBlockKind.OrderAttack, Tr("order"), army, new MapEditorBlockText(Tr("to attack")), new MapEditorBlockChoice(context.Armies, order.Order.ArmyId, changed, context, MapEditorPickKind.Army)),
            _ => Make(
                MapEditorBlockKind.OrderSimple,
                Tr("order"),
                army,
                new MapEditorBlockText(Tr("to")),
                new MapEditorBlockChoice([.. MapEditorOrders.Kinds.Where(kind => !MapEditorOrders.NeedsTarget(kind.Value)).Select(kind => new MapEditorOption<object?>(kind.Value, kind.Label))], order.Order.Kind, changed))
        };
    }

    // The second range only counts with "and" / "or"; without it the condition keeps zeros there.
    private CustomMapVariableCondition VariableCondition()
    {
        CustomMapVariableJoin join = Choice(1) is CustomMapVariableJoin value ? value : CustomMapVariableJoin.None;

        return join == CustomMapVariableJoin.None
            ? new CustomMapVariableCondition(ChoiceInt(0), Number(0), Number(1))
            : new CustomMapVariableCondition(ChoiceInt(0), Number(0), Number(1), join, ChoiceInt(2), Number(2), Number(3));
    }

    private static MapEditorBlockChoice RaceChoice(MapEditorEventContext context, WorldRace race, Action changed)
    {
        return new MapEditorBlockChoice(context.PlayableRaces, race, changed);
    }

    private static MapEditorBlockChoice RelationChoice(CustomMapRelation level, Action changed)
    {
        return new MapEditorBlockChoice(
            [new MapEditorOption<object?>(CustomMapRelation.War, Tr("at war")), new MapEditorOption<object?>(CustomMapRelation.Neutral, Tr("neutral")), new MapEditorOption<object?>(CustomMapRelation.Peace, Tr("at peace"))],
            level,
            changed);
    }

    private static MapEditorBlockChoice CasterChoice(MapEditorEventContext context, WorldRace? caster, Action changed)
    {
        return new MapEditorBlockChoice([new MapEditorOption<object?>(null, Tr("nobody")), .. context.PlayableRaces], caster, changed);
    }

    private static WorldRace FirstRace(MapEditorEventContext context, int index)
    {
        return context.Map.Players.ElementAtOrDefault(index)?.Race ?? context.Map.Players.FirstOrDefault()?.Race ?? WorldRace.Human;
    }

    private static MapEditorBlockChoice ZoneChoice(MapEditorEventContext context, int locationId, Action changed)
    {
        return new MapEditorBlockChoice(context.Locations, locationId, changed, context, MapEditorPickKind.Zone);
    }

    private static MapEditorBlockChoice ArmyChoice(MapEditorEventContext context, CustomMapArmyRef army, bool triggerer, Action changed)
    {
        return new MapEditorBlockChoice(context.ArmyRefs(triggerer), army, changed, context, MapEditorPickKind.ArmyRef);
    }

    private static CustomMapArmyRef FirstArmy(MapEditorEventContext context, bool triggerer)
    {
        return context.ArmyRefs(triggerer).FirstOrDefault()?.Value as CustomMapArmyRef ?? new CustomMapArmyRef.Army(0);
    }

    public CustomMapCondition ToCondition()
    {
        return Kind switch
        {
            MapEditorBlockKind.OnTurn => new CustomMapTurnCondition(Number(0), Repeat: false),
            MapEditorBlockKind.EveryTurns => new CustomMapTurnCondition(Number(0), Repeat: true),
            MapEditorBlockKind.EnterZone => new CustomMapEnterZoneCondition(ChoiceInt(0)),
            MapEditorBlockKind.OwnCity => new CustomMapOwnCityCondition(ChoiceCity(0)),
            MapEditorBlockKind.EnterCity => new CustomMapEnterCityCondition(ChoiceCity(0)),
            MapEditorBlockKind.LootRuin => new CustomMapLootRuinCondition(ChoiceInt(0)),
            MapEditorBlockKind.DestroyArmy => new CustomMapDestroyArmyCondition(ChoiceArmy(0)),
            MapEditorBlockKind.ArmyExists => new CustomMapArmyExistsCondition(ChoiceArmy(0), Choice(1) is true),
            MapEditorBlockKind.OwnItem => new CustomMapOwnItemCondition(Field<MapEditorBlockItem>().Item),
            MapEditorBlockKind.LeaderItem => new CustomMapLeaderItemCondition(ChoiceArmy(0), Field<MapEditorBlockItem>().Item),
            MapEditorBlockKind.Relation => new CustomMapRelationCondition(ChoiceRace(0), ChoiceRace(1), Choice(2) is CustomMapRelation level ? level : CustomMapRelation.War),
            MapEditorBlockKind.Alliance => new CustomMapAllianceCondition(ChoiceRace(0), ChoiceRace(1)),
            MapEditorBlockKind.Land => new CustomMapLandCondition(Number(0)),
            MapEditorBlockKind.VisitSite => new CustomMapVisitSiteCondition(ChoiceInt(0)),
            MapEditorBlockKind.ArmyToZone => new CustomMapArmyToZoneCondition(ChoiceArmy(0), ChoiceInt(1)),
            MapEditorBlockKind.ArmyToCity => new CustomMapArmyToCityCondition(ChoiceArmy(0), ChoiceCity(1)),
            MapEditorBlockKind.ItemToZone => new CustomMapItemToZoneCondition(Field<MapEditorBlockItem>().Item, ChoiceInt(0)),
            MapEditorBlockKind.Variable => VariableCondition(),
            _ => throw new InvalidOperationException($"{Kind} is an effect.")
        };
    }

    public CustomMapEffect ToEffect()
    {
        return Kind switch
        {
            MapEditorBlockKind.Message => new CustomMapMessageEffect(
                Field<MapEditorBlockTextField>().Text,
                (CustomMapMessageAudience)(Choice(0) ?? CustomMapMessageAudience.Triggerer),
                Field<MapEditorBlockPortrait>().Portrait,
                Choice(1) is not false),
            MapEditorBlockKind.Objective => new CustomMapObjectiveEffect(Field<MapEditorBlockTextField>().Text),
            // A missing race (a race removed from the map) saves as Neutral, which never is a player: the block stays red.
            MapEditorBlockKind.Win => new CustomMapWinEffect(Choice(0) is WorldRace race ? race : WorldRace.Neutral),
            MapEditorBlockKind.Lose => new CustomMapLoseEffect(),
            MapEditorBlockKind.SwitchEvent => new CustomMapSwitchEventEffect(ChoiceInt(0), Choice(1) is not false),
            MapEditorBlockKind.RevealZone => new CustomMapRevealZoneEffect(ChoiceInt(0), Choice(1) is WorldRace race ? [race] : revealRaces.Length > 1 ? revealRaces : []),
            MapEditorBlockKind.GiveItem => new CustomMapGiveItemEffect(Field<MapEditorBlockItem>().Item, Choice(0) is true),
            MapEditorBlockKind.CreateArmy => new CustomMapCreateArmyEffect(ChoiceInt(0), ChoiceInt(1)),
            MapEditorBlockKind.RemoveArmy => new CustomMapRemoveArmyEffect(ChoiceArmy(0)),
            MapEditorBlockKind.MoveArmy => new CustomMapMoveArmyEffect(ChoiceArmy(0), ChoiceInt(1)),
            MapEditorBlockKind.MoveBeside => new CustomMapMoveBesideEffect(ChoiceArmy(0)),
            MapEditorBlockKind.ChangeOwner => new CustomMapChangeOwnerEffect(ChoiceArmy(0), Choice(1) as WorldRace?),
            MapEditorBlockKind.OrderSimple => new CustomMapChangeOrderEffect(ChoiceArmy(0), new CustomMapArmyOrder(Choice(1) is CustomMapOrderKind kind ? kind : CustomMapOrderKind.Normal)),
            MapEditorBlockKind.OrderGoTo => new CustomMapChangeOrderEffect(ChoiceArmy(0), MapEditorOrders.Make(CustomMapOrderKind.MoveToZone, Choice(1))),
            MapEditorBlockKind.OrderDefendZone => new CustomMapChangeOrderEffect(ChoiceArmy(0), MapEditorOrders.Make(CustomMapOrderKind.DefendZone, Choice(1))),
            MapEditorBlockKind.OrderDefendCity => new CustomMapChangeOrderEffect(ChoiceArmy(0), MapEditorOrders.Make(CustomMapOrderKind.DefendCity, Choice(1))),
            MapEditorBlockKind.OrderSecureCity => new CustomMapChangeOrderEffect(ChoiceArmy(0), MapEditorOrders.Make(CustomMapOrderKind.SecureCity, Choice(1))),
            MapEditorBlockKind.OrderAttack => new CustomMapChangeOrderEffect(ChoiceArmy(0), MapEditorOrders.Make(CustomMapOrderKind.AttackArmy, Choice(1))),
            MapEditorBlockKind.Battle => new CustomMapBattleEffect(ChoiceArmy(0)),
            MapEditorBlockKind.CastSpell => new CustomMapCastSpellEffect(Field<MapEditorBlockSpell>().Spell, Choice(0) as WorldRace?),
            MapEditorBlockKind.CastSpellZone => new CustomMapCastSpellZoneEffect(Field<MapEditorBlockSpell>().Spell, ChoiceInt(0), Choice(1) as WorldRace?),
            MapEditorBlockKind.GiveSpell => new CustomMapGiveSpellEffect(Field<MapEditorBlockSpell>().Spell),
            MapEditorBlockKind.Ally => new CustomMapAllyEffect(ChoiceRace(0), ChoiceRace(1), Choice(2) is true),
            MapEditorBlockKind.SetRelation => new CustomMapRelationEffect(ChoiceRace(0), ChoiceRace(1), Choice(2) is CustomMapRelation level ? level : CustomMapRelation.War),
            MapEditorBlockKind.RemoveMountains => new CustomMapRemoveMountainsEffect(ChoiceInt(0)),
            MapEditorBlockKind.RemoveLandmark => new CustomMapRemoveLandmarkEffect(ChoiceInt(0)),
            MapEditorBlockKind.ChangeLandmark => new CustomMapChangeLandmarkEffect(ChoiceInt(0), Choice(1) is WorldLandmarkDefinitionId type ? type : new WorldLandmarkDefinitionId(string.Empty)),
            MapEditorBlockKind.ChangeTerrain => new CustomMapChangeTerrainEffect(ChoiceInt(0), Choice(1) is WorldMapTerrain terrain ? terrain : WorldMapTerrain.Neutral, Number(0)),
            MapEditorBlockKind.SetVariable => new CustomMapVariableEffect(ChoiceInt(0), Choice(1) is ScenarioVariableOperation operation ? operation : ScenarioVariableOperation.Add, Number(0)),
            MapEditorBlockKind.DestroyItem => new CustomMapDestroyItemEffect(Field<MapEditorBlockItem>().Item, Choice(0) is not false),
            _ => throw new InvalidOperationException($"{Kind} is a condition.")
        };
    }

    private static MapEditorEventBlockViewModel Make(MapEditorBlockKind kind, string? lead, params MapEditorBlockPart?[] parts)
    {
        MapEditorEventBlockViewModel block = new(kind);

        if (lead is not null)
        {
            block.Parts.Add(new MapEditorBlockText(lead));
        }

        foreach (MapEditorBlockPart? part in parts)
        {
            if (part is not null)
            {
                block.Parts.Add(part);
            }
        }

        return block;
    }

    private static T First<T>(IReadOnlyList<MapEditorOption<object?>> options)
        where T : struct
    {
        return options.FirstOrDefault()?.Value is T value ? value : default;
    }

    private static CustomMapCity FirstCity(MapEditorEventContext context)
    {
        return context.Cities.Skip(context.Map.Players.Count).FirstOrDefault()?.Value as CustomMapCity
               ?? context.Cities.FirstOrDefault()?.Value as CustomMapCity
               ?? new CustomMapCity.Village(0);
    }

    private T Field<T>()
        where T : MapEditorBlockPart
    {
        return Parts.OfType<T>().First();
    }

    private object? Choice(int index)
    {
        return Parts.OfType<MapEditorBlockChoice>().ElementAt(index).Value;
    }

    // A missing target saves as ID 0, which no object has: the engine reports it and the block stays red.
    private int ChoiceInt(int index)
    {
        return Choice(index) is int value ? value : 0;
    }

    private WorldRace ChoiceRace(int index)
    {
        return Choice(index) is WorldRace race ? race : WorldRace.Neutral;
    }

    // A missing army (deleted, or a removed template) saves as army 0, which the engine reports: the block stays red.
    private CustomMapArmyRef ChoiceArmy(int index)
    {
        return Choice(index) as CustomMapArmyRef ?? new CustomMapArmyRef.Army(0);
    }

    private CustomMapCity ChoiceCity(int index)
    {
        return Choice(index) as CustomMapCity ?? new CustomMapCity.Village(0);
    }

    private int Number(int index)
    {
        return Parts.OfType<MapEditorBlockNumber>().ElementAt(index).Value;
    }
}
