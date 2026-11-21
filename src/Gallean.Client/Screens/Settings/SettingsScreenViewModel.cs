using System.ComponentModel;
using Gallean.Configuration;
using Gallean.Engine;

namespace Gallean.Client;

internal sealed class SettingsScreenViewModel : AbstractScreenViewModel
{
    private readonly GameViewModel gameViewModel;
    private int soundVolume;
    private int musicVolume;
    private int renderResolution;
    private int autoSaveInterval;
    private int autoSaveLimit;
    private BattleSpeed battleSpeed;
    private MovementSpeed movementSpeed;
    private UiSize uiSize;
    private bool lockCursor;
    private bool showPathOnHover;
    private Language? language;
    private string worldAi;
    private string battleAi;

    public SettingsScreenViewModel(GameViewModel gameViewModel)
    {
        this.gameViewModel = gameViewModel;

        soundVolume = gameViewModel.Settings.SoundVolume;
        musicVolume = gameViewModel.Settings.MusicVolume;
        renderResolution = gameViewModel.Settings.RenderResolution;
        autoSaveInterval = gameViewModel.Settings.AutoSaveInterval;
        autoSaveLimit = gameViewModel.Settings.AutoSaveLimit;
        battleSpeed = gameViewModel.Settings.BattleSpeed;
        movementSpeed = gameViewModel.Settings.MovementSpeed;
        uiSize = gameViewModel.Settings.UiSize;
        lockCursor = gameViewModel.Settings.LockCursor;
        showPathOnHover = gameViewModel.Settings.ShowPathOnHover;
        language = gameViewModel.Settings.Language;
        worldAi = KnownOrClassic(gameViewModel.Settings.WorldAi, WorldAis);
        battleAi = KnownOrClassic(gameViewModel.Settings.BattleAi, BattleAis);

        gameViewModel.PropertyChanged += OnGamePropertyChanged;
    }

    public int MinVolume => GameSettings.MinVolume;
    public int MaxVolume => GameSettings.MaxVolume;

    public IReadOnlyList<int> RenderResolutions { get; } = [50, 75, 100];
    public IReadOnlyList<UiSize> UiSizes => Enum.GetValues<UiSize>();
    public IReadOnlyList<int> AutoSaveIntervals => [1, 3, 5, 10, 0];
    public IReadOnlyList<int> AutoSaveLimits { get; } = [1, 3, 5, 10, 20];
    public IReadOnlyList<BattleSpeed> BattleSpeeds => Enum.GetValues<BattleSpeed>();
    public IReadOnlyList<MovementSpeed> MovementSpeeds => Enum.GetValues<MovementSpeed>();
    public IReadOnlyList<Language> Languages { get; } = Enum.GetValues<Language>();
    public IReadOnlyList<string> WorldAis { get; } = [.. AiBrainRegistry.WorldBrains.Select(brain => brain.Id)];
    public IReadOnlyList<string> BattleAis { get; } = [.. AiBrainRegistry.BattleBrains.Select(brain => brain.Id)];

    // Gallean Upgrade: the AIs used for AI players (docs/ai-switching.md). Not saved in games.
    public string WorldAi
    {
        get => worldAi;
        set
        {
            if (SetProperty(ref worldAi, value))
            {
                OnPropertyChanged(nameof(WorldAiDescription));
                Apply();
                AskAboutOwnAis(own => own.WorldAi, own => own with { WorldAi = null }, isMap: true);
            }
        }
    }

    public string BattleAi
    {
        get => battleAi;
        set
        {
            if (SetProperty(ref battleAi, value))
            {
                OnPropertyChanged(nameof(BattleAiDescription));
                Apply();
                AskAboutOwnAis(own => own.BattleAi, own => own with { BattleAi = null }, isMap: false);
            }
        }
    }

    public string WorldAiDescription => AiBrainFormatter.FormatWorldDescription(worldAi);
    public string BattleAiDescription => AiBrainFormatter.FormatBattleDescription(battleAi);

    public int SoundVolume
    {
        get => soundVolume;
        set
        {
            if (SetProperty(ref soundVolume, value))
            {
                Apply();
            }
        }
    }

    public int MusicVolume
    {
        get => musicVolume;
        set
        {
            if (SetProperty(ref musicVolume, value))
            {
                Apply();
            }
        }
    }

