using System.Text.Json.Serialization;

namespace HLTVService.Models;

internal sealed record BrowserMatch
{
    [JsonPropertyName("matchId")]
    public string? MatchId { get; init; }

    [JsonPropertyName("team1Name")]
    public string? Team1Name { get; init; }

    [JsonPropertyName("team2Name")]
    public string? Team2Name { get; init; }

    [JsonPropertyName("team1Logo")]
    public string? Team1Logo { get; init; }

    [JsonPropertyName("team2Logo")]
    public string? Team2Logo { get; init; }

    [JsonPropertyName("team1Score")]
    public int? Team1Score { get; init; }

    [JsonPropertyName("team2Score")]
    public int? Team2Score { get; init; }

    [JsonPropertyName("team1MapWins")]
    public int? Team1MapWins { get; init; }

    [JsonPropertyName("team2MapWins")]
    public int? Team2MapWins { get; init; }

    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("event")]
    public string? Event { get; init; }

    [JsonPropertyName("matchTimeUnix")]
    public long? MatchTimeUnix { get; init; }

    [JsonPropertyName("matchUrl")]
    public string? MatchUrl { get; init; }

    [JsonPropertyName("isLive")]
    public bool IsLive { get; init; }
}
