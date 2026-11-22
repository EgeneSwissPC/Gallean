using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (AI designer, docs/ai-designer.md, phase 3): the blocks of the designer screen.

internal enum AiDesignerCategory
{
    Condition,
    Economy,
    Army,
    Rule,
    Tuning,
    Behaviour,

    // Gallean Upgrade (battle AI designer): target preferences of a battle stage.
    Target
}

// Rule and tuning blocks (one of each per stage).
internal enum AiDesignerRule
{
    Heal,
    Reinforce,
    AttackPower,
    LeaderSavings,
    WaitForFreeCapital,
    KeepArmiesHome,
    GatherArmies,
    RushEnemyCapital,
    GoldReserve,

    // Battle AI designer: tactics of a battle stage (one of each per stage).
    HealersFirst,
    CastersFirst,
    LeaderFirst,
    HardestHitterFirst,
    WoundedFirst,
    FocusFire,
    ProtectLeader,
    LeaderDefends,
    NeverFlee,
    FleeBelow,
    NeverDefend
}

internal enum AiDesignerCondition
{
    FromTurn,
    GoldAbove,
    CitiesBelow,
    EnemyNearCapital,
    LostCity,

    // Battle AI designer: conditions of a battle stage, judged before each action of one of its units.
    FromRound,
    PowerShareBelow,
    PowerShareAbove,
    LeaderHitpointsBelow,
    CargoAbove,
    CargoWithGearAbove,
    CarriesQuestItem
}

// A palette entry: what the block is (one of Condition / Action / Rule is set), its label and its help text.
internal sealed record AiDesignerPaletteItem(
    AiDesignerCategory Category,
    string Label,
    string Description,
    AiDesignerCondition? Condition = null,
    WorldAiActionKind? Action = null,
    AiDesignerRule? Rule = null)
{
    public IBrush Brush => AiDesignerBlocks.GetBrush(Category);
}

internal sealed record AiDesignerPaletteGroup(string Title, IReadOnlyList<AiDesignerPaletteItem> Items);

// A block on a stage card: a sentence of text and number parts (reusing the event editor's parts).
internal sealed partial class AiDesignerBlockViewModel : ObservableObject
{
    public AiDesignerBlockViewModel(AiDesignerPaletteItem item, IReadOnlyList<MapEditorBlockPart> parts)
    {
        Item = item;
        Parts = parts;
    }

    public AiDesignerPaletteItem Item { get; }
    public IReadOnlyList<MapEditorBlockPart> Parts { get; }
    public IBrush Brush => Item.Brush;
    public string Label => string.Join(" ", Parts.OfType<MapEditorBlockText>().Select(part => part.Text));

    public int Number(int index) => Parts.OfType<MapEditorBlockNumber>().ElementAt(index).Value;
}

// One priority row of the economy or army slot: its blocks share a priority.
internal sealed partial class AiDesignerTierViewModel(AiDesignerSlotViewModel slot) : ObservableObject
{
    public AiDesignerSlotViewModel Slot { get; } = slot;
    public ObservableCollection<AiDesignerBlockViewModel> Blocks { get; } = [];

    [ObservableProperty]
    public partial int Number { get; set; }
}

// The economy or the army slot of a stage: priority rows, highest first.
internal sealed partial class AiDesignerSlotViewModel(AiDesignerStageViewModel stage, AiDesignerCategory category) : ObservableObject
{
    public AiDesignerStageViewModel Stage { get; } = stage;
    public AiDesignerCategory Category { get; } = category;
    public ObservableCollection<AiDesignerTierViewModel> Tiers { get; } = [];

    [ObservableProperty]
    public partial string UnusedText { get; set; } = "";

    public bool HasNoTiers => Tiers.Count == 0;

