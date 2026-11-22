using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (AI designer, docs/ai-designer.md, phase 3): edits one designed map AI. The view
// models mirror the DesignedWorldAi; every change snapshots the design for undo, and Save writes the file.
// The battle AI designer (docs/battle-ai-designer.md phase 9) uses the same screen for a DesignedBattleAi:
// the When blocks are battle conditions, the economy and army rows are hidden, and the rules area holds the
// stage's target preferences and tactics.
internal sealed partial class AiDesignerScreenViewModel : AbstractScreenViewModel
{
    private const int MaxHistory = 200;

    private readonly string id;
    private readonly Stack<object> undo = new();
    private readonly Stack<object> redo = new();
    private object current;
    private object saved;
    private bool loading;

    public AiDesignerScreenViewModel(GameViewModel game, DesignedWorldAi design)
    {
        Game = game;
        id = design.Id;
        name_ = design.Name;
        Palette = AiDesignerBlocks.CreatePalette();
        Load(design);
        current = Snapshot();
        saved = current;
        SelectedStage = Stages.FirstOrDefault();
        RefreshProblems();
        InitializeTest();
    }

    public AiDesignerScreenViewModel(GameViewModel game, DesignedBattleAi design)
    {
        Game = game;
        IsBattle = true;
        id = design.Id;
        name_ = design.Name;
        Palette = AiDesignerBlocks.CreateBattlePalette();
        Load(design);
        current = Snapshot();
        saved = current;
        SelectedStage = Stages.FirstOrDefault();
        RefreshProblems();
    }

    // A battle AI (DesignedBattleAi) rather than a map AI.
    public bool IsBattle { get; }
    public bool IsMap => !IsBattle;
    public string Title => IsBattle ? Tr("Battle AI designer") : Tr("AI designer");
    public string RulesTitle => IsBattle ? Tr("Targets and tactics") : Tr("Rules and tuning");
    public string RulesHint => IsBattle
        ? Tr("Drop target and tactic blocks here; without them the stage plays like Classic")
        : Tr("Drop rule blocks here; without them the stage plays by the Classic values");

    private object Snapshot()
    {
        return IsBattle ? ToBattleDesign() : ToDesign();
    }

    public GameViewModel Game { get; }
    public IReadOnlyList<AiDesignerPaletteGroup> Palette { get; }

    // Raised after + Stage, so the view can scroll the new card into sight.
    public event Action<AiDesignerStageViewModel>? StageAdded;
    public ObservableCollection<AiDesignerStageViewModel> Stages { get; } = [];

    private string name_;

    public string Name
    {
        get => name_;
        set
        {
            if (SetProperty(ref name_, value))
            {
                OnChanged();
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int NameLength => AiEditorMenuScreenViewModel.MaxNameLength;

    [ObservableProperty]
    public partial AiDesignerStageViewModel? SelectedStage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HelpTitle), nameof(HelpText))]
    public partial AiDesignerPaletteItem? HelpItem { get; set; }

