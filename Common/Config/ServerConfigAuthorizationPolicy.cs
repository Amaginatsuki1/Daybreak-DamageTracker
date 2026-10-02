namespace DaybreakDamageTracker.Common.Config;

internal static class ServerConfigAuthorizationPolicy
{
    public static bool CanEdit(bool isHost, bool hasExplicitConnectionGrant)
        => isHost || hasExplicitConnectionGrant;
}