    public void Renumber()
    {
        // Rows emptied by a move or a removal disappear.
        foreach (AiDesignerTierViewModel empty in Tiers.Where(tier => tier.Blocks.Count == 0).ToList())
        {
            Tiers.Remove(empty);
        }

        for (int i = 0; i < Tiers.Count; i++)
        {
            Tiers[i].Number = i + 1;
        }

        HashSet<WorldAiActionKind> used = [.. Tiers.SelectMany(tier => tier.Blocks).Select(block => block.Item.Action!.Value)];
        string[] unused = [.. AiDesignerBlocks.Actions(Category).Where(item => !used.Contains(item.Action!.Value)).Select(item => item.Label)];
        UnusedText = unused.Length == 0 ? "" : Tr("Never done in this stage: {0}", string.Join(", ", unused));
        OnPropertyChanged(nameof(HasNoTiers));
    }
}

// An "and" group of conditions; the groups of a stage are joined by "or".
internal sealed partial class AiDesignerGroupViewModel(AiDesignerStageViewModel stage) : ObservableObject
{
    public AiDesignerStageViewModel Stage { get; } = stage;
    public ObservableCollection<AiDesignerBlockViewModel> Blocks { get; } = [];

    [ObservableProperty]
    public partial bool IsFirst { get; set; }

    public bool HasNoBlocks => Blocks.Count == 0;

    public void Refresh()
    {
        OnPropertyChanged(nameof(HasNoBlocks));
    }
}

internal static class AiDesignerBlocks
{
    public static IBrush GetBrush(AiDesignerCategory category)
    {
        return category switch
        {
            AiDesignerCategory.Condition => new SolidColorBrush(Color.FromRgb(0x2F, 0x55, 0x8C)),
            AiDesignerCategory.Economy => new SolidColorBrush(Color.FromRgb(0x8A, 0x67, 0x1E)),
            AiDesignerCategory.Army => new SolidColorBrush(Color.FromRgb(0x8C, 0x2F, 0x2F)),
            AiDesignerCategory.Rule => new SolidColorBrush(Color.FromRgb(0x2F, 0x6E, 0x3E)),
            AiDesignerCategory.Behaviour => new SolidColorBrush(Color.FromRgb(0xA0, 0x55, 0x1E)),
            AiDesignerCategory.Target => new SolidColorBrush(Color.FromRgb(0x8C, 0x2F, 0x2F)),
            _ => new SolidColorBrush(Color.FromRgb(0x5E, 0x3A, 0x84))
        };
    }

    public static IReadOnlyList<AiDesignerPaletteItem> Conditions =>
    [
        new(AiDesignerCategory.Condition, Tr("From turn N"), Tr("True from that turn on."), Condition: AiDesignerCondition.FromTurn),
        new(AiDesignerCategory.Condition, Tr("Gold above N"), Tr("True while the treasury holds more gold than that."), Condition: AiDesignerCondition.GoldAbove),
        new(AiDesignerCategory.Condition, Tr("Fewer cities than N"), Tr("True while the AI owns fewer cities than that, its capital included."), Condition: AiDesignerCondition.CitiesBelow),
        new(AiDesignerCategory.Condition, Tr("Enemy near my capital"), Tr("True while an enemy army the AI has seen stands within that many tiles of its capital."), Condition: AiDesignerCondition.EnemyNearCapital),
        new(AiDesignerCategory.Condition, Tr("I lost a city"), Tr("True for the turn after the AI lost a city or its capital."), Condition: AiDesignerCondition.LostCity)
    ];

    public static IReadOnlyList<AiDesignerPaletteItem> EconomyActions =>
    [
        new(AiDesignerCategory.Economy, Tr("Cast spells"), Tr("Casts the most useful spell it can afford."), Action: WorldAiActionKind.CastSpell),
        new(AiDesignerCategory.Economy, Tr("Research spells"), Tr("Researches a new spell (once per turn)."), Action: WorldAiActionKind.ResearchSpell),
        new(AiDesignerCategory.Economy, Tr("Hire leaders"), Tr("Hires a new leader in the capital, and saves up for one."), Action: WorldAiActionKind.HireLeader),
        new(AiDesignerCategory.Economy, Tr("Construct buildings"), Tr("Constructs a capital building (once per turn)."), Action: WorldAiActionKind.ConstructBuilding),
        new(AiDesignerCategory.Economy, Tr("Buy units"), Tr("Buys a unit for a garrison or an army (once per turn)."), Action: WorldAiActionKind.PurchaseUnit)
    ];