    [ObservableProperty]
    public partial string ProblemText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasChanges { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public bool HasProblems => ProblemText.Length > 0;
    public string HelpTitle => HelpItem?.Label ?? Tr("How it works");
    public string HelpText => HelpItem?.Description ?? (IsBattle
        ? Tr("Each card is a stage. Before every action of one of its units, the AI plays the first stage from the top whose When holds; the last card plays when none above holds. In a stage, target blocks add a bonus to Classic's choice of whom to hit, and tactic blocks change fleeing, defending and healing. Drag blocks from the left, or click one to add it to the selected stage.")
        : Tr("Each card is a stage. From the top, the first stage whose When holds is the one the AI plays; the last card plays when none above holds. In a stage, the economy and army rows are priorities: the top row first, blocks in one row are equal. A block left out is never done in that stage. Drag blocks from the left, or click one to add it to the selected stage."));

    public override bool PaintsOwnBackground => true;

    partial void OnSelectedStageChanged(AiDesignerStageViewModel? value)
    {
        foreach (AiDesignerStageViewModel stage in Stages)
        {
            stage.IsSelected = ReferenceEquals(stage, value);
        }
    }

    // ---------------------------------------------------------------- editing

    public void OnChanged()
    {
        if (loading)
        {
            return;
        }

        object next = Snapshot();

        if (next.Equals(current))
        {
            return;
        }

        undo.Push(current);
        while (undo.Count > MaxHistory)
        {
            // Stack has no RemoveLast; history beyond the cap is rarely needed, so trim by rebuilding.
            object[] kept = [.. undo.Take(MaxHistory)];
            undo.Clear();
            foreach (object snapshot in kept.Reverse())
            {
                undo.Push(snapshot);
            }
        }

        redo.Clear();
        current = next;
        AfterChange();
    }

    private void AfterChange()
    {
        HasChanges = !current.Equals(saved);
        StatusText = "";
        RefreshProblems();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public void InsertBlock(AiDesignerPaletteItem item, object container, int index, AiDesignerBlockViewModel? moving = null)
    {
        AiDesignerStageViewModel? stage = StageOf(container);

        if (stage is null || !Accepts(container, item))
        {
            return;
        }

        int[] numbers = moving is null ? [] : [.. moving.Parts.OfType<MapEditorBlockNumber>().Select(part => part.Value)];

        if (moving is not null)
        {
            RemoveFromContainer(moving, ref container, ref index);
        }

        AiDesignerBlockViewModel block = AiDesignerBlocks.CreateBlock(item, OnChanged, numbers.Length > 0 ? numbers[0] : null, numbers.Length > 1 ? numbers[1] : null);

        switch (container)
        {
            case AiDesignerGroupViewModel group:
                group.Blocks.Insert(Math.Clamp(index, 0, group.Blocks.Count), block);
                break;

            case AiDesignerTierViewModel tier:
                AiDesignerTierViewModel? target = tier;
                RemoveAction(tier.Slot, item.Action!.Value, ref target, ref index);
                tier.Blocks.Insert(Math.Clamp(index, 0, tier.Blocks.Count), block);

                break;

            case AiDesignerSlotViewModel slot:
                // A new priority row at the end of the slot.
                AiDesignerTierViewModel? none = null;
                RemoveAction(slot, item.Action!.Value, ref none, ref index);
                AiDesignerTierViewModel newTier = new(slot);
                newTier.Blocks.Add(block);
                slot.Tiers.Add(newTier);
                break;

            case AiDesignerStageViewModel rules:
                if (rules.Rules.Any(existing => existing.Item.Rule == item.Rule))
                {
                    return;
                }

                rules.Rules.Insert(Math.Clamp(index, 0, rules.Rules.Count), block);
                break;
        }

        HelpItem = item;
        SelectedStage = stage;
        stage.RefreshState();
        OnChanged();
    }

    // Removes an action already in the slot (an action appears once per slot); keeps the target row valid.
    private static void RemoveAction(AiDesignerSlotViewModel slot, WorldAiActionKind action, ref AiDesignerTierViewModel? target, ref int index)
    {
        foreach (AiDesignerTierViewModel tier in slot.Tiers.ToList())
        {
            AiDesignerBlockViewModel? existing = tier.Blocks.FirstOrDefault(block => block.Item.Action == action);

            if (existing is null)
            {
                continue;
            }

            int position = tier.Blocks.IndexOf(existing);
            tier.Blocks.Remove(existing);

            if (ReferenceEquals(tier, target) && position < index)
            {
                index--;
            }

            if (tier.Blocks.Count == 0 && !ReferenceEquals(tier, target))
            {
                slot.Tiers.Remove(tier);
            }
        }
    }

    public void MoveBlock(AiDesignerBlockViewModel block, object container, int index)
    {
        InsertBlock(block.Item, container, index, block);
    }

    // A click on a palette block adds it to the selected stage.
    [RelayCommand]
    private void AddBlock(AiDesignerPaletteItem item)
    {
        AiDesignerStageViewModel stage = SelectedStage ?? Stages[0];

        switch (item.Category)
        {
            case AiDesignerCategory.Condition:
                if (stage.IsDefault)
                {
                    StatusText = Tr("The last stage has no conditions: select another stage, or add one with + Stage.");
                    HelpItem = item;
                    return;
                }

                if (stage.Groups.Count == 0)
                {
                    stage.Groups.Add(new AiDesignerGroupViewModel(stage));
                }

                InsertBlock(item, stage.Groups[^1], stage.Groups[^1].Blocks.Count);
                break;

            case AiDesignerCategory.Economy or AiDesignerCategory.Army:
                AiDesignerSlotViewModel slot = item.Category == AiDesignerCategory.Economy ? stage.Economy : stage.Army;

                if (slot.Tiers.SelectMany(tier => tier.Blocks).Any(block => block.Item.Action == item.Action))
                {
                    // Already there: drag it to change its priority.
                    StatusText = Tr("This block is already in the stage; drag it to another row to change its priority.");
                    HelpItem = item;
                    return;
                }

                InsertBlock(item, slot, 0);
                break;

            default:
                if (stage.Rules.Any(block => block.Item.Rule == item.Rule))
                {
                    StatusText = Tr("This block is already in the stage.");
                    HelpItem = item;
                    return;
                }

                InsertBlock(item, stage, stage.Rules.Count);
                break;
        }
    }

    [RelayCommand]
    private void RemoveBlock(AiDesignerBlockViewModel block)
    {
        object? container = FindContainer(block);

        if (container is null)
        {
            return;
        }

        int index = 0;
        RemoveFromContainer(block, ref container, ref index);
        StageOf(container)?.RefreshState();
        OnChanged();
    }

    private void RemoveFromContainer(AiDesignerBlockViewModel block, ref object container, ref int index)
    {
        object? source = FindContainer(block);

        switch (source)
        {
            case AiDesignerGroupViewModel group:
                if (ReferenceEquals(group, container) && group.Blocks.IndexOf(block) < index)
                {
                    index--;
                }

                group.Blocks.Remove(block);
                break;

            case AiDesignerTierViewModel tier:
                if (ReferenceEquals(tier, container) && tier.Blocks.IndexOf(block) < index)
                {
                    index--;
                }

                tier.Blocks.Remove(block);

                if (tier.Blocks.Count == 0 && !ReferenceEquals(tier, container))
                {
                    tier.Slot.Tiers.Remove(tier);
                }

                break;

            case AiDesignerStageViewModel stage:
                if (ReferenceEquals(stage, container) && stage.Rules.IndexOf(block) < index)
                {
                    index--;
                }

                stage.Rules.Remove(block);
                break;
        }

        if (source is not null && !ReferenceEquals(StageOf(source), StageOf(container)))
        {
            StageOf(source)?.RefreshState();
        }
    }

    // The group, priority row or stage (rules) a block is in.
    public object? FindContainer(AiDesignerBlockViewModel block)
    {
        foreach (AiDesignerStageViewModel stage in Stages)
        {
            if (stage.Rules.Contains(block))
            {
                return stage;
            }

            foreach (AiDesignerGroupViewModel group in stage.Groups)
            {
                if (group.Blocks.Contains(block))
                {
                    return group;
                }
            }

            foreach (AiDesignerTierViewModel tier in stage.Economy.Tiers.Concat(stage.Army.Tiers))
            {
                if (tier.Blocks.Contains(block))
                {
                    return tier;
                }
            }
        }

        return null;
    }

    public static bool Accepts(object container, AiDesignerPaletteItem item)
    {
        return container switch
        {
            AiDesignerGroupViewModel group => item.Category == AiDesignerCategory.Condition && !group.Stage.IsDefault,
            AiDesignerTierViewModel tier => item.Category == tier.Slot.Category,
            AiDesignerSlotViewModel slot => item.Category == slot.Category,
            AiDesignerStageViewModel => item.Category is AiDesignerCategory.Rule or AiDesignerCategory.Tuning or AiDesignerCategory.Behaviour or AiDesignerCategory.Target,
            _ => false
        };
    }

    private static AiDesignerStageViewModel? StageOf(object container)
    {
        return container switch
        {
            AiDesignerGroupViewModel group => group.Stage,
            AiDesignerTierViewModel tier => tier.Slot.Stage,
            AiDesignerSlotViewModel slot => slot.Stage,
            AiDesignerStageViewModel stage => stage,
            _ => null
        };
    }

    public void AddGroup(AiDesignerStageViewModel stage)
    {
        if (stage.IsDefault)
        {
            return;
        }

        stage.Groups.Add(new AiDesignerGroupViewModel(stage));
        stage.RefreshState();
        SelectedStage = stage;
        OnChanged();
    }

    public void RemoveGroup(AiDesignerStageViewModel stage, AiDesignerGroupViewModel group)
    {
        stage.Groups.Remove(group);
        stage.RefreshState();
        OnChanged();
    }

    public void MoveTier(AiDesignerTierViewModel tier, int delta)
    {
        ObservableCollection<AiDesignerTierViewModel> tiers = tier.Slot.Tiers;
        int index = tiers.IndexOf(tier);
        int target = index + delta;

        if (index < 0 || target < 0 || target >= tiers.Count)
        {
            return;
        }

        tiers.Move(index, target);
        tier.Slot.Stage.RefreshState();
        OnChanged();
    }

    [RelayCommand]
    private void AddStage()
    {
        int insertAt = Math.Max(0, Stages.Count - 1);
        AiDesignerStageViewModel viewModel = IsBattle
            ? CreateBattleStage(new DesignedBattleStage(Tr("Stage {0}", Stages.Count), [[new DesignedBattleCondition.FromRound(3)]], DesignedBattleTactics.Classic))
            : CreateStage(DesignedWorldStage.Classic with
            {
                Name = Tr("Stage {0}", Stages.Count),
                When = [[new DesignedStageCondition.FromTurn(20)]]
            });

        Stages.Insert(insertAt, viewModel);
        RefreshStages();
        SelectedStage = viewModel;
        OnChanged();
        StageAdded?.Invoke(viewModel);
    }

    public void MoveStage(AiDesignerStageViewModel stage, int delta)
    {
        int index = Stages.IndexOf(stage);
        int target = index + delta;

        if (stage.IsDefault || target < 0 || target >= Stages.Count - 1)
        {
            return;
        }

        Stages.Move(index, target);
        RefreshStages();
        OnChanged();
    }

    public void RemoveStage(AiDesignerStageViewModel stage)
    {
        if (stage.IsDefault)
        {
            return;
        }

        Stages.Remove(stage);
        RefreshStages();

        if (ReferenceEquals(SelectedStage, stage))
        {
            SelectedStage = Stages.FirstOrDefault();
        }

        OnChanged();
    }

    public void SelectStage(AiDesignerStageViewModel stage)
    {
        SelectedStage = stage;
    }

    private void RefreshStages()
    {
        for (int i = 0; i < Stages.Count; i++)
        {
            Stages[i].Number = i + 1;
            Stages[i].IsDefault = i == Stages.Count - 1;
            Stages[i].IsFirst = i == 0;
            Stages[i].IsLastBeforeDefault = i == Stages.Count - 2;
        }
    }

    // ---------------------------------------------------------------- undo, save, close

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        redo.Push(current);
        Restore(undo.Pop());
    }

    private bool CanUndo() => undo.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        undo.Push(current);
        Restore(redo.Pop());
    }

