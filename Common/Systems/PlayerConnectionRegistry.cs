namespace DaybreakDamageTracker.Common.Systems;

internal readonly record struct EncounterPlayerKey(int PlayerIndex, int Generation);

internal static class PlayerConnectionRegistry
{
    private static bool[] _wasActive = new bool[Main.maxPlayers];
    private static int[] _generation = new int[Main.maxPlayers];

    public static void Reset()
    {
        _wasActive = new bool[Main.maxPlayers];
        _generation = new int[Main.maxPlayers];
    }

    public static void Update()
    {
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            bool active = Main.player[i].active;
            if (active && !_wasActive[i])
                _generation[i] = _generation[i] == int.MaxValue ? 1 : _generation[i] + 1;
            _wasActive[i] = active;
        }
    }

    public static EncounterPlayerKey GetCurrent(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
            return new EncounterPlayerKey(playerIndex, 0);

        if (Main.player[playerIndex].active && !_wasActive[playerIndex])
        {
            _generation[playerIndex] = _generation[playerIndex] == int.MaxValue ? 1 : _generation[playerIndex] + 1;
            _wasActive[playerIndex] = true;
        }

        return new EncounterPlayerKey(playerIndex, _generation[playerIndex]);
    }

    public static bool IsCurrent(EncounterPlayerKey key)
        => key.PlayerIndex >= 0 &&
           key.PlayerIndex < Main.maxPlayers &&
           Main.player[key.PlayerIndex].active &&
           GetCurrent(key.PlayerIndex).Generation == key.Generation;

    public static void MarkDisconnected(int playerIndex)
    {
        if (playerIndex >= 0 && playerIndex < Main.maxPlayers)
            _wasActive[playerIndex] = false;
    }
}