    public static IReadOnlyList<AiDesignerPaletteItem> ArmyActions =>
    [
        new(AiDesignerCategory.Army, Tr("Use items"), Tr("Uses potions, orbs and other items."), Action: WorldAiActionKind.UseItem),
        new(AiDesignerCategory.Army, Tr("Resurrect"), Tr("Brings dead units back in a city."), Action: WorldAiActionKind.Resurrect),
        new(AiDesignerCategory.Army, Tr("Heal"), Tr("Heals wounded units in a city."), Action: WorldAiActionKind.Heal),
        new(AiDesignerCategory.Army, Tr("Fill up armies"), Tr("Moves units between an army and the garrison it stands in."), Action: WorldAiActionKind.AutoFit),
        new(AiDesignerCategory.Army, Tr("Attack"), Tr("Attacks armies, cities and ruins it can beat with the wanted advantage."), Action: WorldAiActionKind.Attack),
        new(AiDesignerCategory.Army, Tr("Thief missions"), Tr("Sends thieves to spy, steal and poison."), Action: WorldAiActionKind.Thief),
        new(AiDesignerCategory.Army, Tr("Plant rods"), Tr("Rod leaders plant rods to claim land."), Action: WorldAiActionKind.Rod),
        new(AiDesignerCategory.Army, Tr("Enter cities"), Tr("Goes into its own cities (to heal, fill up or defend)."), Action: WorldAiActionKind.EnterCity),
        new(AiDesignerCategory.Army, Tr("Pick up treasure"), Tr("Picks up bags lying on the map."), Action: WorldAiActionKind.TakeBag),
        new(AiDesignerCategory.Army, Tr("Move and explore"), Tr("Walks towards targets, or explores when it knows none."), Action: WorldAiActionKind.Move)
    ];

    public static IReadOnlyList<AiDesignerPaletteItem> Rules =>
    [
        new(AiDesignerCategory.Rule, Tr("Heal below N %"), Tr("An army goes healing below that share of its hit points and comes back at the second value. Classic: 80 % and 90 %."), Rule: AiDesignerRule.Heal),
        new(AiDesignerCategory.Rule, Tr("Fetch units below N %"), Tr("An army goes back for more units while its leadership is filled below that share. Classic: 80 %."), Rule: AiDesignerRule.Reinforce),
        new(AiDesignerCategory.Tuning, Tr("Attack advantage"), Tr("How much of the usual power advantage it wants before it attacks: under 100 % attacks more, over 100 % attacks less. Classic: 100 %."), Rule: AiDesignerRule.AttackPower),
        new(AiDesignerCategory.Tuning, Tr("Savings for a leader"), Tr("How much gold it may still be missing while it saves up for a new leader (it buys nothing else meanwhile). Classic: 400."), Rule: AiDesignerRule.LeaderSavings),
        new(AiDesignerCategory.Tuning, Tr("Wait for a free capital"), Tr("Doesn't save up for a leader while an army stands on the capital entrance, so that army leaves first."), Rule: AiDesignerRule.WaitForFreeCapital)
    ];

    public static IReadOnlyList<AiDesignerPaletteItem> Behaviours =>
    [
        new(AiDesignerCategory.Behaviour, Tr("Keep armies home"), Tr("The fighting armies nearest the capital (as many as you set) stay within 4 tiles of it and walk back when they are further away. With only that many armies, all of them stay. Rod planters and thieves don't count."), Rule: AiDesignerRule.KeepArmiesHome),
        new(AiDesignerCategory.Behaviour, Tr("Gather before attacking"), Tr("An army only attacks when that many fighting armies, itself included, stand within 4 tiles of each other; until then it walks to the nearest other army. With fewer armies than that, nobody attacks."), Rule: AiDesignerRule.GatherArmies),
        new(AiDesignerCategory.Behaviour, Tr("Rush the enemy capital"), Tr("Armies with nothing better to do march on the nearest enemy capital the AI has seen. Attacking it still needs the wanted advantage."), Rule: AiDesignerRule.RushEnemyCapital),
        new(AiDesignerCategory.Behaviour, Tr("Keep a gold reserve"), Tr("Spells, research, leaders, buildings and units never spend the treasury below that much gold."), Rule: AiDesignerRule.GoldReserve)
    ];

