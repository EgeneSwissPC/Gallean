using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

internal sealed record BattleSandboxUnitResultViewModel(AssetSpriteKey.UnitFace FaceKey, string Name, string SurvivedText, double SurvivedShare);

// One side's results: wins with the margin of error, as attacker and as defender, fled, units alive, hit points
// left, and per unit how often it survived.
internal sealed record BattleSandboxSideResultViewModel(
    string Title,
    string ArmyName,
    string AiName,
    string WinsText,
    IReadOnlyList<string> Details,
    IReadOnlyList<BattleSandboxUnitResultViewModel> Units);

// Gallean Upgrade (battle sandbox, docs/battle-ai-designer.md phase 5): Run N. Fights the battle N times headless
// with the engine's bench, in chunks of 100 so the bar moves and Cancel works between chunks (even chunks keep the
// seeds and the alternating sides exactly as one big run), then shows the results in the same popup.
internal sealed partial class BattleSandboxRunViewModel : AbstractScreenViewModel
{
    public const int ChunkSize = 100;

    private readonly AiBattleBench bench;
    private readonly BattleBenchSide armyA;
    private readonly BattleBenchSide armyB;
    private readonly BattleBenchSetup setup;
    private readonly bool alternate;
    private readonly Func<UnitDefinitionId, string> unitName;
    private readonly CancellationTokenSource cancellation = new();

    public BattleSandboxRunViewModel(AiBattleBench bench, BattleBenchSide armyA, BattleBenchSide armyB, BattleBenchSetup setup, int runs, int firstSeed, bool alternate, Func<UnitDefinitionId, string> unitName)
    {
        this.bench = bench;
        this.armyA = armyA;
        this.armyB = armyB;
        this.setup = setup;
        this.alternate = alternate;
        this.unitName = unitName;
        Runs = runs;
        FirstSeed = firstSeed;
        ProgressText = Tr("{0} of {1} battles", 0, runs);
    }

    public int Runs { get; }

    // Opens one battle of this run on the battle screen (seed, army A attacks).
    public Func<int, bool, Task<BattleScreenViewModel?>>? Watch { get; init; }
    public int FirstSeed { get; }
    public Task? RunTask { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string ProgressText { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial BattleBenchResult? Result { get; private set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<BattleSandboxSideResultViewModel> Sides { get; private set; } = [];

    public bool HasResult => Result is not null && !IsRunning;

    public bool CanWatchWin => HasResult && Watch is not null && FindRun(BattleBenchWinner.A) is not null;
    public bool CanWatchLoss => HasResult && Watch is not null && FindRun(BattleBenchWinner.B) is not null;

    private BattleBenchRun? FindRun(BattleBenchWinner winner) => Result?.Runs.FirstOrDefault(run => run.Winner == winner);

    // The first battle the attacker of the setup (army A) won / lost, as it was fought.
    [RelayCommand]
    private Task WatchWinAsync() => WatchRunAsync(FindRun(BattleBenchWinner.A));

    [RelayCommand]
    private Task WatchLossAsync() => WatchRunAsync(FindRun(BattleBenchWinner.B));

    private Task WatchRunAsync(BattleBenchRun? run)
    {
        return run is null || Watch is null ? Task.CompletedTask : Watch(run.Seed, run.ArmyAAttacks);
    }

    public Task StartAsync()
    {
        RunTask = RunAsync();

        return RunTask;
    }

    private async Task RunAsync()
    {
        IsRunning = true;
        List<BattleBenchResult> parts = [];
        int done = 0;

        try
        {
            while (done < Runs)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                int count = Math.Min(ChunkSize, Runs - done);
                int seed = FirstSeed + done;
                parts.Add(await Task.Run(() => bench.Run(armyA, armyB, setup, count, seed, alternate, Environment.ProcessorCount, cancellation.Token)));
                done += count;
                Progress = (double)done / Runs;
                ProgressText = Tr("{0} of {1} battles", done, Runs);
            }
        }
        catch (OperationCanceledException)
        {
            // Show what was fought before Cancel.
        }

        IsRunning = false;

        if (parts.Count > 0)
        {
            Result = BattleBenchResult.Combine(parts);
            BuildResults(Result);
            OnPropertyChanged(nameof(CanWatchWin));
            OnPropertyChanged(nameof(CanWatchLoss));
        }
        else
        {
            CloseSelf();
        }
    }

    private void BuildResults(BattleBenchResult result)
    {
        int draws = result.Wins(BattleBenchWinner.None);
        string sides = alternate ? Tr("sides alternating") : Tr("sides as set up");
        Summary = draws > 0
            ? Tr("{0} battles ({1}), seeds from {2}, {3} rounds on average, {4} without a winner.", result.Runs.Length, sides, FirstSeed, Format1(result.AverageRounds), draws)
            : Tr("{0} battles ({1}), seeds from {2}, {3} rounds on average.", result.Runs.Length, sides, FirstSeed, Format1(result.AverageRounds));
        Sides = [BuildSide(Tr("Attacker"), armyA, result.Summarize(BattleBenchWinner.A)), BuildSide(Tr("Defender"), armyB, result.Summarize(BattleBenchWinner.B))];
    }

    private BattleSandboxSideResultViewModel BuildSide(string title, BattleBenchSide side, BattleBenchSideSummary summary)
    {
        List<string> details = [];

        if (summary.Attacks > 0 && summary.Defences > 0)
        {
            details.Add(Tr("Won {0} of {1} as attacker, {2} of {3} as defender", summary.AttackWins, summary.Attacks, summary.DefenceWins, summary.Defences));
        }

        details.Add(Tr("Fled in {0} battles", summary.Fled));
        details.Add(Tr("Units alive: {0} of {1} on average", Format1(summary.AverageSurvivors), summary.Units));
        details.Add(Tr("Hit points left: {0} %", Math.Round(summary.HitpointShare * 100)));

        int runs = Result?.Runs.Length ?? 1;

        return new BattleSandboxSideResultViewModel(
            title,
            side.Army.Name == title ? string.Empty : side.Army.Name,
            AiBrainFormatter.FormatName(side.AiId),
            Tr("Wins {0} % ± {1}", Math.Round(summary.Wins.Share * 100), Math.Ceiling(summary.Wins.Margin * 100)),
            details,
            [.. summary.UnitSurvival.Select(unit => new BattleSandboxUnitResultViewModel(
                new AssetSpriteKey.UnitFace(unit.TypeId),
                unitName(unit.TypeId),
                Tr("survived {0} of {1}", unit.Survived, runs),
                (double)unit.Survived / runs))]);
    }

    private static string Format1(double value) => value.ToString("F1", System.Globalization.CultureInfo.CurrentCulture);

    [RelayCommand]
    private void Cancel()
    {
        cancellation.Cancel();
    }

    protected override bool TryClose()
    {
        if (IsRunning)
        {
            cancellation.Cancel();
            return false;
        }

        CloseSelf();

        return true;
    }
}
