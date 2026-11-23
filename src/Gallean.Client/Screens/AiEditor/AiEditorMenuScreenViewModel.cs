using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

internal sealed record AiEditorItem(string Id, string Name, string Details);

// Gallean Upgrade (AI editor, docs/ai-designer.md, phase 2): the AI editor mode's first screen. Lists the
// player's designed map AIs and creates new ones from a built-in AI or one of theirs. The battle AI designer
// (docs/battle-ai-designer.md phase 8) adds a Battle AIs tab: the same list for designed battle AIs.
internal sealed partial class AiEditorMenuScreenViewModel : AbstractScreenViewModel
{
    public const int MaxNameLength = 40;

    private readonly MusicQueue musicQueue = MusicQueue.Create([new AssetSoundKey.MenuMusic()], isLooping: true);

    public AiEditorMenuScreenViewModel(GameViewModel game, string? selectId = null, bool battle = false)
    {
        Game = game;
        IsBattle = battle;
        Name = Tr("My AI");
        Background = MainMenuBackgroundViewModel.Create();
        Refresh(selectId);
    }

    public GameViewModel Game { get; }
    public MainMenuBackgroundViewModel Background { get; }
    public ObservableCollection<AiEditorItem> Ais { get; } = [];
    public ObservableCollection<MapEditorOption<string>> Sources { get; } = [];
    public string AisDirectory => IsBattle ? DesignedBattleAiLibrary.Directory : DesignedAiLibrary.Directory;

    // The tab: map AIs or battle AIs.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMap), nameof(ListTitle), nameof(NewTitle), nameof(Hint), nameof(AisDirectory))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    public partial bool IsBattle { get; set; }

    public bool IsMap => !IsBattle;
    public string ListTitle => IsBattle ? Tr("Your battle AIs") : Tr("Your map AIs");
    public string NewTitle => IsBattle ? Tr("New battle AI") : Tr("New map AI");

    public string Hint => IsBattle
        ? Tr("A new battle AI plays exactly like the one it starts from until you change it. Battle AIs appear in Settings, in the AI players panel and in the battle sandbox.")
        : Tr("A new AI plays exactly like the one it starts from until you change it. Your AIs appear in Settings and in the AI players panel.");
    public int NameLength => MaxNameLength;