    // ---------------------------------------------------------------- battle AI designer

    public static IReadOnlyList<AiDesignerPaletteItem> BattleConditions =>
    [
        new(AiDesignerCategory.Condition, Tr("From round N"), Tr("True from that battle round on."), Condition: AiDesignerCondition.FromRound),
        new(AiDesignerCategory.Condition, Tr("Losing: power below N %"), Tr("True while our side holds less than that share of the power on the field (hit points, damage, armour of every unit still standing). Classic thinks about fleeing below 25 %."), Condition: AiDesignerCondition.PowerShareBelow),
        new(AiDesignerCategory.Condition, Tr("Winning: power above N %"), Tr("True while our side holds more than that share of the power on the field."), Condition: AiDesignerCondition.PowerShareAbove),
        new(AiDesignerCategory.Condition, Tr("Leader hurt below N %"), Tr("True while our leader has less than that share of its hit points, or has fallen. Never true for an army without a leader."), Condition: AiDesignerCondition.LeaderHitpointsBelow),
        new(AiDesignerCategory.Condition, Tr("Carrying valuables over N"), Tr("True while the items in our army's bag are worth more than that much gold. Equipped items don't count: they make the leader stronger instead, which the power share already sees."), Condition: AiDesignerCondition.CargoAbove),
        new(AiDesignerCategory.Condition, Tr("Bag and gear worth over N"), Tr("Like \"Carrying valuables\", but the leader's equipped items count too."), Condition: AiDesignerCondition.CargoWithGearAbove),
        new(AiDesignerCategory.Condition, Tr("Carrying a quest item"), Tr("True while our army carries or wears a special quest item."), Condition: AiDesignerCondition.CarriesQuestItem)
    ];

    public static IReadOnlyList<AiDesignerPaletteItem> BattleTargets =>
    [
        new(AiDesignerCategory.Target, Tr("Healers first"), Tr("Enemy units that heal, cure or revive get a bonus on Classic's score for a target. 100 % doubles it."), Rule: AiDesignerRule.HealersFirst),
        new(AiDesignerCategory.Target, Tr("Mages first"), Tr("Enemy units whose attack hits every unit (mages) get a bonus on Classic's score."), Rule: AiDesignerRule.CastersFirst),
        new(AiDesignerCategory.Target, Tr("Leader first"), Tr("The enemy leader gets a bonus on Classic's score. In the sandbox this wins most mirror battles."), Rule: AiDesignerRule.LeaderFirst),
        new(AiDesignerCategory.Target, Tr("Hardest hitter first"), Tr("The enemy unit with the strongest attack gets a bonus on Classic's score."), Rule: AiDesignerRule.HardestHitterFirst),
        new(AiDesignerCategory.Target, Tr("Finish the weakest"), Tr("The enemy unit with the fewest hit points left gets a bonus on Classic's score."), Rule: AiDesignerRule.WoundedFirst),
        new(AiDesignerCategory.Target, Tr("Focus fire"), Tr("The enemy unit that has lost the largest share of its hit points gets a bonus, so attacks keep going to the same unit until it falls."), Rule: AiDesignerRule.FocusFire)
    ];