    public int RenderResolution
    {
        get => renderResolution;
        set
        {
            if (SetProperty(ref renderResolution, value))
            {
                Apply();
            }
        }
    }

    public UiSize UiSize
    {
        get => uiSize;
        set
        {
            if (SetProperty(ref uiSize, value))
            {
                Apply();
            }
        }
    }

    public int AutoSaveInterval
    {
        get => autoSaveInterval;
        set
        {
            if (SetProperty(ref autoSaveInterval, value))
            {
                Apply();
            }
        }
    }

    public int AutoSaveLimit
    {
        get => autoSaveLimit;
        set
        {
            if (SetProperty(ref autoSaveLimit, value))
            {
                Apply();
            }
        }
    }

    public BattleSpeed BattleSpeed
    {
        get => battleSpeed;
        set
        {
            if (SetProperty(ref battleSpeed, value))
            {
                Apply();
            }
        }
    }

    public MovementSpeed MovementSpeed
    {
        get => movementSpeed;
        set
        {
            if (SetProperty(ref movementSpeed, value))
            {
                Apply();
            }
        }
    }

    public bool LockCursor
    {
        get => lockCursor;
        set
        {
            if (SetProperty(ref lockCursor, value))
            {
                Apply();
            }
        }
    }

    public bool ShowPathOnHover
    {
        get => showPathOnHover;
        set
        {
            if (SetProperty(ref showPathOnHover, value))
            {
                Apply();
            }
        }
    }

    public Language Language
    {
        get => language ?? gameViewModel.Settings.EffectiveLanguage;
        set
        {
            if (SetProperty(ref language, value))
            {
                Apply();
            }
        }
    }

    public bool Windowed
    {
        get => gameViewModel.IsWindowed;
        set => gameViewModel.IsWindowed = value;
    }

    public override void Dispose()
    {
        gameViewModel.PropertyChanged -= OnGamePropertyChanged;

        base.Dispose();
    }

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    private void Apply()
    {
        gameViewModel.UpdateSettings(GameSettings.Create(soundVolume, musicVolume, gameViewModel.IsWindowed, renderResolution, autoSaveInterval, autoSaveLimit, battleSpeed, uiSize, lockCursor, movementSpeed, language) with
        {
            ShowPathOnHover = showPathOnHover,
            WorldAi = worldAi == AiBrainRegistry.ClassicId ? null : worldAi,
            BattleAi = battleAi == AiBrainRegistry.ClassicId ? null : battleAi
        });
    }

    // Gallean Upgrade: players with their own AI keep it unless the player confirms switching
    // everyone (docs/ai-switching.md). Only asked during a game, outside battles.
    private void AskAboutOwnAis(Func<WorldAiOverride, string?> part, Func<WorldAiOverride, WorldAiOverride> clearPart, bool isMap)
    {
        if (gameViewModel.RootScreen is not AbstractGameScreenViewModel gameScreen || gameScreen.GameModel.Snapshot.BattleState is not null)
        {
            return;
        }

        GameModel gameModel = gameScreen.GameModel;
        int count = AiPlayersPanel.CountOwnAis(gameModel.Snapshot.WorldState, part);

        if (count == 0)
        {
            return;
        }

        TextSpans message = (isMap, count == 1) switch
        {
            (true, true) => TrSpans("One computer player in this game has its own Map AI and keeps it. Switch it to the new choice too?"),
            (true, false) => TrSpans("{0} computer players in this game have their own Map AI and keep it. Switch them to the new choice too?", count),
            (false, true) => TrSpans("One computer player in this game has its own Battle AI and keeps it. Switch it to the new choice too?"),
            (false, false) => TrSpans("{0} computer players in this game have their own Battle AI and keep it. Switch them to the new choice too?", count)
        };

        gameViewModel.OpenScreen(new ConfirmPromptViewModel(
            Tr("AI players"),
            message,
            Tr("Switch everyone"),
            ConfirmPromptType.Primary,
            () => AiPlayersPanel.ClearAsync(gameModel, clearPart)));
    }

    private static string KnownOrClassic(string? id, IReadOnlyList<string> known)
    {
        return id is not null && known.Contains(id) ? id : AiBrainRegistry.ClassicId;
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameViewModel.IsWindowed))
        {
            OnPropertyChanged(nameof(Windowed));
        }
    }
}
