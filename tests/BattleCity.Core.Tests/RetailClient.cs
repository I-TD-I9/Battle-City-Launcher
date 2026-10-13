namespace BattleCity.Tests;

/// <summary>
/// The retail online_pc.dat, which the repository cannot hold: the file BATTLE_CITY_CLIENT names,
/// or the client of an installed game.
/// </summary>
static class RetailClient
{
    public static string? Path { get; } = Locate();

    static string? Locate()
    {
        var named = Environment.GetEnvironmentVariable("BATTLE_CITY_CLIENT");
        if (!string.IsNullOrEmpty(named))
            return named;
        var found = Game.GameLocator.Find(null);
        return found?.Status == Game.GameStatus.Ready ? found.Client : null;
    }
}

/// <summary>A test that needs the retail client, skipped where it is not on the machine.</summary>
sealed class RetailClientFactAttribute : FactAttribute
{
    public RetailClientFactAttribute()
    {
        if (RetailClient.Path is null)
            Skip = "needs the game's online_pc.dat: install the game or set BATTLE_CITY_CLIENT to it";
    }
}