    public static IReadOnlyList<AiDesignerPaletteItem> BattleTactics =>
    [
        new(AiDesignerCategory.Behaviour, Tr("Protect our leader"), Tr("Heals, cures and protections go to our leader first (they count five times on Classic's score)."), Rule: AiDesignerRule.ProtectLeader),
        new(AiDesignerCategory.Behaviour, Tr("Hurt leader defends"), Tr("Our leader defends instead of attacking while below that share of its hit points. In the sandbox this costs a strong leader most of its battles; it suits a weak caster leader."), Rule: AiDesignerRule.LeaderDefends),
        new(AiDesignerCategory.Behaviour, Tr("Flee below N %"), Tr("Our leader thinks about fleeing when our side holds less than that share of the power. Classic: 25 %."), Rule: AiDesignerRule.FleeBelow),
        new(AiDesignerCategory.Behaviour, Tr("Never flee"), Tr("Our leader never flees, however the battle goes."), Rule: AiDesignerRule.NeverFlee),
        new(AiDesignerCategory.Behaviour, Tr("Never defend"), Tr("No unit defends: every unit attacks when it can."), Rule: AiDesignerRule.NeverDefend)
    ];

    public static IReadOnlyList<AiDesignerPaletteGroup> CreateBattlePalette()
    {
        return
        [
            new(Tr("When"), BattleConditions),
            new(Tr("Target preferences"), BattleTargets),
            new(Tr("Tactics"), BattleTactics)
        ];
    }

    public static IReadOnlyList<AiDesignerPaletteItem> Actions(AiDesignerCategory category)
    {
        return category == AiDesignerCategory.Economy ? EconomyActions : ArmyActions;
    }

    public static AiDesignerPaletteItem ItemFor(WorldAiActionKind kind)
    {
        return EconomyActions.Concat(ArmyActions).First(item => item.Action == kind);
    }

    public static AiDesignerPaletteItem ItemFor(AiDesignerCondition condition)
    {
        return Conditions.Concat(BattleConditions).First(item => item.Condition == condition);
    }

    public static AiDesignerPaletteItem ItemFor(AiDesignerRule rule)
    {
        return Rules.Concat(Behaviours).Concat(BattleTargets).Concat(BattleTactics).First(item => item.Rule == rule);
    }

    public static IReadOnlyList<AiDesignerPaletteGroup> CreatePalette()
    {
        return
        [
            new(Tr("When"), Conditions),
            new(Tr("Economy, in priority order"), EconomyActions),
            new(Tr("Armies, in priority order"), ArmyActions),
            new(Tr("Rules and tuning"), Rules),
            new(Tr("New behaviour"), Behaviours)
        ];
    }

