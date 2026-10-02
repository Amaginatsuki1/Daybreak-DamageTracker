namespace DaybreakDamageTracker.Common.Config;

internal static class ServerConfigValuePolicy
{
    // Native ServerSide configs are synchronized by tModLoader as one packet. These
    // structural limits plus the measured JSON byte budget below keep a valid config
    // comfortably below the 65,535-byte ModPacket ceiling.
    public const int MaximumBossOverrides = 32;
    public const int MaximumTextLength = 160;
    public const int MaximumBossKeyLength = 80;
    public const int MaximumLiteralNameLength = 80;
    public const int MaximumTypeValuesPerRole = 64;
    public const int MaximumTypeValuesPerOverride = 128;
    public const int MaximumTypeValuesAcrossOverrides = 256;
    public const int MaximumTextCodeUnitsAcrossOverrides = 8_192;
    public const int MaximumSerializedConfigUtf8Bytes = 48 * 1_024;

    public static string NormalizeText(string? value, int maximumLength)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.Length <= maximumLength)
            return text;

        int length = maximumLength;
        if (length > 0 &&
            char.IsHighSurrogate(text[length - 1]) &&
            length < text.Length &&
            char.IsLowSurrogate(text[length]))
        {
            length--;
        }
        return text[..length];
    }

    public static List<string> SanitizeListInPlace(List<string>? values)
    {
        List<string> target = values ?? [];
        List<string> normalized = target
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeText(value, MaximumTextLength))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumTypeValuesPerRole)
            .ToList();
        target.Clear();
        target.AddRange(normalized);
        return target;
    }

    public static string TrimToRemaining(string? value, ref int remaining)
    {
        if (remaining <= 0)
        {
            remaining = 0;
            return string.Empty;
        }

        string text = value ?? string.Empty;
        if (text.Length <= remaining)
        {
            remaining -= text.Length;
            return text;
        }

        string trimmed = NormalizeText(text, remaining);
        remaining -= trimmed.Length;
        return trimmed;
    }

    public static void TrimListTextToRemaining(List<string> values, ref int remaining)
    {
        for (int index = 0; index < values.Count; index++)
        {
            string trimmed = TrimToRemaining(values[index], ref remaining);
            if (trimmed.Length == 0)
            {
                values.RemoveRange(index, values.Count - index);
                return;
            }

            values[index] = trimmed;
        }
    }

    public static bool FitsSerializedConfigBudget(int utf8ByteCount)
        => utf8ByteCount >= 0 && utf8ByteCount <= MaximumSerializedConfigUtf8Bytes;

    public static void RemoveDuplicateBossKeys<T>(List<T> values, Func<T, string> keySelector)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int index = 0; index < values.Count; index++)
        {
            string key = keySelector(values[index]);
            if (string.IsNullOrWhiteSpace(key) || seen.Add(key))
                continue;

            values.RemoveAt(index);
            index--;
        }
    }

    public static int TrimListsToTotal(int maximumTotal, params List<string>[] lists)
    {
        int remaining = Math.Max(0, maximumTotal);
        int kept = 0;
        foreach (List<string> list in lists)
        {
            int keep = Math.Min(list.Count, remaining);
            if (list.Count > keep)
                list.RemoveRange(keep, list.Count - keep);
            kept += keep;
            remaining -= keep;
        }
        return kept;
    }
}
