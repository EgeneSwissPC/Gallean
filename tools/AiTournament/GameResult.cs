using System.Text;

namespace AiTournament;

internal sealed record PlayerResult(
    WorldPlayerId PlayerId,
    WorldRace Race,
    Contender Contender,
    bool IsWinner,
    bool IsDefeated,
    int CitiesAtStart,
    int CitiesAtEnd,
    int? FirstCaptureTurn,
    int Turns,
    double AverageTurnMs,
    double MaxTurnMs);

internal sealed record GameResult(int Seed, int Turns, string Outcome, IReadOnlyList<PlayerResult> Players, double Seconds)
{
    public string Format()
    {
        StringBuilder text = new();
        text.Append($"seed {Seed,4}: {Outcome} after {Turns} turns ({Seconds:F0} s)");

        foreach (PlayerResult player in Players)
        {
            string state = player.IsWinner ? "WIN" : player.IsDefeated ? "out" : "-";
            text.Append($"\n    P{player.PlayerId.Value} {player.Race,-8} {player.Contender,-20} {state,-3} cities {player.CitiesAtStart}->{player.CitiesAtEnd}, " +
                $"first capture {(player.FirstCaptureTurn?.ToString() ?? "-")}, AI {player.AverageTurnMs:F0} ms/turn (max {player.MaxTurnMs:F0})");
        }

        return text.ToString();
    }
}