    // A block with its sentence; numbers start at the given values (null = the block's default).
    public static AiDesignerBlockViewModel CreateBlock(AiDesignerPaletteItem item, Action changed, int? first = null, int? second = null)
    {
        MapEditorBlockNumber Number(int value, int minimum, int maximum) => new(value, minimum, maximum, changed);

        IReadOnlyList<MapEditorBlockPart> parts = item switch
        {
            { Condition: AiDesignerCondition.FromTurn } => [new MapEditorBlockText(Tr("From turn")), Number(first ?? 20, 1, 999)],
            { Condition: AiDesignerCondition.GoldAbove } => [new MapEditorBlockText(Tr("Gold above")), Number(first ?? 1000, 0, 9999)],
            { Condition: AiDesignerCondition.CitiesBelow } => [new MapEditorBlockText(Tr("Owns fewer cities than")), Number(first ?? 3, 1, 99)],
            { Condition: AiDesignerCondition.EnemyNearCapital } => [new MapEditorBlockText(Tr("An enemy army within")), Number(first ?? 6, 1, 50), new MapEditorBlockText(Tr("tiles of my capital"))],
            { Condition: AiDesignerCondition.LostCity } => [new MapEditorBlockText(Tr("I lost a city since my last turn"))],
            { Rule: AiDesignerRule.Heal } => [new MapEditorBlockText(Tr("Heal below")), Number(first ?? 80, 0, 100), new MapEditorBlockText(Tr("% hit points, back at")), Number(second ?? 90, 0, 100), new MapEditorBlockText("%")],
            { Rule: AiDesignerRule.Reinforce } => [new MapEditorBlockText(Tr("Fetch more units below")), Number(first ?? 80, 0, 100), new MapEditorBlockText(Tr("% of leadership"))],
            { Rule: AiDesignerRule.AttackPower } => [new MapEditorBlockText(Tr("Attack with")), Number(first ?? 100, 10, 500), new MapEditorBlockText(Tr("% of the usual advantage"))],
            { Rule: AiDesignerRule.LeaderSavings } => [new MapEditorBlockText(Tr("Save for a leader while short by up to")), Number(first ?? 400, 0, 9999), new MapEditorBlockText(Tr("gold"))],
            { Rule: AiDesignerRule.WaitForFreeCapital } => [new MapEditorBlockText(Tr("Don't save for a leader while the capital entrance is taken"))],
            { Rule: AiDesignerRule.KeepArmiesHome } => [new MapEditorBlockText(Tr("Keep")), Number(first ?? 1, 1, 20), new MapEditorBlockText(Tr("armies near the capital"))],
            { Rule: AiDesignerRule.GatherArmies } => [new MapEditorBlockText(Tr("Attack only when")), Number(first ?? 2, 2, 20), new MapEditorBlockText(Tr("armies stand together"))],
            { Rule: AiDesignerRule.RushEnemyCapital } => [new MapEditorBlockText(Tr("March on the nearest enemy capital"))],
            { Rule: AiDesignerRule.GoldReserve } => [new MapEditorBlockText(Tr("Never spend below")), Number(first ?? 300, 0, 9999), new MapEditorBlockText(Tr("gold"))],
            { Condition: AiDesignerCondition.FromRound } => [new MapEditorBlockText(Tr("From round")), Number(first ?? 3, 1, 99)],
            { Condition: AiDesignerCondition.PowerShareBelow } => [new MapEditorBlockText(Tr("We hold less than")), Number(first ?? 40, 0, 100), new MapEditorBlockText(Tr("% of the power"))],
            { Condition: AiDesignerCondition.PowerShareAbove } => [new MapEditorBlockText(Tr("We hold more than")), Number(first ?? 60, 0, 100), new MapEditorBlockText(Tr("% of the power"))],
            { Condition: AiDesignerCondition.LeaderHitpointsBelow } => [new MapEditorBlockText(Tr("Our leader below")), Number(first ?? 50, 1, 100), new MapEditorBlockText(Tr("% hit points"))],
            { Condition: AiDesignerCondition.CargoAbove } => [new MapEditorBlockText(Tr("We carry items worth over")), Number(first ?? 1000, 0, 99999), new MapEditorBlockText(Tr("gold"))],
            { Condition: AiDesignerCondition.CargoWithGearAbove } => [new MapEditorBlockText(Tr("We carry and wear items worth over")), Number(first ?? 2000, 0, 99999), new MapEditorBlockText(Tr("gold"))],
            { Condition: AiDesignerCondition.CarriesQuestItem } => [new MapEditorBlockText(Tr("We carry a quest item"))],
            { Category: AiDesignerCategory.Target } => [new MapEditorBlockText(item.Label), new MapEditorBlockText("+"), Number(first ?? 100, 10, 1000), new MapEditorBlockText("%")],
            { Rule: AiDesignerRule.ProtectLeader } => [new MapEditorBlockText(Tr("Heal and protect our leader first"))],
            { Rule: AiDesignerRule.LeaderDefends } => [new MapEditorBlockText(Tr("Our leader defends below")), Number(first ?? 30, 1, 100), new MapEditorBlockText(Tr("% hit points"))],
            { Rule: AiDesignerRule.FleeBelow } => [new MapEditorBlockText(Tr("Flee when we hold less than")), Number(first ?? 25, 1, 100), new MapEditorBlockText(Tr("% of the power"))],
            { Rule: AiDesignerRule.NeverFlee } => [new MapEditorBlockText(Tr("Never flee"))],
            { Rule: AiDesignerRule.NeverDefend } => [new MapEditorBlockText(Tr("Never defend: every unit attacks"))],
            _ => [new MapEditorBlockText(item.Label)]
        };

        return new AiDesignerBlockViewModel(item, parts);
    }
}
