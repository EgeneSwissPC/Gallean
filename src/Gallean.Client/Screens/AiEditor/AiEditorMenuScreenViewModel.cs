using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

internal sealed record AiEditorItem(string Id, string Name, string Details);

// Gallean Upgrade (AI editor, docs/ai-designer.md, phase 2): the AI editor mode's first screen. Lists the
// player's designed map AIs and creates new ones from a built-in AI or one of theirs.
internal sealed partial class AiEditorMenuScreenViewModel : AbstractScreenViewModel
{
    public const int MaxNameLength = 40;

    private readonly MusicQueue musicQueue = MusicQueue.Create([new AssetSoundKey.MenuMusic()], isLooping: true);

    public AiEditorMenuScreenViewModel(GameViewModel game)
    {
        Game = game;
        Name = Tr("My AI");
        Background = MainMenuBackgroundViewModel.Create();
        Refresh(selectId: null);
    }

    public GameViewModel Game { get; }
    public MainMenuBackgroundViewModel Background { get; }
    public ObservableCollection<AiEditorItem> Ais { get; } = [];
    public ObservableCollection<MapEditorOption<string>> Sources { get; } = [];
    public string AisDirectory => DesignedAiLibrary.Directory;
    public int NameLength => MaxNameLength;

    public override bool PaintsOwnBackground => true;
    public override ScreenMusic Music => ScreenMusic.Tracks(musicQueue);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(DuplicateCommand))]
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

    private void Refresh(string? selectId)
    {
        IReadOnlyList<DesignedWorldAi> designs = DesignedAiLibrary.Reload();
        string? source = SelectedSource?.Value;

        Ais.Clear();

        foreach (DesignedWorldAi design in designs.OrderBy(design => design.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            string stages = design.Stages.Length == 1 ? Tr("1 stage") : Tr("{0} stages", design.Stages.Length);
            DateTime changed = File.GetLastWriteTime(Path.Combine(DesignedAiLibrary.Directory, DesignedWorldAiStore.GetFileName(design.Id) + ".json"));
            Ais.Add(new AiEditorItem(design.Id, design.Name, $"{stages} · {changed:yyyy-MM-dd}"));
        }

        Sources.Clear();

        foreach (AiBrainInfo info in AiBrainRegistry.WorldBrains)
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
        string fileName = DesignedAiLibrary.MakeFileName(name, Ais.Select(item => item.Id));
        DesignedWorldAi design = DesignedWorldAi.FromBuiltIn(SelectedSource!.Value, fileName, name);

        DesignedAiLibrary.Store.Save(design);
        Refresh(design.Id);
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
            Tr("Delete AI"),
            TrSpans("Delete \"{0}\"? Games and settings that use it play Classic+ instead. This cannot be undone.", selected.Name),
            Tr("Delete"),
            ConfirmPromptType.Danger,
            () =>
            {
                DesignedAiLibrary.Store.Delete(selected.Id);
                Refresh(selectId: null);

                return Task.CompletedTask;
            }));
    }

    // The designer screen arrives in the next phase (docs/ai-designer.md, phase 3).
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
    }

    private static bool CanEdit()
    {
        return false;
    }

    [RelayCommand]
    private void Back()
    {
        TryClose();
    }
}
