namespace HLTVService.Models;

public sealed record Match
{
    public string MatchId { get; init; } = "";
    public string Team1Name { get; init; } = "";
    public string Team2Name { get; init; } = "";
    public string Team1Logo { get; init; } = "";
    public string Team2Logo { get; init; } = "";
    public int? Team1Score { get; set; }
    public int? Team2Score { get; set; }
    public int? Team1MapWins { get; set; }
    public int? Team2MapWins { get; set; }
    public string Format { get; init; } = "";
    public string Event { get; init; } = "";
    public DateTimeOffset? MatchTime { get; init; }
    public bool IsLive { get; init; }
    public string MatchUrl { get; init; } = "";
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;
}
