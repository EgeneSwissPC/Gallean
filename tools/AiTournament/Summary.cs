using System.Globalization;
using System.Text;

namespace AiTournament;

internal static class Summary
{
    public static string FormatGame(AiGameResult game)
    {
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"seed {game.Seed,4}: {game.Outcome} after {game.Turns} turns ({game.Seconds:F0} s)");

        foreach (AiPlayerResult player in game.Players)
        {
            string state = player.IsWinner ? "WIN" : player.IsDefeated ? "out" : "-";
            text.Append(CultureInfo.InvariantCulture, $"\n    P{player.PlayerId.Value} {player.Race,-8} {player.Contender,-20} {state,-3} cities {player.CitiesAtStart}->{player.CitiesAtEnd}, " +
                $"first capture {(player.FirstCaptureTurn?.ToString(CultureInfo.InvariantCulture) ?? "-")}, AI {player.AverageTurnMs:F0} ms/turn (max {player.MaxTurnMs:F0})");

            if (!player.StageTurns.IsEmpty)
            {
                text.Append(", stages " + FormatStages(player.StageTurns));
            }
        }

        return text.ToString();
    }

    public static string Format(IReadOnlyList<AiContender> contenders, IReadOnlyList<AiGameResult> results)
    {
        IReadOnlyList<AiContenderSummary> summaries = AiContenderSummary.Summarize(contenders, results);
        int width = Math.Max(20, contenders.Max(contender => contender.ToString().Length));
        StringBuilder text = new();
        text.AppendLine("Contender".PadRight(width) + " seats  wins  draws  leads  out   win%  win turn  1st capture  cities end  AI ms/turn  max ms");

        foreach (AiContenderSummary s in summaries)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{s.Contender.ToString().PadRight(width)} {s.Seats,5} {s.Wins,5} {s.Draws,6} {s.Leads,6} {s.Defeats,4} {(s.Seats == 0 ? 0 : 100.0 * s.Wins / s.Seats),5:F0}% {s.AverageWinTurn,9:F1} {s.AverageFirstCapture,12:F1} {s.AverageCitiesAtEnd,11:F1} {s.AverageTurnMs,11:F0} {s.MaxTurnMs,7:F0}"));
        }

        foreach (AiContenderSummary s in summaries.Where(s => !s.StageTurns.IsEmpty))
        {
            text.AppendLine($"Stages of {s.Contender}: {FormatStages(s.StageTurns)}");
        }

        text.AppendLine("leads = draws (turn limit) that this contender ended with strictly the most cities.");

        return text.ToString().TrimEnd();
    }

    private static string FormatStages(IReadOnlyDictionary<string, int> stages)
    {
        int total = stages.Values.Sum();

        return string.Join(", ", stages.OrderByDescending(pair => pair.Value).Select(pair =>
            string.Create(CultureInfo.InvariantCulture, $"{(pair.Key.Length == 0 ? "(none)" : pair.Key)} {100.0 * pair.Value / total:F0}%")));
    }

    public static string ToCsv(IReadOnlyList<AiGameResult> results)
    {
        StringBuilder csv = new();
        csv.AppendLine("seed,turns,outcome,player,race,world_ai,battle_ai,winner,defeated,cities_start,cities_end,first_capture_turn,player_turns,avg_turn_ms,max_turn_ms");

        foreach (AiGameResult game in results)
        {
            foreach (AiPlayerResult p in game.Players)
            {
                csv.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{game.Seed},{game.Turns},{game.Outcome},{p.PlayerId.Value},{p.Race},{p.Contender.WorldAi},{p.Contender.BattleAi},{p.IsWinner},{p.IsDefeated},{p.CitiesAtStart},{p.CitiesAtEnd},{p.FirstCaptureTurn},{p.Turns},{p.AverageTurnMs:F1},{p.MaxTurnMs:F1}"));
            }
        }

        return csv.ToString();
    }
}