    private bool CanRedo() => redo.Count > 0;

    private void Restore(object snapshot)
    {
        int selected = SelectedStage is null ? 0 : Stages.IndexOf(SelectedStage);
        loading = true;

        if (snapshot is DesignedBattleAi battle)
        {
            name_ = battle.Name;
            Load(battle);
        }
        else
        {
            DesignedWorldAi world = (DesignedWorldAi)snapshot;
            name_ = world.Name;
            Load(world);
        }

        OnPropertyChanged(nameof(Name));
        object restored = snapshot;
        loading = false;
        current = restored;
        SelectedStage = Stages.ElementAtOrDefault(Math.Clamp(selected, 0, Stages.Count - 1));
        AfterChange();
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(Name);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (IsBattle)
        {
            DesignedBattleAi battle = ToBattleDesign();
            DesignedBattleAiLibrary.Store.Save(battle);
            DesignedBattleAiLibrary.Reload();
            saved = battle;
            current = battle;
            HasChanges = false;
            StatusText = Tr("Saved.");

            return;
        }

        DesignedWorldAi design = ToDesign();
        DesignedAiLibrary.Store.Save(design);
        DesignedAiLibrary.Reload();
        saved = design;
        current = design;
        HasChanges = false;
        StatusText = Tr("Saved.");
    }

