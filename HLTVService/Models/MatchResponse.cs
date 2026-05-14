namespace HLTVService.Models;

public sealed record MatchResponse(IReadOnlyCollection<Match> LiveMatches, IReadOnlyCollection<Match> UpcomingMatches);
