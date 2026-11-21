using System.Globalization;
using System.Text;

namespace AiTournament;

internal static class Summary
{
    public static string Format(IReadOnlyList<Contender> contenders, IReadOnlyList<GameResult> results)
    {
        StringBuilder text = new();
        text.AppendLine("Contender            seats  wins  draws  leads  out   win%  win turn  1st capture  cities end  AI ms/turn  max ms");

        foreach (Contender contender in contenders.Distinct())
        {
            List<(GameResult Game, PlayerResult Player)> seats = [.. results.SelectMany(game => game.Players.Where(p => p.Contender == contender).Select(p => (game, p)))];
            int wins = seats.Count(seat => seat.Player.IsWinner);
            int draws = seats.Count(seat => !seat.Player.IsWinner && !seat.Player.IsDefeated && seat.Game.Outcome == "draw");
            int outs = seats.Count(seat => seat.Player.IsDefeated);
            int leads = seats.Count(seat => seat.Game.Outcome == "draw" && IsSoleLeader(seat.Game, seat.Player));
            double winTurn = seats.Where(seat => seat.Player.IsWinner).Select(seat => (double)seat.Game.Turns).DefaultIfEmpty(double.NaN).Average();
            double firstCapture = seats.Where(seat => seat.Player.FirstCaptureTurn is not null).Select(seat => (double)seat.Player.FirstCaptureTurn!.Value).DefaultIfEmpty(double.NaN).Average();
            double cities = seats.Select(seat => (double)seat.Player.CitiesAtEnd).DefaultIfEmpty(double.NaN).Average();
            double ms = seats.Select(seat => seat.Player.AverageTurnMs).DefaultIfEmpty(double.NaN).Average();
            double max = seats.Select(seat => seat.Player.MaxTurnMs).DefaultIfEmpty(double.NaN).Max();

            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{contender,-20} {seats.Count,5} {wins,5} {draws,6} {leads,6} {outs,4} {(seats.Count == 0 ? 0 : 100.0 * wins / seats.Count),5:F0}% {winTurn,9:F1} {firstCapture,12:F1} {cities,11:F1} {ms,11:F0} {max,7:F0}"));
        }

        text.AppendLine("leads = draws (turn limit) that this contender ended with strictly the most cities.");

        return text.ToString().TrimEnd();
    }

    private static bool IsSoleLeader(GameResult game, PlayerResult player)
    {
        return game.Players.All(other => other == player || other.CitiesAtEnd < player.CitiesAtEnd);
    }

    public static string ToCsv(IReadOnlyList<GameResult> results)
    {
        StringBuilder csv = new();
        csv.AppendLine("seed,turns,outcome,player,race,world_ai,battle_ai,winner,defeated,cities_start,cities_end,first_capture_turn,player_turns,avg_turn_ms,max_turn_ms");

        foreach (GameResult game in results)
        {
            foreach (PlayerResult p in game.Players)
            {
                csv.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{game.Seed},{game.Turns},{game.Outcome},{p.PlayerId.Value},{p.Race},{p.Contender.WorldAi},{p.Contender.BattleAi},{p.IsWinner},{p.IsDefeated},{p.CitiesAtStart},{p.CitiesAtEnd},{p.FirstCaptureTurn},{p.Turns},{p.AverageTurnMs:F1},{p.MaxTurnMs:F1}"));
            }
        }

        return csv.ToString();
    }
}