    protected override bool TryClose()
    {
        testCancellation?.Cancel();

        if (HasChanges)
        {
            Game.OpenScreen(new ConfirmPromptViewModel(
                Tr("Unsaved changes"),
                TrSpans("Leave the designer without saving your changes?"),
                Tr("Leave"),
                ConfirmPromptType.Danger,
                () =>
                {
                    Leave();
                    return Task.CompletedTask;
                }));

            return true;
        }

        Leave();

        return true;
    }

    private void Leave()
    {
        if (IsBattle)
        {
            DesignedBattleAiLibrary.Reload();
        }

        Game.ShowScreen(new AiEditorMenuScreenViewModel(Game, selectId: id, battle: IsBattle));
    }

    [RelayCommand]
    private void Back()
    {
        TryClose();
    }

    // Battle AIs: the Test button opens the battle sandbox with this AI, as it is on screen (saved or not), on the
    // attacker side; the sandbox's Back returns here. The saved AIs are registered again on return.
    [RelayCommand]
    private Task TestInSandboxAsync()
    {
        DesignedBattleAi design = ToBattleDesign();
        AiBrainRegistry.SetDesignedBattleAis([.. DesignedBattleAiLibrary.Store.LoadAll().Where(other => other.Id != id), design]);

        return Game.OpenBattleSandboxAsync(this, id);
    }


