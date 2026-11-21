using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (AI designer, docs/ai-designer.md, phase 4): the Test panel. Plays quick AI-only games
// of the AI as it is on screen (saved or not) against another AI and shows the results.
internal sealed partial class AiDesignerScreenViewModel
{
    private CancellationTokenSource? testCancellation;
    private IReadOnlyList<MapEditorOption<string>>? testMaps;

    public IReadOnlyList<MapEditorOption<string>> TestMaps => testMaps ??= LoadTestMaps();

    private IReadOnlyList<MapEditorOption<string>>? testOpponents;

    public IReadOnlyList<MapEditorOption<string>> TestOpponents => testOpponents ??=
        [.. AiBrainRegistry.WorldBrains.Where(info => info.Id != id).Select(info => new MapEditorOption<string>(info.Id, AiBrainFormatter.FormatName(info.Id)))];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunTestCommand))]
    public partial MapEditorOption<string>? TestMap { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunTestCommand))]
    public partial MapEditorOption<string>? TestOpponent { get; set; }

    [ObservableProperty]
    public partial string TestGamesText { get; set; } = "4";

    [ObservableProperty]
    public partial string TestTurnsText { get; set; } = "30";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunTestCommand), nameof(CancelTestCommand))]
    public partial bool IsTesting { get; set; }

    [ObservableProperty]
    public partial string TestProgress { get; set; } = "";

    public ObservableCollection<string> TestResults { get; } = [];

    // The running test, for tests to await.
    public Task? TestTask { get; private set; }

    private IReadOnlyList<MapEditorOption<string>> LoadTestMaps()
    {
        try
        {
            return [.. Game.ListAiTestMaps().Select(map => new MapEditorOption<string>(map.Id, Tr("{0} ({1} × {1})", map.Name, map.Size)))];
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return [];
        }
    }

    private void InitializeTest()
    {
        TestMap = TestMaps.FirstOrDefault();
        TestOpponent = TestOpponents.FirstOrDefault(option => option.Value == AiBrainRegistry.ClassicId);
    }

    private static int ParseCount(string text, int fallback, int minimum, int maximum)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }

    private bool CanRunTest() => !IsTesting && TestMap is not null && TestOpponent is not null;

    [RelayCommand(CanExecute = nameof(CanRunTest))]
    private Task RunTestAsync()
    {
        TestTask = RunTestCoreAsync();
        return TestTask;
    }

    private async Task RunTestCoreAsync()
    {
        int games = ParseCount(TestGamesText, 4, 2, 20);
        int turns = ParseCount(TestTurnsText, 30, 5, 100);
        TestGamesText = games.ToString(CultureInfo.InvariantCulture);
        TestTurnsText = turns.ToString(CultureInfo.InvariantCulture);

        string mapId = TestMap!.Value;
        AiContender mine = new(id, AiBrainRegistry.ClassicId);
        AiContender opponent = new(TestOpponent!.Value, AiBrainRegistry.ClassicId);
        DesignedWorldAi design = ToDesign();
        string myName = string.IsNullOrWhiteSpace(design.Name) ? Tr("This AI") : design.Name;
        string opponentName = TestOpponent.Label;

        // The AI as it is on screen, even unsaved, plays under its own ID; the saved ones are restored after.
        AiBrainRegistry.SetDesignedWorldAis([.. DesignedAiLibrary.Store.LoadAll().Where(other => other.Id != id), design]);

        testCancellation = new CancellationTokenSource();
        CancellationToken token = testCancellation.Token;
        IsTesting = true;
        TestResults.Clear();
        TestProgress = Tr("Preparing…");

        try
        {
            AiTournamentRunner runner = await Task.Run(() => Game.CreateAiTournament(mapId, turns), token);
            IReadOnlyList<(AiContender[] Seating, int Seed)> plan = await Task.Run(() => runner.Plan([mine, opponent], (games + 1) / 2, firstSeed: 1), token);
            AiGameResult[] results = new AiGameResult[plan.Count];
            int finished = 0;
            TestProgress = Tr("Game {0} of {1}…", 1, plan.Count);

            await Task.Run(() => Parallel.For(0, plan.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 4), CancellationToken = token }, index =>
            {
                results[index] = runner.Play(plan[index].Seating, plan[index].Seed, token);
                int done = Interlocked.Increment(ref finished);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => TestProgress = Tr("Game {0} of {1}…", Math.Min(done + 1, plan.Count), plan.Count));
            }), token);

            ShowResults(AiContenderSummary.Summarize([mine, opponent], results), myName, opponentName, results.Length, turns);
        }
        catch (OperationCanceledException)
        {
            TestProgress = Tr("Test cancelled.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or KeyNotFoundException)
        {
            TestProgress = Tr("The test could not run on this map: {0}", exception.Message);
        }
        finally
        {
            IsTesting = false;
            testCancellation.Dispose();
            testCancellation = null;
            DesignedAiLibrary.Reload();
        }
    }

    private void ShowResults(IReadOnlyList<AiContenderSummary> summaries, string myName, string opponentName, int games, int turns)
    {
        TestProgress = Tr("{0} games of up to {1} turns:", games, turns);

        foreach ((AiContenderSummary summary, string name) in summaries.Zip([myName, opponentName]))
        {
            string capture = double.IsNaN(summary.AverageFirstCapture) ? Tr("no city taken") : Tr("first city taken on turn {0}", summary.AverageFirstCapture.ToString("F0", CultureInfo.CurrentCulture));
            TestResults.Add(Tr("{0}: {1} wins, {2} draws ({3} ahead), {4} times out; {5} cities at the end; {6}; {7} ms per turn.",
                name, summary.Wins, summary.Draws, summary.Leads, summary.Defeats,
                summary.AverageCitiesAtEnd.ToString("F1", CultureInfo.CurrentCulture), capture, summary.AverageTurnMs.ToString("F0", CultureInfo.CurrentCulture)));
        }

        AddStageShares(summaries[0]);
    }

    private void AddStageShares(AiContenderSummary mine)
    {
        int total = mine.StageTurns.Values.Sum();

        if (total == 0)
        {
            return;
        }

        string stages = string.Join(", ", mine.StageTurns.OrderByDescending(pair => pair.Value).Select(pair =>
            Tr("{0} {1} %", pair.Key.Length == 0 ? Tr("(no stage)") : pair.Key, (100 * pair.Value / total).ToString(CultureInfo.CurrentCulture))));
        TestResults.Add(Tr("Time in each stage: {0}.", stages));
    }

    private bool CanCancelTest() => IsTesting;

    [RelayCommand(CanExecute = nameof(CanCancelTest))]
    private void CancelTest()
    {
        testCancellation?.Cancel();
    }
}
