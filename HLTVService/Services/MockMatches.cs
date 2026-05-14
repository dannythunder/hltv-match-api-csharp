using HLTVService.Models;

namespace HLTVService.Services;

public static class MockMatches
{
    public static IReadOnlyCollection<Match> Live()
    {
        var now = DateTimeOffset.Now;
        return
        [
            new()
            {
                MatchId = "2386680",
                Team1Name = "Players",
                Team2Name = "UnderDogs",
                Team1Logo = "https://www.hltv.org/dynamic-svg/teamplaceholder?letter=P",
                Team2Logo = "https://www.hltv.org/dynamic-svg/teamplaceholder?letter=U",
                Team1Score = 3,
                Team2Score = 8,
                Team1MapWins = 0,
                Team2MapWins = 0,
                Format = "Live",
                Event = "CCT Season 3 South America Series 5",
                IsLive = true,
                MatchUrl = "https://www.hltv.org/matches/2386680/players-vs-underdogs-cct-season-3-south-america-series-5",
                LastUpdated = now
            },
            new()
            {
                MatchId = "2386722",
                Team1Name = "kONO",
                Team2Name = "FORZE Reload",
                Team1Logo = "https://img-cdn.hltv.org/teamlogo/rW5srPjiZnkwa3TlC4vJFi.png?ixlib=java-2.1.0&w=50&s=1b6453d6089b4ea66937a105536973b6",
                Team2Logo = "https://img-cdn.hltv.org/teamlogo/YJnWBDwmRDJ9o2uOUrhNeQ.png?ixlib=java-2.1.0&w=50&s=83f5ce35b3f98833671987bc637c74b7",
                Team1Score = 3,
                Team2Score = 1,
                Team1MapWins = 1,
                Team2MapWins = 1,
                Format = "Live",
                Event = "NODWIN Clutch Series 1",
                IsLive = true,
                MatchUrl = "https://www.hltv.org/matches/2386722/kono-vs-forze-reload-nodwin-clutch-series-1",
                LastUpdated = now
            }
        ];
    }

    public static IReadOnlyCollection<Match> Upcoming()
    {
        var now = DateTimeOffset.Now;
        return
        [
            new()
            {
                MatchId = "2386759",
                Team1Name = "Dusty Roses",
                Team2Name = "CAPIVARAS",
                Team1Logo = "https://img-cdn.hltv.org/teamlogo/PNUX5aK9d4-WkrskZv6qK3.png?ixlib=java-2.1.0&w=50&s=414fd80ef34e35c9dbbe59a695efb2d2",
                Team2Logo = "https://www.hltv.org/dynamic-svg/teamplaceholder?letter=C",
                Format = "bo3",
                Event = "ESL Impact League Season 8 South America",
                MatchTime = now.AddHours(2),
                MatchUrl = "https://www.hltv.org/matches/2386759/dusty-roses-vs-capivaras-esl-impact-league-season-8-south-america",
                LastUpdated = now
            },
            new()
            {
                MatchId = "2386756",
                Team1Name = "Overpeek",
                Team2Name = "BIG EQUIPA",
                Team1Logo = "https://img-cdn.hltv.org/teamlogo/dPb1EGDqNC4tk2Z3xQhN-j.png?ixlib=java-2.1.0&w=50&s=f9ff80bbdc0ae1c36fc9ae1c89f8e653",
                Team2Logo = "https://img-cdn.hltv.org/teamlogo/6UagLkzIYk5UVlCFmEYXt1.png?ixlib=java-2.1.0&w=50&s=d2655c1d0158cda2f0bdaaeced552421",
                Format = "bo3",
                Event = "ESL Impact League Season 8 Europe",
                MatchTime = now.AddHours(4),
                MatchUrl = "https://www.hltv.org/matches/2386756/overpeek-vs-big-equipa-esl-impact-league-season-8-europe",
                LastUpdated = now
            }
        ];
    }
}