    // ---------------------------------------------------------------- model <-> view models

    private void Load(DesignedWorldAi design)
    {
        bool wasLoading = loading;
        loading = true;
        Stages.Clear();

        ImmutableArray<DesignedWorldStage> stages = design.Stages.IsDefaultOrEmpty ? [DesignedWorldStage.Classic] : design.Stages;

        foreach (DesignedWorldStage stage in stages)
        {
            Stages.Add(CreateStage(stage));
        }

        // The last card is always the default stage (its When is ignored); a design whose last stage has
        // conditions gets an empty default stage after it, which plays like Classic.
        if (!stages[^1].When.IsDefaultOrEmpty)
        {
            Stages.Add(CreateStage(DesignedWorldStage.Classic with { Name = Tr("Default") }));
        }

        RefreshStages();
        loading = wasLoading;
    }

    private AiDesignerStageViewModel CreateStage(DesignedWorldStage stage)
    {
        bool wasLoading = loading;
        loading = true;
        AiDesignerStageViewModel viewModel = new(this, stage.Name);

        foreach (ImmutableArray<DesignedStageCondition> group in stage.When.IsDefault ? [] : stage.When)
        {
            AiDesignerGroupViewModel groupViewModel = new(viewModel);

            foreach (DesignedStageCondition condition in group)
            {
                (AiDesignerCondition kind, int? value) = condition switch
                {
                    DesignedStageCondition.FromTurn fromTurn => (AiDesignerCondition.FromTurn, (int?)fromTurn.Turn),
                    DesignedStageCondition.GoldAbove goldAbove => (AiDesignerCondition.GoldAbove, goldAbove.Gold),
                    DesignedStageCondition.CitiesBelow citiesBelow => (AiDesignerCondition.CitiesBelow, citiesBelow.Count),
                    DesignedStageCondition.EnemyNearCapital near => (AiDesignerCondition.EnemyNearCapital, near.Tiles),
                    _ => (AiDesignerCondition.LostCity, null)
                };

                groupViewModel.Blocks.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(kind), OnChanged, value));
            }

