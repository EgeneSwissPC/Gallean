using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// The land spread preview, at the top of the economy panel: a turn bar like the unit level bar (-10, -, +, +10)
// redraws the map with the land after that many turns of the game's spread (CustomMapSpreadPreview), and the
// panel's numbers follow. Nothing is edited while it shows a later turn: a click on the map goes back to the map.
internal sealed partial class MapEditorScreenViewModel
{
    private CustomMapSpreadPreview? spreadPreview;
    private WorldState? spreadState;
    private int spreadVersion;

    // Steps expected to take longer than this run on a background thread (a 512 map with 150 villages and rods
    // takes about 0.2 s a turn), so the editor stays responsive; nearer steps are shown at once.
    private static readonly TimeSpan SpreadInlineBudget = TimeSpan.FromMilliseconds(150);

    // A far step is being computed; the panel says so and the map keeps the last turn shown until it is ready.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpreadTurnText))]
    public partial bool IsSpreadComputing { get; private set; }

    // The running background step, for tests.
    public Task SpreadTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpreadPreview), nameof(SpreadTurnText))]
    [NotifyCanExecuteChangedFor(nameof(SpreadDownCommand), nameof(SpreadDownTenCommand), nameof(BackToMapCommand))]
    [NotifyCanExecuteChangedFor(nameof(SpreadUpCommand), nameof(SpreadUpTenCommand))]
    public partial int SpreadTurns { get; set; }

    public bool IsSpreadPreview => SpreadTurns > 0;

    public string SpreadTurnText => IsSpreadComputing ? Tr("Turn {0}: computing...", SpreadTurns) : SpreadTurns switch
    {
        0 => Tr("The map now"),
        1 => Tr("After 1 turn"),
        _ => Tr("After {0} turns", SpreadTurns)
    };

    private bool CanSpreadDown => SpreadTurns > 0;

    private bool CanSpreadUp => SpreadTurns < CustomMapSpreadPreview.MaxTurns;

    // The state the map view and the economy panel show: the spread preview's turn, or the map as it is.
    private WorldState ShownState => spreadState ?? state;

    [RelayCommand(CanExecute = nameof(CanSpreadDown))]
    private void SpreadDown() => SpreadTurns--;

    [RelayCommand(CanExecute = nameof(CanSpreadDown))]
    private void SpreadDownTen() => SpreadTurns = Math.Max(0, SpreadTurns - 10);

    [RelayCommand(CanExecute = nameof(CanSpreadUp))]
    private void SpreadUp() => SpreadTurns++;

    [RelayCommand(CanExecute = nameof(CanSpreadUp))]
    private void SpreadUpTen() => SpreadTurns = Math.Min(CustomMapSpreadPreview.MaxTurns, SpreadTurns + 10);

    [RelayCommand(CanExecute = nameof(CanSpreadDown))]
    private void BackToMap() => SpreadTurns = 0;

    partial void OnSpreadTurnsChanged(int value)
    {
        int version = ++spreadVersion;

        if (value <= 0)
        {
            spreadState = null;
            IsSpreadComputing = false;
            RefreshPreview();
            return;
        }

        // A new preview after any change of the map (the shown state is replaced on every edit).
        if (spreadPreview is null || !ReferenceEquals(spreadPreview.Start, state))
        {
            spreadPreview = new CustomMapSpreadPreview(state, editorGame.Session.EngineContext);
        }

        CustomMapSpreadPreview preview = spreadPreview;
        int missing = value - preview.ComputedTurns;

        if (missing <= 0 || preview.ComputedTurns == 0 || preview.AverageTurnTime * missing <= SpreadInlineBudget)
        {
            spreadState = preview.GetState(value);
            IsSpreadComputing = false;
            RefreshPreview();
            return;
        }

        IsSpreadComputing = true;
        SpreadTask = ComputeSpreadAsync(preview, value, version);
    }

    private async Task ComputeSpreadAsync(CustomMapSpreadPreview preview, int turns, int version)
    {
        WorldState result = await Task.Run(() => preview.GetState(turns));

        if (version == spreadVersion)
        {
            spreadState = result;
            IsSpreadComputing = false;
            RefreshPreview();
        }
    }

    // Any edit (click, stroke, undo, closing the panel) goes back to the map first.
    private bool LeaveSpreadPreview()
    {
        if (SpreadTurns == 0)
        {
            return false;
        }

        SpreadTurns = 0;

        return true;
    }

    private MapEditorPlacementPreview SpreadHoverPreview()
    {
        return new MapEditorPlacementPreview([], null, null, null, Tr("{0} (preview, nothing is edited): click goes back to the map", SpreadTurnText));
    }
}
