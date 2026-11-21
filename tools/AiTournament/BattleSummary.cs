using System.Globalization;
using System.Text;

namespace AiTournament;

internal static class BattleSummary
{
    public static string FormatRun(BattleBenchRun run)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"seed {run.Seed,4}: {(run.ArmyAAttacks ? "A attacks" : "B attacks")}, winner {run.Winner}, {run.Rounds} rounds; " +
            $"A {run.A.Survivors}/{run.A.Units.Length} alive {run.A.Hitpoints}/{run.A.MaxHitpoints} HP{(run.A.Fled ? " fled" : "")}; " +
            $"B {run.B.Survivors}/{run.B.Units.Length} alive {run.B.Hitpoints}/{run.B.MaxHitpoints} HP{(run.B.Fled ? " fled" : "")}");
    }

    public static string Format(BattleBenchResult result, Func<UnitDefinitionId, string> unitName)
    {
        StringBuilder text = new();
        int runs = result.Runs.Length;
        int draws = result.Runs.Count(run => run.Winner == BattleBenchWinner.None);

        string drawText = draws > 0 ? FormattableString.Invariant($", {draws} without a winner") : "";
        text.AppendLine(FormattableString.Invariant($"Runs {runs}, average {result.Runs.Average(run => run.Rounds):F1} rounds{drawText}"));
        AppendSide(text, "A", result.ArmyA, result.Runs, run => run.A, BattleBenchWinner.A, unitName);
        AppendSide(text, "B", result.ArmyB, result.Runs, run => run.B, BattleBenchWinner.B, unitName);

        return text.ToString().TrimEnd();
    }

    public static string ToCsv(BattleBenchResult result)
    {
        StringBuilder text = new();
        text.AppendLine("seed,a_attacks,winner,rounds,a_alive,a_units,a_hp,a_max_hp,a_fled,b_alive,b_units,b_hp,b_max_hp,b_fled");

        foreach (BattleBenchRun run in result.Runs)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{run.Seed},{(run.ArmyAAttacks ? 1 : 0)},{run.Winner},{run.Rounds},{run.A.Survivors},{run.A.Units.Length},{run.A.Hitpoints},{run.A.MaxHitpoints},{(run.A.Fled ? 1 : 0)}," +
                $"{run.B.Survivors},{run.B.Units.Length},{run.B.Hitpoints},{run.B.MaxHitpoints},{(run.B.Fled ? 1 : 0)}"));
        }

        return text.ToString();
    }

    private static void AppendSide(StringBuilder text, string label, BattleBenchSide side, IReadOnlyList<BattleBenchRun> runs, Func<BattleBenchRun, BattleBenchSideResult> select, BattleBenchWinner win, Func<UnitDefinitionId, string> unitName)
    {
        int wins = runs.Count(run => run.Winner == win);
        int attackWins = runs.Count(run => run.Winner == win && run.ArmyAAttacks == (win == BattleBenchWinner.A));
        int attacks = runs.Count(run => run.ArmyAAttacks == (win == BattleBenchWinner.A));
        int fled = runs.Count(run => select(run).Fled);
        double survivors = runs.Average(run => select(run).Survivors);
        double hitpoints = runs.Average(run => select(run).MaxHitpoints == 0 ? 0 : 100.0 * select(run).Hitpoints / select(run).MaxHitpoints);

        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{label} {side.Army.Name} ({side.AiId}): {wins} wins ({attackWins}/{attacks} attacking, {wins - attackWins}/{runs.Count - attacks} defending), " +
            $"fled in {fled}, {survivors:F1}/{side.Army.Units.Length} units alive, {hitpoints:F0}% HP left"));

        BattleBenchSideResult first = select(runs[0]);

        for (int index = 0; index < first.Units.Length; index++)
        {
            BattleBenchUnitResult unit = first.Units[index];
            int survived = runs.Count(run => select(run).Units[index].Survived);
            string name = unitName(unit.TypeId);

            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    {name} ({unit.TypeId.Value}, line {unit.Position.Line} flank {unit.Position.Flank}): survived {survived}/{runs.Count}"));
        }
    }
}