            viewModel.Groups.Add(groupViewModel);
        }

        LoadTiers(viewModel.Economy, stage.Economy);
        LoadTiers(viewModel.Army, stage.Army);

        DesignedWorldRules rules = stage.Rules;

        if (rules.HurtPercent is not null || rules.HealedPercent is not null)
        {
            viewModel.Rules.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(AiDesignerRule.Heal), OnChanged, rules.HurtPercent, rules.HealedPercent));
        }

        if (rules.ReinforcePercent is { } reinforce)
        {
            viewModel.Rules.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(AiDesignerRule.Reinforce), OnChanged, reinforce));
        }

        if (rules.AttackPowerPercent is { } attack)
        {
            viewModel.Rules.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(AiDesignerRule.AttackPower), OnChanged, attack));
        }

        if (rules.LeaderSavingsGold is { } savings)
        {
            viewModel.Rules.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(AiDesignerRule.LeaderSavings), OnChanged, savings));
        }

        if (rules.WaitForFreeCapital is true)
        {
            viewModel.Rules.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(AiDesignerRule.WaitForFreeCapital), OnChanged));
        }

        if (rules.KeepArmiesHome is { } home)
        {
            viewModel.Rules.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(AiDesignerRule.KeepArmiesHome), OnChanged, home));
        }

        if (rules.GatherArmies is { } gather)
        {
            viewModel.Rules.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(AiDesignerRule.GatherArmies), OnChanged, gather));
        }

        if (rules.RushEnemyCapital is true)
        {
            viewModel.Rules.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(AiDesignerRule.RushEnemyCapital), OnChanged));
        }

        if (rules.GoldReserve is { } reserve)
        {
            viewModel.Rules.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(AiDesignerRule.GoldReserve), OnChanged, reserve));
        }

        if (rules.SpendAbovePercent is { } spend)
        {
            viewModel.Rules.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(AiDesignerRule.SpendAbove), OnChanged, spend));
        }

        viewModel.RefreshState();
        loading = wasLoading;

        return viewModel;
    }

    private void Load(DesignedBattleAi design)
    {
        bool wasLoading = loading;
        loading = true;
        Stages.Clear();

        ImmutableArray<DesignedBattleStage> stages = design.Stages.IsDefaultOrEmpty ? [DesignedBattleStage.Classic] : design.Stages;

        foreach (DesignedBattleStage stage in stages)
        {
            Stages.Add(CreateBattleStage(stage));
        }

        // As for map AIs: the last card is the default stage.
        if (!stages[^1].When.IsDefaultOrEmpty)
        {
            Stages.Add(CreateBattleStage(DesignedBattleStage.Classic with { Name = Tr("Default") }));
        }

        RefreshStages();
        loading = wasLoading;
    }

    private AiDesignerStageViewModel CreateBattleStage(DesignedBattleStage stage)
    {
        bool wasLoading = loading;
        loading = true;
        AiDesignerStageViewModel viewModel = new(this, stage.Name);

        foreach (ImmutableArray<DesignedBattleCondition> group in stage.When.IsDefault ? [] : stage.When)
        {
            AiDesignerGroupViewModel groupViewModel = new(viewModel);

            foreach (DesignedBattleCondition condition in group)
            {
                (AiDesignerCondition kind, int? value) = condition switch
                {
                    DesignedBattleCondition.FromRound round => (AiDesignerCondition.FromRound, (int?)round.Round),
                    DesignedBattleCondition.PowerShareBelow below => (AiDesignerCondition.PowerShareBelow, below.Percent),
                    DesignedBattleCondition.PowerShareAbove above => (AiDesignerCondition.PowerShareAbove, above.Percent),
                    DesignedBattleCondition.LeaderHitpointsBelow leader => (AiDesignerCondition.LeaderHitpointsBelow, leader.Percent),
                    DesignedBattleCondition.CargoAbove { IncludeEquipped: true } cargo => (AiDesignerCondition.CargoWithGearAbove, cargo.Gold),
                    DesignedBattleCondition.CargoAbove cargo => (AiDesignerCondition.CargoAbove, cargo.Gold),
                    _ => (AiDesignerCondition.CarriesQuestItem, null)
                };

                groupViewModel.Blocks.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(kind), OnChanged, value));
            }

            viewModel.Groups.Add(groupViewModel);
        }

        DesignedBattleTactics tactics = stage.Tactics;

        void Add(AiDesignerRule rule, int? value = null)
        {
            viewModel.Rules.Add(AiDesignerBlocks.CreateBlock(AiDesignerBlocks.ItemFor(rule), OnChanged, value));
        }

        if (tactics.HealersFirst != 0) { Add(AiDesignerRule.HealersFirst, tactics.HealersFirst); }
        if (tactics.CastersFirst != 0) { Add(AiDesignerRule.CastersFirst, tactics.CastersFirst); }
        if (tactics.LeaderFirst != 0) { Add(AiDesignerRule.LeaderFirst, tactics.LeaderFirst); }
        if (tactics.HardestHitterFirst != 0) { Add(AiDesignerRule.HardestHitterFirst, tactics.HardestHitterFirst); }
        if (tactics.WoundedFirst != 0) { Add(AiDesignerRule.WoundedFirst, tactics.WoundedFirst); }
        if (tactics.FocusFire != 0) { Add(AiDesignerRule.FocusFire, tactics.FocusFire); }
        if (tactics.ProtectLeader) { Add(AiDesignerRule.ProtectLeader); }
        if (tactics.LeaderDefendsBelowPercent != 0) { Add(AiDesignerRule.LeaderDefends, tactics.LeaderDefendsBelowPercent); }
        if (tactics.FleeBelowPercent != DesignedBattleTactics.Classic.FleeBelowPercent) { Add(AiDesignerRule.FleeBelow, tactics.FleeBelowPercent); }
        if (tactics.NeverFlee) { Add(AiDesignerRule.NeverFlee); }
        if (!tactics.CanDefend) { Add(AiDesignerRule.NeverDefend); }

        viewModel.RefreshState();
        loading = wasLoading;

        return viewModel;
    }

    public DesignedBattleAi ToBattleDesign()
    {
        return new DesignedBattleAi(id, Name.Trim(), [.. Stages.Select(ToBattleStage)]);
    }

    private static DesignedBattleStage ToBattleStage(AiDesignerStageViewModel stage)
    {
        ImmutableArray<ImmutableArray<DesignedBattleCondition>> when = stage.IsDefault
            ? []
            : [.. stage.Groups.Where(group => group.Blocks.Count > 0).Select(group => group.Blocks.Select(ToBattleCondition).ToImmutableArray())];

        AiDesignerBlockViewModel? Rule(AiDesignerRule rule) => stage.Rules.FirstOrDefault(block => block.Item.Rule == rule);
        int Bonus(AiDesignerRule rule) => Rule(rule)?.Number(0) ?? 0;

        DesignedBattleTactics tactics = new(
            Bonus(AiDesignerRule.HealersFirst),
            Bonus(AiDesignerRule.CastersFirst),
            Bonus(AiDesignerRule.LeaderFirst),
            Bonus(AiDesignerRule.HardestHitterFirst),
            Bonus(AiDesignerRule.WoundedFirst),
            Bonus(AiDesignerRule.FocusFire),
            Rule(AiDesignerRule.ProtectLeader) is not null,
            Rule(AiDesignerRule.NeverFlee) is not null,
            Rule(AiDesignerRule.FleeBelow)?.Number(0) ?? DesignedBattleTactics.Classic.FleeBelowPercent,
            Rule(AiDesignerRule.NeverDefend) is null,
            Bonus(AiDesignerRule.LeaderDefends));

        return new DesignedBattleStage(stage.Name.Trim(), when, tactics);
    }

    private static DesignedBattleCondition ToBattleCondition(AiDesignerBlockViewModel block)
    {
        return block.Item.Condition switch
        {
            AiDesignerCondition.FromRound => new DesignedBattleCondition.FromRound(block.Number(0)),
            AiDesignerCondition.PowerShareBelow => new DesignedBattleCondition.PowerShareBelow(block.Number(0)),
            AiDesignerCondition.PowerShareAbove => new DesignedBattleCondition.PowerShareAbove(block.Number(0)),
            AiDesignerCondition.LeaderHitpointsBelow => new DesignedBattleCondition.LeaderHitpointsBelow(block.Number(0)),
            AiDesignerCondition.CargoAbove => new DesignedBattleCondition.CargoAbove(block.Number(0)),
            AiDesignerCondition.CargoWithGearAbove => new DesignedBattleCondition.CargoAbove(block.Number(0), IncludeEquipped: true),
            _ => new DesignedBattleCondition.CarriesQuestItem()
        };
    }

    private void LoadTiers(AiDesignerSlotViewModel slot, ImmutableArray<ImmutableArray<WorldAiActionKind>> tiers)
    {
        HashSet<WorldAiActionKind> seen = [];

        foreach (ImmutableArray<WorldAiActionKind> tier in tiers.IsDefault ? [] : tiers)
        {
            AiDesignerTierViewModel tierViewModel = new(slot);

            foreach (WorldAiActionKind kind in tier)
            {
                AiDesignerPaletteItem item = AiDesignerBlocks.ItemFor(kind);

                // Actions of the other slot or listed twice are dropped (the engine would ignore them too).
                if (item.Category == slot.Category && seen.Add(kind))
                {
                    tierViewModel.Blocks.Add(AiDesignerBlocks.CreateBlock(item, OnChanged));
                }
            }

            if (tierViewModel.Blocks.Count > 0)
            {
                slot.Tiers.Add(tierViewModel);
            }
        }
    }

    public DesignedWorldAi ToDesign()
    {
        return new DesignedWorldAi(id, Name.Trim(), [.. Stages.Select(ToStage)]);
    }

    private static DesignedWorldStage ToStage(AiDesignerStageViewModel stage)
    {
        ImmutableArray<ImmutableArray<DesignedStageCondition>> when = stage.IsDefault
            ? []
            : [.. stage.Groups.Where(group => group.Blocks.Count > 0).Select(group => group.Blocks.Select(ToCondition).ToImmutableArray())];

        AiDesignerBlockViewModel? Rule(AiDesignerRule rule) => stage.Rules.FirstOrDefault(block => block.Item.Rule == rule);

        AiDesignerBlockViewModel? heal = Rule(AiDesignerRule.Heal);
        AiDesignerBlockViewModel? reinforce = Rule(AiDesignerRule.Reinforce);
        AiDesignerBlockViewModel? attack = Rule(AiDesignerRule.AttackPower);
        AiDesignerBlockViewModel? savings = Rule(AiDesignerRule.LeaderSavings);

        DesignedWorldRules rules = new(
            heal?.Number(0),
            heal?.Number(1),
            reinforce?.Number(0),
            attack?.Number(0),
            savings?.Number(0),
            Rule(AiDesignerRule.WaitForFreeCapital) is null ? null : true,
            Rule(AiDesignerRule.KeepArmiesHome)?.Number(0),
            Rule(AiDesignerRule.GatherArmies)?.Number(0),
            Rule(AiDesignerRule.RushEnemyCapital) is null ? null : true,
            Rule(AiDesignerRule.GoldReserve)?.Number(0),
            Rule(AiDesignerRule.SpendAbove)?.Number(0));

        return new DesignedWorldStage(stage.Name.Trim(), when, ToTiers(stage.Economy), ToTiers(stage.Army), rules);
    }

    private static ImmutableArray<ImmutableArray<WorldAiActionKind>> ToTiers(AiDesignerSlotViewModel slot)
    {
        return [.. slot.Tiers.Where(tier => tier.Blocks.Count > 0).Select(tier => tier.Blocks.Select(block => block.Item.Action!.Value).ToImmutableArray())];
    }

    private static DesignedStageCondition ToCondition(AiDesignerBlockViewModel block)
    {
        return block.Item.Condition switch
        {
            AiDesignerCondition.FromTurn => new DesignedStageCondition.FromTurn(block.Number(0)),
            AiDesignerCondition.GoldAbove => new DesignedStageCondition.GoldAbove(block.Number(0)),
            AiDesignerCondition.CitiesBelow => new DesignedStageCondition.CitiesBelow(block.Number(0)),
            AiDesignerCondition.EnemyNearCapital => new DesignedStageCondition.EnemyNearCapital(block.Number(0)),
            _ => new DesignedStageCondition.LostCity()
        };
    }

    // ---------------------------------------------------------------- problems

    private void RefreshProblems()
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add(Tr("The AI needs a name."));
        }

        foreach (AiDesignerStageViewModel stage in Stages)
        {
            bool problem = false;
            string name = string.IsNullOrWhiteSpace(stage.Name) ? Tr("Stage {0}", stage.Number) : stage.Name.Trim();

            if (!stage.IsDefault && stage.Groups.All(group => group.Blocks.Count == 0))
            {
                problems.Add(Tr("{0}: no conditions, so this stage always plays and the stages below it never do.", name));
                problem = true;
            }

            if (IsMap && stage.Economy.Tiers.Count == 0 && stage.Army.Tiers.Count == 0)
            {
                problems.Add(Tr("{0}: the economy and army rows are empty, so the AI does nothing in this stage.", name));
                problem = true;
            }

            stage.HasProblem = problem;
        }

        ProblemText = string.Join("\n", problems);
        OnPropertyChanged(nameof(HasProblems));
    }
}