    public override bool PaintsOwnBackground => true;
    public override ScreenMusic Music => ScreenMusic.Tracks(musicQueue);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(DuplicateCommand), nameof(EditCommand))]
    public partial AiEditorItem? SelectedAi { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial MapEditorOption<string>? SelectedSource { get; set; }

    protected override bool TryClose()
    {
        Game.ShowMainMenuCommand.Execute(null);

        return true;
    }

    partial void OnIsBattleChanged(bool value)
    {
        SelectedSource = null;
        Refresh(selectId: null);
    }

    [RelayCommand]
    private void ShowMapAis()
    {
        IsBattle = false;
    }

    [RelayCommand]
    private void ShowBattleAis()
    {
        IsBattle = true;
    }

    private void Refresh(string? selectId)
    {
        string? source = SelectedSource?.Value;
        List<(string Id, string Name, int Stages, string Path)> designs = IsBattle
            ? [.. DesignedBattleAiLibrary.Reload().Select(design => (design.Id, design.Name, design.Stages.Length, Path.Combine(DesignedBattleAiLibrary.Directory, DesignedBattleAiStore.GetFileName(design.Id) + ".json")))]
            : [.. DesignedAiLibrary.Reload().Select(design => (design.Id, design.Name, design.Stages.Length, Path.Combine(DesignedAiLibrary.Directory, DesignedWorldAiStore.GetFileName(design.Id) + ".json")))];

        Ais.Clear();

        foreach ((string id, string name, int stageCount, string path) in designs.OrderBy(design => design.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            string stages = stageCount == 1 ? Tr("1 stage") : Tr("{0} stages", stageCount);
            Ais.Add(new AiEditorItem(id, name, $"{stages} · {File.GetLastWriteTime(path):yyyy-MM-dd}"));
        }

        Sources.Clear();

        foreach (AiBrainInfo info in IsBattle ? AiBrainRegistry.BattleBrains : AiBrainRegistry.WorldBrains)
        {
            Sources.Add(new MapEditorOption<string>(info.Id, AiBrainFormatter.FormatName(info.Id)));
        }

        SelectedSource = Sources.FirstOrDefault(option => option.Value == source) ?? Sources.FirstOrDefault();
        SelectedAi = Ais.FirstOrDefault(item => item.Id == selectId) ?? Ais.FirstOrDefault();
    }

    private bool CanCreate()
    {
        return !string.IsNullOrWhiteSpace(Name) && SelectedSource is not null;
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        string name = Name.Trim();

        if (IsBattle)
        {
            DesignedBattleAi battle = DesignedBattleAi.FromBuiltIn(SelectedSource!.Value, DesignedBattleAiLibrary.MakeFileName(name, Ais.Select(item => item.Id)), name);
            DesignedBattleAiLibrary.Store.Save(battle);
            Refresh(battle.Id);

            // A new AI opens straight in the designer.
            Game.ShowScreen(new AiDesignerScreenViewModel(Game, battle));

            return;
        }

        string fileName = DesignedAiLibrary.MakeFileName(name, Ais.Select(item => item.Id));
        DesignedWorldAi design = DesignedWorldAi.FromBuiltIn(SelectedSource!.Value, fileName, name);

        DesignedAiLibrary.Store.Save(design);
        Refresh(design.Id);

        // A new AI opens straight in the designer.
        Game.ShowScreen(new AiDesignerScreenViewModel(Game, design));
    }

    private bool HasSelection()
    {
        return SelectedAi is not null;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        if (SelectedAi is not { } selected)
        {
            return;
        }

        string name = Tr("{0} (copy)", selected.Name);

        if (IsBattle)
        {
            DesignedBattleAi battle = DesignedBattleAi.FromBuiltIn(selected.Id, DesignedBattleAiLibrary.MakeFileName(name, Ais.Select(item => item.Id)), name.Length > MaxNameLength ? name[..MaxNameLength] : name);
            DesignedBattleAiLibrary.Store.Save(battle);
            Refresh(battle.Id);

            return;
        }

        string fileName = DesignedAiLibrary.MakeFileName(name, Ais.Select(item => item.Id));
        DesignedWorldAi design = DesignedWorldAi.FromBuiltIn(selected.Id, fileName, name.Length > MaxNameLength ? name[..MaxNameLength] : name);

        DesignedAiLibrary.Store.Save(design);
        Refresh(design.Id);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (SelectedAi is not { } selected)
        {
            return;
        }

        Game.OpenScreen(new ConfirmPromptViewModel(
            TrSpans("Delete AI"),
            IsBattle
                ? TrSpans("Delete \"{0}\"? Games and settings that use it play Classic instead. This cannot be undone.", selected.Name)
                : TrSpans("Delete \"{0}\"? Games and settings that use it play Classic+ instead. This cannot be undone.", selected.Name),
            Tr("Delete"),
            ConfirmPromptType.Danger,
            () =>
            {
                if (IsBattle)
                {
                    DesignedBattleAiLibrary.Store.Delete(selected.Id);
                }
                else
                {
                    DesignedAiLibrary.Store.Delete(selected.Id);
                }

                Refresh(selectId: null);

                return Task.CompletedTask;
            }));
    }

    private bool CanEdit()
    {
        return SelectedAi is not null;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (IsBattle)
        {
            if (SelectedAi is { } chosen && DesignedBattleAiLibrary.Store.LoadAll().FirstOrDefault(design => design.Id == chosen.Id) is { } battle)
            {
                Game.ShowScreen(new AiDesignerScreenViewModel(Game, battle));
            }

            return;
        }

        if (SelectedAi is { } selected &&
            DesignedAiLibrary.Store.LoadAll().FirstOrDefault(design => design.Id == selected.Id) is { } design)
        {
            Game.ShowScreen(new AiDesignerScreenViewModel(Game, design));
        }
    }

    [RelayCommand]
    private void Back()
    {
        TryClose();
    }
}
